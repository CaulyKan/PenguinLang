namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal))
            {
                Global.GlobalVariables.Add(symbol.FullName, new RuntimeVar(model, symbol.TypeInfo, symbol));
            }

            Global.ExternFunctions.Add("__builtin.print", (result, args) => { Output.Append(args[0].Value); Console.Write(args[0].Value); });
            Global.ExternFunctions.Add("__builtin.println", (result, args) => { Output.AppendLine(args[0].Value as string); Console.WriteLine(args[0].Value); });
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new RuntimeGlobal();

        public StringBuilder Output { get; } = new StringBuilder();

        public string CollectOutput() => Output.ToString();

        public void Run()
        {
            foreach (var ns in Model.Namespaces)
            {
                var frame = new RuntimeFrame(ns, Global, []);
                frame.Run();
            }

            foreach (var inital in Model.Namespaces.SelectMany(ns => ns.InitialRoutines))
            {
                var frame = new RuntimeFrame(inital, Global, []);
                frame.Run();
            }
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public Dictionary<string, RuntimeVar> GlobalVariables { get; } = [];

        public Dictionary<string, Action<RuntimeVar?, List<RuntimeVar>>> ExternFunctions { get; } = [];
        public bool EnableDebugPrint { get; set; } = false;
        public TextWriter DebugWriter { get; set; } = Console.Out;
    }
}