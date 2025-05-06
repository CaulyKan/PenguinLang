using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using PenguinLangSyntax;

namespace BabyPenguin.VirtualMachine
{
    public class DAP : DebugAdapterBase
    {
        BabyPenguinVM? vm;
        bool stopAtEntry;
        object syncObject = new object();
        private System.Threading.Thread debugThread;
        private ManualResetEvent runEvent = new ManualResetEvent(true);
        private StoppedEvent.ReasonValue? stopReason;
        private bool stopped = true;

        public BabyPenguinVM VM => vm ?? throw new InvalidOperationException("VM not initialized.");

        IEnumerable<RuntimeFrameResult?>? runtimeControl;
        public IEnumerable<RuntimeFrameResult?> RuntimeControl => runtimeControl ?? throw new InvalidOperationException("Runtime control not initialized.");

        public DAP()
        {
            base.InitializeProtocolClient(Console.OpenStandardInput(), Console.OpenStandardOutput());
        }

        private void SendOutput(string message)
        {
            var e = new OutputEvent(message)
            {
                Category = OutputEvent.CategoryValue.Stdout
            };
            this.Protocol.SendEvent(e);
        }

        private void SendDebug(string message)
        {
            var e = new OutputEvent(message)
            {
                Category = OutputEvent.CategoryValue.Console
            };
            this.Protocol.SendEvent(e);
        }

        private void RequestStop(StoppedEvent.ReasonValue reason, int threadId = 0)
        {
            lock (this.syncObject)
            {
                this.stopReason = reason;
                this.stopped = true;
                this.runEvent.Reset();
                this.Protocol.SendEvent(new StoppedEvent(reason)
                {
                    ThreadId = 0,
                    AllThreadsStopped = true
                });
            }
        }

        private void DebugThreadProc()
        {
            this.runEvent.WaitOne();
            foreach (var _ in this.RuntimeControl)
            {
                this.RequestStop(StoppedEvent.ReasonValue.Breakpoint);
                this.runEvent.WaitOne();
            }

            Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Exited, threadId: 0));
            Protocol.SendEvent(new ExitedEvent(exitCode: 0));
            Protocol.SendEvent(new TerminatedEvent());
        }

        protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
        {
            this.Protocol.SendEvent(new InitializedEvent());

            return new InitializeResponse()
            {
                SupportsConfigurationDoneRequest = true,
                SupportsSingleThreadExecutionRequests = true,
            };
        }

        protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
        {
            string fileName = arguments.ConfigurationProperties.GetValueAsString("program");
            if (String.IsNullOrEmpty(fileName))
            {
                throw new ProtocolException("Launch failed because launch configuration did not specify 'program'.");
            }

            fileName = Path.GetFullPath(fileName);
            if (!File.Exists(fileName))
            {
                throw new ProtocolException("Launch failed because 'program' files does not exist.");
            }

            var writer = new StringWriter();
            var compiler = new SemanticCompiler(new ErrorReporter(writer));
            compiler.AddFile(fileName);
            var model = compiler.Compile();
            SendDebug(writer.ToString() + "\n");
            vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;
            vm.Global.PrintFunc = SendOutput;
            vm.Global.DebugFunc = SendDebug;

            this.stopAtEntry = arguments.ConfigurationProperties.GetValueAsBool("stopAtEntry") ?? false;

            return new LaunchResponse();
        }

        protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
        {
            Protocol.SendEvent(
                new ThreadEvent(
                    reason: ThreadEvent.ReasonValue.Started,
                    threadId: 0));

            VM.Initialize();
            runtimeControl = VM.StartFrame!.Run();
            this.runEvent.Reset();

            this.debugThread = new System.Threading.Thread(this.DebugThreadProc);
            this.debugThread.Name = "Debug Loop Thread";
            this.debugThread.Start();

            if (this.stopAtEntry)
            {
                Continue(true);
            }
            else
            {
                Continue(false);
            }

            return new ConfigurationDoneResponse();
        }

        protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
        {
            if (this.vm != null)
                this.Continue(step: false);

            if (this.debugThread != null)
                this.debugThread.Join();

            return new DisconnectResponse();
        }


        protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
        {
            this.Continue(step: false);
            return new ContinueResponse();
        }

        protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
        {
            this.Continue(step: true);
            return new StepInResponse();
        }

        protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
        {
            this.Continue(step: true);
            return new StepOutResponse();
        }

        protected override NextResponse HandleNextRequest(NextArguments arguments)
        {
            this.Continue(step: true);
            return new NextResponse();
        }

        private void Continue(bool step)
        {
            lock (this.syncObject)
            {
                if (step)
                {
                    this.stopReason = StoppedEvent.ReasonValue.Step;
                    this.VM.Global.StepMode = true;
                }
                else
                {
                    this.stopReason = null;
                    this.VM.Global.StepMode = false;
                }
            }

            this.stopped = false;
            this.runEvent.Set();
        }


        protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
        {
            if (!this.stopped)
            {
                throw new ProtocolException("Not in break mode!");
            }

            return new ThreadsResponse([new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread(0, "Main Thread")]);
        }

        protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments args)
        {
            List<StackFrame> stackFrames = [];

            foreach (var frame in this.VM.Global.StackFrames)
            {
                stackFrames.Add(new StackFrame(frame.GetHashCode(), frame.CodeContainer.FullName, frame.CurrentSourceLocation.RowStart, frame.CurrentSourceLocation.ColStart)
                {
                    Source = new Source { Path = frame.CodeContainer.SourceLocation.FileName }
                });
            }

            return new StackTraceResponse(stackFrames);
        }

        protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
        {
            var frame = arguments.FrameId;
            return new ScopesResponse([new Scope("Locals", frame, false)]);
        }

        protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
        {
            List<Variable> variables = [];
            foreach (var frame in this.VM.Global.StackFrames)
            {
                if (frame.GetHashCode() == arguments.VariablesReference)
                {
                    foreach (var var in frame.LocalVariables)
                    {
                        variables.Add(new Variable(var.Value.Symbol.Name, var.Value.ValueToString, arguments.VariablesReference));
                    }
                }
            }
            return new VariablesResponse(variables);
        }

        protected override SourceResponse HandleSourceRequest(SourceArguments arguments)
        {
            var file = arguments.Source.Path;
            if (File.Exists(file))
                return new SourceResponse(File.ReadAllText(file));
            else throw new ProtocolException("file not found");
        }

        protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
        {
            return new SetBreakpointsResponse();
        }

        protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
        {
            return new SetExceptionBreakpointsResponse();
        }
    }

}