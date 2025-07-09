namespace BabyPenguin.VirtualMachine
{
    public abstract class BabyPenguinIR
    {
        public List<string> Labels { get; } = [];

        public abstract SourceLocation SourceLocation { get; set; }

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

    public class NopInstuction(SourceLocation sourceLocation) : BabyPenguinIR
    {
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        public override string StringCommand => "NOP";
    }

    public class GotoInstruction(SourceLocation sourceLocation, string targetLabel, ISymbol? condition = null, bool jumpOnCondition = true) : BabyPenguinIR
    {
        public string TargetLabel { get; } = targetLabel;
        public ISymbol? Condition { get; } = condition;
        public bool JumpOnCondition { get; } = jumpOnCondition;

        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "GOTO";
        override public string StringOP1 => Condition == null ? "" : (JumpOnCondition ? "" : "!" + Condition.ToString());
        override public string StringResult => TargetLabel;
    }

    public enum ReturnStatus
    {
        Blocked = 0, // the routine is blocked by some dependency and dont have a result
        YieldNotFinished = 2, // the routine generated a result but not finished
        Finished = 3, // the routine finished without generating a result
        YieldFinished = 4, // the routine generated a result and finished
    }

    public class ReturnInstruction(SourceLocation sourceLocation, ISymbol? retValue, ReturnStatus returnStatus) : BabyPenguinIR
    {
        public ISymbol? RetValue { get; } = retValue;
        public ReturnStatus ReturnStatus { get; } = returnStatus;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "RETN";
        override public string StringOP1 => (RetValue == null) ? "" : RetValue.ToString()!;
        public override string StringOP2 => ReturnStatus.ToString();
    }

    public enum SignalCode
    {
        Breakpoint = 0,
    }

    public class SignalInstruction(SourceLocation sourceLocation, ISymbol? codeSymbol, int? code) : BabyPenguinIR
    {
        public ISymbol? CodeSymbol { get; } = codeSymbol;
        public int? Code { get; } = code;

        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "SIGNAL";
        override public string StringOP1 => CodeSymbol?.ToString() ?? "";
        override public string StringOP2 => Code?.ToString() ?? "";
    }

    public class BinaryOperationInstruction(SourceLocation sourceLocation, BinaryOperatorEnum _operator, ISymbol leftSymbol, ISymbol rightSymbol, ISymbol target) : BabyPenguinIR
    {
        public BinaryOperatorEnum Operator { get; } = _operator;
        public ISymbol LeftSymbol { get; } = leftSymbol;
        public ISymbol RightSymbol { get; } = rightSymbol;
        public ISymbol Target { get; } = target;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => LeftSymbol.ToString() ?? "";
        override public string StringOP2 => RightSymbol.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public class UnaryOperationInstruction(SourceLocation sourceLocation, UnaryOperatorEnum _operator, ISymbol operand, ISymbol target) : BabyPenguinIR
    {
        public UnaryOperatorEnum Operator { get; } = _operator;
        public ISymbol Operand { get; } = operand;
        public ISymbol Target { get; } = target;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public class NewInstanceInstruction(SourceLocation sourceLocation, ISymbol target) : BabyPenguinIR
    {
        public ISymbol Target { get; } = target;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "NEW";
        override public string StringResult => Target.ToString() ?? "";
    }

    public class AssignmentInstruction(SourceLocation sourceLocation, ISymbol rightHandSymbol, ISymbol leftHandSymbol) : BabyPenguinIR
    {
        public ISymbol RightHandSymbol { get; } = rightHandSymbol;
        public ISymbol LeftHandSymbol { get; } = leftHandSymbol;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "ASSIGN";
        override public string StringOP1 => RightHandSymbol.ToString() ?? "";
        override public string StringResult => LeftHandSymbol.ToString() ?? "";
    }

    public class ReadMemberInstruction(SourceLocation sourceLocation, ISymbol member, ISymbol memberOwnerSymbol, ISymbol target, bool isFatPointer) : BabyPenguinIR
    {

        public ISymbol Member { get; } = member;
        public ISymbol MemberOwnerSymbol { get; } = memberOwnerSymbol;
        public ISymbol Target { get; } = target;
        public bool IsFatPointer { get; } = isFatPointer;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "RDMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => MemberOwnerSymbol.ToString() ?? "";
        override public string StringResult => (Target.ToString() ?? "") + (IsFatPointer ? " (fat)" : "");
    }

    public class WriteMemberInstruction(SourceLocation sourceLocation, ISymbol member, ISymbol _value, ISymbol memberOwnerSymbol) : BabyPenguinIR
    {
        public ISymbol Member { get; } = member;
        public ISymbol Value { get; } = _value;
        public ISymbol MemberOwnerSymbol { get; } = memberOwnerSymbol;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "WRMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => Value.ToString() ?? "";
        override public string StringResult => MemberOwnerSymbol?.ToString() ?? "";
    }

    public class AssignLiteralToSymbolInstruction(SourceLocation sourceLocation, ISymbol target, IType type, string literalValue) : BabyPenguinIR
    {
        public ISymbol Target { get; } = target;
        public IType Type { get; } = type;
        public string LiteralValue { get; } = literalValue;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "LITERAL";
        override public string StringOP1 => LiteralValue;
        override public string StringResult => Target.ToString() ?? "";
    }

    public class FunctionCallInstruction(SourceLocation sourceLocation, ISymbol functionSymbol, List<ISymbol> arguments, ISymbol? target) : BabyPenguinIR
    {
        public ISymbol FunctionSymbol { get; } = functionSymbol;
        public List<ISymbol> Arguments { get; } = arguments;
        public ISymbol? Target { get; } = target;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "CALL";
        override public string StringOP1 => FunctionSymbol.FullName() ?? "";
        override public string StringOP2 => string.Join(", ", Arguments);
        override public string StringResult => Target?.ToString() ?? "";
    }

    // public class SlicingInstruction(ISymbol Slicable, ISymbol Index, ISymbol Target) : SemanticInstruction
    // {
    //     override public string StringCommand => "SLICE";
    //     override public string StringOP1 => Slicable.ToString() ?? "";
    //     override public string StringOP2 => $"{Index}";
    //     override public string StringResult => Target.ToString() ?? "";
    // }

    public class CastInstruction(SourceLocation sourceLocation, ISymbol operand, IType typeInfo, ISymbol target) : BabyPenguinIR
    {
        public ISymbol Operand { get; } = operand;
        public IType TypeInfo { get; } = typeInfo;
        public ISymbol Target { get; } = target;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "CAST";
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringOP2 => TypeInfo.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public class WriteEnumInstruction(SourceLocation sourceLocation, ISymbol value, ISymbol targetEnum) : BabyPenguinIR
    {
        public ISymbol Value { get; } = value;
        public ISymbol TargetEnum { get; } = targetEnum;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "WRENUM";
        override public string StringOP1 => Value.ToString() ?? "";
        override public string StringResult => TargetEnum.ToString() ?? "";
    }

    public class ReadEnumInstruction(SourceLocation sourceLocation, ISymbol _enum, ISymbol targetValue) : BabyPenguinIR
    {
        public ISymbol Enum { get; } = _enum;
        public ISymbol TargetValue { get; } = targetValue;
        public override SourceLocation SourceLocation { get; set; } = sourceLocation;
        override public string StringCommand => "RDENUM";
        override public string StringOP1 => Enum.ToString() ?? "";
        override public string StringResult => TargetValue.ToString() ?? "";
    }
}