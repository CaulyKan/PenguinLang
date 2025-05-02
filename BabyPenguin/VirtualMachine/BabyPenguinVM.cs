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

        public StringBuilder Output { get; } = new StringBuilder();

        public string CollectOutput() => Output.ToString();

        public void Run()
        {
            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol;
            if (mainFunc == null)
                throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], 0);
            frame.Run();
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public Dictionary<string, IRuntimeVar> GlobalVariables { get; } = [];

        public Dictionary<string, Action<RuntimeFrame, IRuntimeVar?, List<IRuntimeVar>>> ExternFunctions { get; } = [];

        public void RegisterExternFunction(string name, Action<IRuntimeVar?, List<IRuntimeVar>> func)
        {
            ExternFunctions.Add(name, (frame, result, args) => func(result, args));
        }

        public void RegisterExternFunction(string name, Action<RuntimeFrame, IRuntimeVar?, List<IRuntimeVar>> func)
        {
            ExternFunctions.Add(name, func);
        }

        public bool EnableDebugPrint { get; set; } = false;

        public TextWriter DebugWriter { get; set; } = Console.Out;
    }
}