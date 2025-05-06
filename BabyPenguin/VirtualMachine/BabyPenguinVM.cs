namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal))
            {
                Global.GlobalVariables.Add(symbol.FullName, IRuntimeVar.FromSymbol(model, symbol));
            }

            ExternFunctions.Build(this);
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new RuntimeGlobal();

        public RuntimeFrame? StartFrame { get; private set; }

        public Stack<RuntimeFrame> StackFrames => Global.StackFrames;

        public string CollectOutput() => Global.Output.ToString();

        public void Initialize()
        {
            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol;
            if (mainFunc == null)
                throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], 0);
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
        public Stack<RuntimeFrame> StackFrames { get; } = new Stack<RuntimeFrame>();

        public bool StepMode { get; set; } = false;

        public Dictionary<string, IRuntimeVar> GlobalVariables { get; } = [];

        public Dictionary<string, Func<RuntimeFrame, IRuntimeVar?, List<IRuntimeVar>, IEnumerable<bool>>> ExternFunctions { get; } = [];

        public void RegisterExternFunction(string name, Action<IRuntimeVar?, List<IRuntimeVar>> func)
        {
            ExternFunctions.Add(name, (frame, result, args) =>
            {
                func(result, args);
                return [true];
            });
        }

        public void RegisterExternFunction(string name, Func<RuntimeFrame, IRuntimeVar?, List<IRuntimeVar>, IEnumerable<bool>> func)
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