using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;
using BabyPenguin;

namespace BabyPenguin.Semantic
{
    public record SemanticInstruction
    {
        public List<string> Labels { get; } = [];

        public SemanticInstruction WithLabel(string label)
        {
            Labels.Add(label);
            return this;
        }

        public sealed override string ToString() => $"{StringCommand} {StringOP1} {StringOP2} -> {StringResult} {StringLabels}";
        public virtual string StringCommand => "";
        public virtual string StringOP1 => "";
        public virtual string StringOP2 => "";
        public virtual string StringResult => "";
        public virtual string StringLabels => Labels.Count == 0 ? "" : "[" + string.Join(", ", Labels) + "]";
    }

    public record NopInstuction() : SemanticInstruction
    {
        public override string StringCommand => "NOP";
    }

    public record GotoInstruction(string TargetLabel, ISymbol? Condition = null, bool JumpOnCondition = true) : SemanticInstruction
    {
        override public string StringCommand => "GOTO";
        override public string StringOP1 => Condition == null ? "" : (JumpOnCondition ? "" : "!" + Condition.ToString());
        override public string StringResult => TargetLabel;
    }

    public record ReturnInstruction(ISymbol? RetValue = null) : SemanticInstruction
    {
        override public string StringCommand => "RETN";
        override public string StringOP1 => (RetValue == null) ? "" : RetValue.ToString()!;
    }

    public record BinaryOperationInstruction(BinaryOperatorEnum Operator, ISymbol LeftSymbol, ISymbol RightSymbol, ISymbol Target) : SemanticInstruction
    {
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => LeftSymbol.ToString() ?? "";
        override public string StringOP2 => RightSymbol.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record UnaryOperationInstruction(UnaryOperatorEnum Operator, ISymbol Operand, ISymbol Target) : SemanticInstruction
    {
        override public string StringCommand => Operator.ToString().ToUpper();
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record NewInstanceInstruction(ISymbol Target) : SemanticInstruction
    {
        override public string StringCommand => "NEW";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record AssignmentInstruction(ISymbol RightHandSymbol, ISymbol LeftHandSymbol) : SemanticInstruction
    {
        override public string StringCommand => "ASSIGN";
        override public string StringOP1 => RightHandSymbol.ToString() ?? "";
        override public string StringResult => LeftHandSymbol.ToString() ?? "";
    }

    public record ReadMemberInstruction(ISymbol Member, ISymbol MemberOwnerSymbol, ISymbol Target) : SemanticInstruction
    {
        override public string StringCommand => "RDMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => MemberOwnerSymbol.ToString() ?? "";
        override public string StringResult => Target.ToString() ?? "";
    }

    public record WriteMemberInstruction(ISymbol Member, ISymbol Value, ISymbol MemberOwnerSymbol) : SemanticInstruction
    {
        override public string StringCommand => "WRMBR";
        override public string StringOP1 => Member.ToString() ?? "";
        override public string StringOP2 => Value.ToString() ?? "";
        override public string StringResult => MemberOwnerSymbol.ToString() ?? "";
    }

    public record AssignLiteralToSymbolInstruction(ISymbol Target, TypeInfo Type, string LiteralValue) : SemanticInstruction
    {
        override public string StringCommand => "LITERAL";
        override public string StringOP1 => LiteralValue;
        override public string StringResult => Target.ToString() ?? "";
    }

    public record FunctionCallInstruction(ISymbol FunctionSymbol, List<ISymbol> Arguments, ISymbol? Target) : SemanticInstruction
    {
        override public string StringCommand => "CALL";
        override public string StringOP1 => FunctionSymbol.ToString() ?? "";
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

    public record CastInstruction(ISymbol Operand, TypeInfo TypeInfo, ISymbol Target) : SemanticInstruction
    {
        override public string StringCommand => "CAST";
        override public string StringOP1 => Operand.ToString() ?? "";
        override public string StringOP2 => TypeInfo.ToString();
        override public string StringResult => Target.ToString() ?? "";
    }
}