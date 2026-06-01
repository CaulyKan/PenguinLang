using System.Collections.Concurrent;

namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal && !s.IsClassMember))
            {
                Global.GlobalVariables.Add(symbol.FullName(), IRuntimeSymbol.FromSymbol(model, symbol, Global));
            }

            ExternFunctions.Build(this);
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new();

        public RuntimeFrame? StartFrame { get; private set; }

        public string CollectOutput() => Global.Output.ToString();

        public void Initialize()
        {
            // Generate register-based IR from the semantic model
            var generator = new IRGenerator(Model);
            Global.IRModule = generator.Generate();

            // Build CodeContainer index for O(1) lookup (eliminates 38% profile hotspot)
            BuildCodeContainerIndex();

            // Build sanitized extern function index for O(1) lookup
            BuildSanitizedExternFunctionIndex();

            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol
                ?? throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], null);
            StartFrame = frame;
        }

        private static string SanitizeName(string name) => name.Replace(".", "_");

        private void BuildCodeContainerIndex()
        {
            foreach (var node in Model.FindAll(n => n is SemanticInterface.ICodeContainer))
            {
                var cc = (SemanticInterface.ICodeContainer)node;
                var sanitized = SanitizeName(cc.FullName());
                Global.CodeContainerIndex[sanitized] = cc;
            }
        }

        private void BuildSanitizedExternFunctionIndex()
        {
            foreach (var kvp in Global.ExternFunctions)
            {
                var sanitized = SanitizeName(kvp.Key);
                Global.SanitizedExternFunctionIndex[sanitized] = kvp.Value;
            }
        }

        public int Run()
        {
            if (StartFrame == null)
                Initialize();
            try
            {
                foreach (var result in StartFrame!.Run())
                {
                    if (result.IsLeft)
                    {
                        if (result.Left!.Reason == RuntimeBreakReason.Exited)
                        {
                            return Global.ExitCode;
                        }
                    }
                }
            }
            catch (ProgramExitException)
            {
                return Global.ExitCode;
            }
            return 0;
        }

        public bool InsertBreakPoint(SourceLocation location)
        {
            // Store breakpoint in global for the new RuntimeFrame to check
            Global.Breakpoints.Add(location);
            return true;
        }

        public bool RemoveBreakPoint(SourceLocation location)
        {
            return Global.Breakpoints.Remove(location);
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public enum StepModeEnum { StepIn, StepOver, StepOut, Run }

        public ConcurrentDictionary<ulong, ReferenceRuntimeValue> AllObjects { get; } = [];

        private ulong _refIdCounter = 0;
        public ulong NextRefId() => Interlocked.Increment(ref _refIdCounter);

        public void ClearAllObjects() => AllObjects.Clear();

        public int ExitCode { get; set; } = 0;

        public bool HasExited { get; set; } = false;

        public string[] CommandLineArgs { get; set; } = Array.Empty<string>();

        public StepModeEnum StepMode { get; set; } = StepModeEnum.Run;

        public Dictionary<string, IRuntimeSymbol> GlobalVariables { get; } = [];

        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> ExternFunctions { get; } = [];

        /// <summary>
        /// Pre-built index: sanitized name → ICodeContainer for O(1) lookup.
        /// Built once during VM initialization instead of traversing the entire semantic tree on every function call.
        /// </summary>
        public Dictionary<string, SemanticInterface.ICodeContainer> CodeContainerIndex { get; } = [];

        /// <summary>
        /// Pre-built index: sanitized name → extern function for O(1) lookup.
        /// Eliminates the fallback linear scan in FindExternFunction.
        /// </summary>
        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> SanitizedExternFunctionIndex { get; } = [];

        public IRModule? IRModule { get; set; }

        public HashSet<SourceLocation> Breakpoints { get; } = [];

        public void RegisterExternFunction(string name, Action<IRuntimeSymbol?, List<IRuntimeValue>> func)
        {
            ExternFunctions.Add(name, (frame, result, args) =>
            {
                func(result, args);
                return [];
            });
        }

        public void RegisterExternFunction(string name, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>> func)
        {
            ExternFunctions.Add(name, func);
        }

        public bool EnableDebugPrint { get; set; } = false;

        public StringBuilder Output { get; } = new();

        public Action<string> PrintFunc { get; set; } = (s) => Console.Write(s);

        public Action<string> DebugFunc { get; set; } = (s) => Console.Write(s);

        public void Print(string s, bool newline = false)
        {
            if (!newline)
            {
                Output.Append(s);
                PrintFunc(s);
            }
            else
            {
                Output.AppendLine(s);
                PrintFunc(s + Environment.NewLine);
            }
        }
    }
}
