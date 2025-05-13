namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal))
            {
                Global.GlobalVariables.Add(symbol.FullName, IRuntimeSymbol.FromSymbol(model, symbol));
            }

            ExternFunctions.Build(this);
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new RuntimeGlobal();

        public RuntimeFrame? StartFrame { get; private set; }

        public string CollectOutput() => Global.Output.ToString();

        public void Initialize()
        {
            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol;
            if (mainFunc == null)
                throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], null);
            StartFrame = frame;
        }

        public void Run()
        {
            if (StartFrame == null)
                Initialize();
            foreach (var _ in StartFrame!.Run()) { }
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public enum StepModeEnum { StepIn, StepOver, StepOut, Run }

        public StepModeEnum StepMode { get; set; } = StepModeEnum.Run;

        public Dictionary<string, IRuntimeSymbol> GlobalVariables { get; } = [];

        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> ExternFunctions { get; } = [];

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

        public StringBuilder Output { get; } = new StringBuilder();

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