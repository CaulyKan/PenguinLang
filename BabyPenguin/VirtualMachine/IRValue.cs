namespace BabyPenguin.VirtualMachine
{
    public enum IRValueKind
    {
        NamedRegister,
        TempRegister,
        Constant,
        Label,
        GlobalRef
    }

    public abstract class IRValue
    {
        public abstract IRValueKind Kind { get; }
        public abstract string Display();
        public abstract string GetIrType();
    }

    public class IRNamedRegister : IRValue
    {
        public string Name { get; }
        public string IrType { get; }
        public int SourceLine { get; }
        public int SourceCol { get; }

        public IRNamedRegister(string name, string irType, int sourceLine = 0, int sourceCol = 0)
        {
            Name = name;
            IrType = irType;
            SourceLine = sourceLine;
            SourceCol = sourceCol;
        }

        public override IRValueKind Kind => IRValueKind.NamedRegister;
        public override string Display() => $"%{Name}";
        public override string GetIrType() => IrType;
    }

    public class IRTempRegister : IRValue
    {
        public int Index { get; }
        public string IrType { get; }

        public IRTempRegister(int index, string irType)
        {
            Index = index;
            IrType = irType;
        }

        public override IRValueKind Kind => IRValueKind.TempRegister;
        public override string Display() => $"%t{Index}";
        public override string GetIrType() => IrType;
    }

    public class IRConstant : IRValue
    {
        public string Value { get; }
        public string IrType { get; }

        public IRConstant(string value, string irType)
        {
            Value = value;
            IrType = irType;
        }

        public override IRValueKind Kind => IRValueKind.Constant;
        public override string Display() => Value;
        public override string GetIrType() => IrType;
    }

    public class IRLabelValue : IRValue
    {
        public string Name { get; }

        public IRLabelValue(string name)
        {
            Name = name;
        }

        public override IRValueKind Kind => IRValueKind.Label;
        public override string Display() => $"{Name}:";
        public override string GetIrType() => "label";
    }

    public class IRGlobalRef : IRValue
    {
        public string Name { get; }
        public string IrType { get; }

        public IRGlobalRef(string name, string irType)
        {
            Name = name;
            IrType = irType;
        }

        public override IRValueKind Kind => IRValueKind.GlobalRef;
        public override string Display() => $"@{Name}";
        public override string GetIrType() => IrType;
    }
}
