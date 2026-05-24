namespace BabyPenguin.VirtualMachine
{
    public class IRGlobalVariable
    {
        public string Name { get; }
        public string IrType { get; }
        public IRValue? InitialValue { get; }

        public IRGlobalVariable(string name, string irType, IRValue? initialValue = null)
        {
            Name = name;
            IrType = irType;
            InitialValue = initialValue;
        }
    }

    public class IRModule
    {
        public Dictionary<string, IRFunction> Functions { get; } = [];
        public List<string> EntryFunctions { get; } = [];
        public Dictionary<string, IRGlobalVariable> GlobalVariables { get; } = [];

        public void AddFunction(IRFunction function)
        {
            Functions[function.Name] = function;
        }

        public IRFunction? FindFunction(string name)
        {
            return Functions.GetValueOrDefault(name);
        }

        public void AddEntryFunction(string name)
        {
            EntryFunctions.Add(name);
        }

        public void AddGlobalVariable(IRGlobalVariable global)
        {
            GlobalVariables[global.Name] = global;
        }

        public string Display()
        {
            var sb = new System.Text.StringBuilder();

            // Display global variables
            foreach (var gv in GlobalVariables.Values)
            {
                sb.AppendLine($"GLOBAL @{gv.Name}: {gv.IrType}");
                if (gv.InitialValue != null)
                    sb.AppendLine($"  INIT {gv.InitialValue.Display()}");
            }

            // Display functions
            foreach (var func in Functions.Values)
            {
                if (func.IsExtern) continue;
                sb.AppendLine();
                sb.AppendLine($"FUN @{func.Name}({string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.IrType}"))}): {func.ReturnType}");
                foreach (var inst in func.Instructions)
                {
                    sb.AppendLine($"  {inst.Display()}");
                }
            }

            return sb.ToString();
        }
    }
}
