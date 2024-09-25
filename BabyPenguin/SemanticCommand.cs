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

    public interface ISemanticCommand { }


    public record BinaryOperationCommand(BinaryOperatorEnum Operator, ISymbol LeftSymbol, ISymbol RightSymbol, ISymbol Target) : ISemanticCommand
    {
        public override string ToString() => $"{Operator.ToString().ToUpper()} {LeftSymbol} {RightSymbol} -> {Target}";
    }

    public record UnaryOperationCommand(UnaryOperatorEnum Operator, ISymbol Operand, ISymbol Target) : ISemanticCommand
    {
        public override string ToString() => $"{Operator.ToString().ToUpper()} {Operand} -> {Target}";
    }

    public record AssignmentCommand(ISymbol RightHandSymbol, ISymbol LeftHandSymbol) : ISemanticCommand
    {
        public override string ToString() => $"ASSIGN {RightHandSymbol} -> {LeftHandSymbol}";
    }

    public record AssignLiteralToSymbolCommand(ISymbol Target, TypeInfo Type, string LiteralValue) : ISemanticCommand
    {
        public override string ToString() => $"LITERAL {LiteralValue} -> {Target}";
    }

    public record FunctionCallCommand(ISymbol FunctionSymbol, List<ISymbol> Arguments, ISymbol Target) : ISemanticCommand
    {
        public override string ToString() => $"CALL {FunctionSymbol} (${string.Join(", ", Arguments)}) -> {Target}";
    }

    public record SlicingCommand(ISymbol Slicable, ISymbol Index, ISymbol Target) : ISemanticCommand
    {
        public override string ToString() => $"SLICE {Slicable} [${Index}] -> {Target}";
    }

    public record CastCommand(ISymbol Operand, TypeInfo Type, ISymbol Target) : ISemanticCommand
    {
        public override string ToString() => $"CAST {Operand} (${Type}) -> {Target}";
    }
}