namespace BabyPenguin.VirtualMachine
{
    public class IRParameter
    {
        public string Name { get; }
        public string IrType { get; }
        public int Index { get; }
        public int SourceLine { get; set; }
        public int SourceCol { get; set; }

        public IRParameter(string name, string irType, int index)
        {
            Name = name;
            IrType = irType;
            Index = index;
        }
    }

    public class IRFunction
    {
        public string Name { get; }
        public string DisplayName { get; set; }
        public string ReturnType { get; }
        public bool IsExtern { get; set; }
        public List<IRParameter> Parameters { get; } = [];
        public List<IRInstruction> Instructions { get; } = [];
        public string SourceFile { get; set; } = "";
        public int SourceLine { get; set; }
        public int SourceCol { get; set; }

        private int _nextTemp;
        private int _nextLabel;

        public IRFunction(string name, string returnType)
        {
            Name = name;
            DisplayName = name;
            ReturnType = returnType;
        }

        public IRValue AllocNamedReg(string name, string irType, int line = 0, int col = 0)
        {
            return new IRNamedRegister(name, irType, line, col);
        }

        public IRValue AllocTemp(string irType)
        {
            return new IRTempRegister(_nextTemp++, irType);
        }

        public IRLabelValue AllocLabel(string prefix)
        {
            return new IRLabelValue($"{prefix}{_nextLabel++}");
        }

        public void AddInst(IRInstruction inst)
        {
            Instructions.Add(inst);
        }

        public IRValue AllocParam(string name, string irType, int line = 0, int col = 0)
        {
            var idx = Parameters.Count;
            var param = new IRParameter(name, irType, idx)
            {
                SourceLine = line,
                SourceCol = col
            };
            Parameters.Add(param);
            return AllocNamedReg(name, irType, line, col);
        }

        public bool HasTerminator()
        {
            if (Instructions.Count == 0) return false;
            return Instructions[^1] is IRRetInst or IRRetVoidInst;
        }

        public bool EndsWithControlFlow()
        {
            if (Instructions.Count == 0) return false;
            if (IsControlFlow(Instructions[^1])) return true;
            if (Instructions.Count >= 2 && IsControlFlow(Instructions[^2])) return true;
            return false;
        }

        private static bool IsControlFlow(IRInstruction inst)
        {
            return inst is IRBrInst or IRBrCondInst or IRRetInst or IRRetVoidInst;
        }
    }
}
