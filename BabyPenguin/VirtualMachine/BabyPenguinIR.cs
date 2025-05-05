namespace BabyPenguin.VirtualMachine
{
    public record BabyPenguinIR
    {
        public List<string> Labels { get; } = [];

        public BabyPenguinIR WithLabel(string label)
        {
            Labels.Add(label);
            return this;
        }

        public sealed override string ToString() => $"{StringCommand} {StringOP1} {StringOP2} -> {StringResult} {StringLabels}";
        public virtual string ToDebugString(string? op1, string? op2, string? result) => $"{ConsoleColor.RED}{StringCommand}{ConsoleColor.NORMAL} {op1} {op2} {(string.IsNullOrEmpty(result) ? "" : "-> " + result)}";
        public virtual string StringCommand => "";
        public virtual string StringOP1 => "";
        public virtual string StringOP2 => "";
        public virtual string StringResult => "";
        public virtual string StringLabels => Labels.Count == 0 ? "" : "[" + string.Join(", ", Labels) + "]";
    }

    public record NopInstuction() : BabyPenguinIR
    {
        public override string StringCommand => "NOP";
    }

    public record GotoInstruction(string TargetLabel, ISymbol? Condition = null, bool JumpOnCondition = true) : BabyPenguinIR
    {
        override public string StringCommand => "GOTO";
        override public string StringOP1 => Condition == null ? "" : (JumpOnCondition ? "" : "!" + Condition.ToString());
        override public string StringResult => TargetLabel;
    }

    public enum ReturnStatus
    {
        Blocked = 0, // the routine is blocked by some dependency and dont have a result
        YieldNotFinished, // the routine generated a result but not finished
        YieldFinished, // the routine generated a result and finished
        Finished, // the routine finished without generating a result
    }

    public record ReturnInstruction(ISymbol? RetValue, ReturnStatus ReturnStatus) : BabyPenguinIR
    {
        override public string StringCommand => "RETN";
        override public string StringOP1 => (RetValue == null) ? "" : RetValue.ToString()!;
        public override string StringOP2 => ReturnStatus.ToString();
    }

    public enum SignalCode
    {
        Breakpoint = 0,
    }

    public record SignalInstruction(ISymbol CodeSymbol) : BabyPenguinIR
    {
        override public string StringCommand => "SIGN";

        override public string StringOP1 => CodeSymbol.ToString() ?? "";
    }

    public record BinaryOperationInstruction(BinaryOperatorEnum Operator, ISymbol LeftSymbol, ISymbol RightSymbol, ISymbol Target) : BabyPenguinIR
    {
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => LeftSymbol.ToString() ?? "";
        override public string StringOP2 => RightSymbol.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record UnaryOperationInstruction(UnaryOperatorEnum Operator, ISymbol Operand, ISymbol Target) : BabyPenguinIR
    {
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record NewInstanceInstruction(ISymbol Target) : BabyPenguinIR
    {
        override public string StringCommand => "NEW";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record AssignmentInstruction(ISymbol RightHandSymbol, ISymbol LeftHandSymbol) : BabyPenguinIR
    {
        override public string StringCommand => "ASSIGN";
        override public string StringOP1 => RightHandSymbol.ToString() ?? "";
        override public string StringResult => LeftHandSymbol.ToString() ?? "";
    }

    public record ReadMemberInstruction(ISymbol Member, ISymbol MemberOwnerSymbol, ISymbol Target) : BabyPenguinIR
    {
        override public string StringCommand => "RDMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => MemberOwnerSymbol.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record WriteMemberInstruction(ISymbol Member, ISymbol Value, ISymbol MemberOwnerSymbol) : BabyPenguinIR
    {
        override public string StringCommand => "WRMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => Value.ToString() ?? "";
        override public string StringResult => MemberOwnerSymbol?.ToString() ?? "";
    }

    public record AssignLiteralToSymbolInstruction(ISymbol Target, IType Type, string LiteralValue) : BabyPenguinIR
    {
        override public string StringCommand => "LITERAL";
        override public string StringOP1 => LiteralValue;
        override public string StringResult => Target.ToString() ?? "";
    }

    public record FunctionCallInstruction(ISymbol FunctionSymbol, List<ISymbol> Arguments, ISymbol? Target) : BabyPenguinIR
    {
        override public string StringCommand => "CALL";
        override public string StringOP1 => FunctionSymbol.FullName ?? "";
        override public string StringOP2 => string.Join(", ", Arguments);
        override public string StringResult => Target?.ToString() ?? "";
    }

    // public record SlicingInstruction(ISymbol Slicable, ISymbol Index, ISymbol Target) : SemanticInstruction
    // {
    //     override public string StringCommand => "SLICE";
    //     override public string StringOP1 => Slicable.ToString() ?? "";
    //     override public string StringOP2 => $"{Index}";
    //     override public string StringResult => Target.ToString() ?? "";
    // }

    public record CastInstruction(ISymbol Operand, IType TypeInfo, ISymbol Target) : BabyPenguinIR
    {
        override public string StringCommand => "CAST";
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringOP2 => TypeInfo.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record WriteEnumInstruction(ISymbol Value, ISymbol TargetEnum) : BabyPenguinIR
    {
        override public string StringCommand => "WRENUM";
        override public string StringOP1 => Value.ToString() ?? "";
        override public string StringResult => TargetEnum.ToString() ?? "";
    }

    public record ReadEnumInstruction(ISymbol Enum, ISymbol TargetValue) : BabyPenguinIR
    {
        override public string StringCommand => "RDENUM";
        override public string StringOP1 => Enum.ToString() ?? "";
        override public string StringResult => TargetValue.ToString() ?? "";
    }
}