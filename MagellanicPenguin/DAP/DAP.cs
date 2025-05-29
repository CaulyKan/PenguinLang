using BabyPenguin;
using BabyPenguin.VirtualMachine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using PenguinLangSyntax;

namespace MagellanicPenguin
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var dap = new DAP();
            dap.Protocol.Run();
        }
    }

    public class DAP : DebugAdapterBase
    {
        BabyPenguinVM? vm;
        bool stopAtEntry;
        object syncObject = new object();
        private System.Threading.Thread? debugThread;
        private AutoResetEvent runEvent = new AutoResetEvent(true);
        private StoppedEvent.ReasonValue? stopReason;
        private bool stopped = true;
        private Dictionary<string, HashSet<(int line, int column)>> currentBreakpoints = new();

        public BabyPenguinVM VM => vm ?? throw new InvalidOperationException("VM not initialized.");

        IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>>? runtimeControl;

        public IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>> RuntimeControl => runtimeControl ?? throw new InvalidOperationException("Runtime control not initialized.");

        public RuntimeFrame? CurrentFrame { get; set; }

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
            foreach (var result in this.RuntimeControl)
            {
                if (result.IsLeft)
                {
                    if (result.Left!.Reason == RuntimeBreakReason.Breakpoint)
                        this.RequestStop(StoppedEvent.ReasonValue.Breakpoint);
                    else if (result.Left.Reason == RuntimeBreakReason.Exception)
                        this.RequestStop(StoppedEvent.ReasonValue.Exception);
                    else if (result.Left.Reason == RuntimeBreakReason.Step)
                        this.RequestStop(StoppedEvent.ReasonValue.Step);

                    CurrentFrame = result.Left.CurrentFrame;
                }
                else
                    continue;
                this.runEvent.WaitOne();
            }

            var output = this.VM.CollectOutput();
            SendDebug("===========Program Output=============\n");
            SendOutput(output);
            SendDebug("======================================\n");
            SendDebug("Program exited with code " + this.VM.Global.ExitCode.ToString());

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
            try
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
            catch (Exception e)
            {
                throw new ProtocolException(e.Message);
            }
        }

        protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
        {
            try
            {
                Protocol.SendEvent(
                    new ThreadEvent(
                        reason: ThreadEvent.ReasonValue.Started,
                        threadId: 0));

                VM.Initialize();

                // // Insert all saved breakpoints
                // foreach (var fileBreakpoints in currentBreakpoints)
                // {
                //     foreach (var (line, column) in fileBreakpoints.Value)
                //     {
                //         var location = new SourceLocation(fileBreakpoints.Key, "", line, line, column, column);
                //         var success = VM.InsertBreakPoint(location);
                //         SendDebug($"Breakpoint set {(success ? "successfully" : "unsuccessfully")} at {location.FileName}@{location.RowStart}:{location.ColStart}\n");
                //     }
                // }

                runtimeControl = VM.StartFrame!.Run();
                this.runEvent.Reset();

                if (this.stopAtEntry)
                {
                    Continue(RuntimeGlobal.StepModeEnum.StepIn);
                }
                else
                {
                    Continue(RuntimeGlobal.StepModeEnum.Run);
                }

                this.debugThread = new System.Threading.Thread(this.DebugThreadProc);
                this.debugThread.Name = "Debug Loop Thread";
                this.debugThread.Start();

                return new ConfigurationDoneResponse();
            }
            catch (Exception e)
            {
                throw new ProtocolException(e.Message);
            }
        }

        protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
        {
            if (this.vm != null)
                this.Continue(RuntimeGlobal.StepModeEnum.Run);

            if (this.debugThread != null)
                this.debugThread.Join();

            return new DisconnectResponse();
        }


        protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
        {
            this.Continue(RuntimeGlobal.StepModeEnum.Run);
            return new ContinueResponse();
        }

        protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
        {
            this.Continue(RuntimeGlobal.StepModeEnum.StepIn);
            return new StepInResponse();
        }

        protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
        {
            this.Continue(RuntimeGlobal.StepModeEnum.StepOut);
            return new StepOutResponse();
        }

        protected override NextResponse HandleNextRequest(NextArguments arguments)
        {
            this.Continue(RuntimeGlobal.StepModeEnum.StepOver);
            return new NextResponse();
        }

        private void Continue(RuntimeGlobal.StepModeEnum step)
        {
            lock (this.syncObject)
            {
                if (step != RuntimeGlobal.StepModeEnum.Run)
                {
                    this.stopReason = StoppedEvent.ReasonValue.Step;
                    this.VM.Global.StepMode = step;
                }
                else
                {
                    this.stopReason = null;
                    this.VM.Global.StepMode = step;
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
            var frame = this.CurrentFrame;

            while (frame != null)
            {
                stackFrames.Add(new StackFrame(frame.GetHashCode(), frame.CodeContainer.FullName, frame.CurrentSourceLocation.RowStart, frame.CurrentSourceLocation.ColStart)
                {
                    Source = new Source { Path = frame.CodeContainer.SourceLocation.FileName }
                });
                frame = frame.ParentFrame;
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
            try
            {
                List<Variable> variables = [];

                if (ReferenceRuntimeValue.AllObjects.TryGetValue((ulong)arguments.VariablesReference, out var obj))
                {
                    foreach (var kvp in obj.Fields)
                    {
                        variables.Add(new Variable(kvp.Key, kvp.Value.ToString(), (kvp.Value is ReferenceRuntimeValue rv) ? (int)rv.RefId : 0));
                    }
                    return new VariablesResponse(variables);
                }
                else
                {
                    var frame = this.CurrentFrame;
                    while (frame != null)
                    {
                        if (frame.GetHashCode() == arguments.VariablesReference)
                        {
                            foreach (var v in frame.LocalVariables)
                            {
                                var localName = NameComponents.ParseName(v.Key);
                                variables.Add(new Variable(localName.Name, v.Value.ValueToString, (v.Value.Value is ReferenceRuntimeValue rv) ? (int)rv.RefId : 0));
                            }
                        }
                        frame = frame.ParentFrame;
                    }
                    return new VariablesResponse(variables);
                }
            }
            catch (Exception e)
            {
                SendDebug(e.ToString());
                return new VariablesResponse([]);
            }
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
            List<Breakpoint> breakpoints = [];
            var filePath = arguments.Source.Path;
            var newBreakpoints = new HashSet<(int line, int column)>();

            // Get current breakpoints for this file
            if (!currentBreakpoints.TryGetValue(filePath, out var oldBreakpoints))
            {
                oldBreakpoints = new HashSet<(int line, int column)>();
            }

            foreach (var sourceBreakpoint in arguments.Breakpoints)
            {
                var line = sourceBreakpoint.Line;
                var column = sourceBreakpoint.Column ?? 0;
                var location = new SourceLocation(filePath, "", line, line, column, column);

                if (vm != null)
                {
                    // Remove existing breakpoint at this location if any
                    var success = vm.RemoveBreakPoint(location);
                    SendDebug($"Breakpoint unset {(success ? "successfully" : "unsuccessfully")} at {location.FileName}@{location.RowStart}:{location.ColStart}\n");

                    // Add new breakpoint
                    if (vm.InsertBreakPoint(location))
                    {
                        breakpoints.Add(new Breakpoint { Verified = true, Line = line, Column = column });
                        SendDebug($"Breakpoint set successfully at {location.FileName}@{location.RowStart}:{location.ColStart}\n");
                        newBreakpoints.Add((line, column));
                    }
                    else
                    {
                        breakpoints.Add(new Breakpoint { Verified = false, Line = line, Column = column });
                        SendDebug($"Breakpoint set unsuccessfully at {location.FileName}@{location.RowStart}:{location.ColStart}\n");
                    }
                }
                else
                {
                    // When VM is not initialized, assume all breakpoints are valid
                    breakpoints.Add(new Breakpoint { Verified = true, Line = line, Column = column });
                    newBreakpoints.Add((line, column));
                }
            }

            // Remove breakpoints that are no longer in the list
            if (vm != null)
            {
                foreach (var oldBreakpoint in oldBreakpoints)
                {
                    if (!newBreakpoints.Contains(oldBreakpoint))
                    {
                        var location = new SourceLocation(filePath, "", oldBreakpoint.line, oldBreakpoint.line, oldBreakpoint.column, oldBreakpoint.column);
                        var success = vm.RemoveBreakPoint(location);
                        SendDebug($"Breakpoint unset {(success ? "successfully" : "unsuccessfully")} at {location.FileName}@{location.RowStart}:{location.ColStart}\n");
                    }
                }
            }

            // Update current breakpoints
            currentBreakpoints[filePath] = newBreakpoints;

            return new SetBreakpointsResponse(breakpoints);
        }

        protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
        {
            return new SetExceptionBreakpointsResponse();
        }

        protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments)
        {
            return new EvaluateResponse();
        }
    }
}