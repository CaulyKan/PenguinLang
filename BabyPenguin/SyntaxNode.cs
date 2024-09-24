using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using Mono.Cecil.Cil;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;

namespace BabyPenguin
{
    namespace Syntax
    {
        public abstract class SyntaxNode : IPrettyPrint
        {
            public SyntaxNode(SyntaxWalker walker, ParserRuleContext context)
            {
                Scope = walker.CurrentScope ?? this as Namespace ?? throw new NotImplementedException();
                Context = context;
                Reporter = walker.Reporter;

                var fileNameIdentifier = System.IO.Path.GetFileNameWithoutExtension(walker.FileName) + "_" + (uint)System.IO.Path.GetFullPath(walker.FileName).GetHashCode();
                SourceLocation = new SourceLocation(walker.FileName, fileNameIdentifier, context.Start.Line, context.Start.Column, context.Stop.Line, context.Stop.Column);
            }

            public ISyntaxScope Scope { get; }
            public ParserRuleContext Context { get; }

            public virtual SourceLocation SourceLocation { get; }

            public ErrorReporter Reporter { get; }
            public virtual string GetText() =>
                Context.Start.InputStream.GetText(new Interval(Context.Start.StartIndex, Context.Stop.StopIndex));
            public override string ToString() => this.GetType().Name + ": " + shorten(GetText().Trim().Replace("\n", " ").Replace("\r", ""));
            public virtual IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                yield return new string(' ', indentLevel * 2) + (note ?? " ") + ToString();
            }
            protected string shorten(string str) => str.Length > 30 ? str.Substring(0, 27) + "..." : str;
        }

        public class Namespace : SyntaxNode, ISyntaxScope
        {
            public Namespace(SyntaxWalker walker, NamespaceDefinitionContext context) : base(walker, context)
            {
                Name = context.identifier().GetText();
                IsAnonymous = false;

                walker.PushScope(SyntaxScopeType.Namespace, this);

                foreach (var namespaceDeclarationContext in context.namespaceDeclaration())
                {
                    processNamespace(walker, namespaceDeclarationContext);
                }

                walker.PopScope();
            }

            public Namespace(SyntaxWalker walker, CompilationUnitContext context) : base(walker, context)
            {
                Name = "_global@" + SourceLocation.FileNameIdentifier;
                IsAnonymous = true;

                walker.PushScope(SyntaxScopeType.Namespace, this);

                foreach (var namespaceDeclarationContext in context.namespaceDeclaration())
                {
                    processNamespace(walker, namespaceDeclarationContext);
                }
            }

            private void processNamespace(SyntaxWalker walker, NamespaceDeclarationContext namespaceDeclarationContext)
            {
                InitialRoutines.AddRange(
                     namespaceDeclarationContext.children.OfType<InitialRoutineContext>()
                        .Select(x => new InitialRoutine(walker, x)));

                Declarations.AddRange(
                     namespaceDeclarationContext.children.OfType<DeclarationContext>()
                        .Select(x => new Declaration(walker, x)));

                SubNamespaces.AddRange(
                     namespaceDeclarationContext.children.OfType<NamespaceDefinitionContext>()
                        .Select(x => new Namespace(walker, x)));

                Functions.AddRange(
                     namespaceDeclarationContext.children.OfType<FunctionDefinitionContext>()
                        .Select(x => new FunctionDefinition(walker, x)));

                Classes.AddRange(
                     namespaceDeclarationContext.children.OfType<ClassDefinitionContext>()
                        .Select(x => new ClassDefinition(walker, x)));
            }

            public List<InitialRoutine> InitialRoutines { get; } = new();
            public List<Declaration> Declarations { get; } = new();
            public List<Namespace> SubNamespaces { get; } = new();
            public List<FunctionDefinition> Functions { get; } = new();
            public List<ClassDefinition> Classes { get; } = new();
            public string Name { get; }

            public SyntaxScopeType ScopeType => SyntaxScopeType.Namespace;

            public List<SyntaxSymbol> Symbols { get; } = [];

            public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];
            public ISyntaxScope? ParentScope { get; set; }

            public bool IsAnonymous { get; private set; }

            public uint ScopeDepth { get; set; } = 0;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel).Concat(
                    Declarations.SelectMany(x => x.PrettyPrint(indentLevel + 1))
                ).Concat(
                    InitialRoutines.SelectMany(x => x.PrettyPrint(indentLevel + 1))
                ).Concat(
                    Functions.SelectMany(x => x.PrettyPrint(indentLevel + 1))
                ).Concat(
                    Classes.SelectMany(x => x.PrettyPrint(indentLevel + 1))
                );
            }
        }

        public class InitialRoutine : SyntaxNode, ISyntaxScope
        {
            public InitialRoutine(SyntaxWalker walker, InitialRoutineContext context) : base(walker, context)
            {
                walker.PushScope(SyntaxScopeType.InitialRoutine, this);

                CodeBlock = new CodeBlock(walker, context.codeBlock());

                walker.PopScope();
            }

            static UInt64 counter = 0;

            public CodeBlock CodeBlock { get; }

            public string Name { get; } = $"initial_{counter++}";

            public SyntaxScopeType ScopeType => SyntaxScopeType.InitialRoutine;

            public List<SyntaxSymbol> Symbols { get; } = [];

            public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];
            public ISyntaxScope? ParentScope { get; set; }
            public bool IsAnonymous => false;

            public uint ScopeDepth { get; set; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel).Concat(
                    CodeBlock.PrettyPrint(indentLevel + 1)
                );
            }
        }

        public class CodeBlock : SyntaxNode, ISyntaxScope
        {
            public CodeBlock(SyntaxWalker walker, CodeBlockContext context) : base(walker, context)
            {
                walker.PushScope(SyntaxScopeType.CodeBlock, this);
                BlockItems = context.children.OfType<CodeBlockItemContext>()
                    .Select(x => new CodeBlockItem(walker, x)).ToList();
                walker.PopScope();
            }

            static UInt64 counter = 0;

            public List<CodeBlockItem> BlockItems { get; } = new();

            public string Name { get; } = $"codeblock_{counter++}";

            public SyntaxScopeType ScopeType => SyntaxScopeType.CodeBlock;

            public List<SyntaxSymbol> Symbols { get; } = [];

            public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

            public ISyntaxScope? ParentScope { get; set; }

            public bool IsAnonymous => true;

            public uint ScopeDepth { get; set; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
                => BlockItems.SelectMany(x => x.PrettyPrint(indentLevel));
        }

        public class CodeBlockItem : SyntaxNode
        {
            public CodeBlockItem(SyntaxWalker walker, CodeBlockItemContext context) : base(walker, context)
            {
                if (context.statement() is not null)
                {
                    Statement = new Statement(walker, context.statement());
                }
                else if (context.declaration() is not null)
                {
                    Declaration = new Declaration(walker, context.declaration());
                }
            }

            public Statement? Statement { get; }

            public Declaration? Declaration { get; }

            public bool IsDeclaration => Declaration is not null;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return IsDeclaration ? Declaration!.PrettyPrint(indentLevel) : Statement!.PrettyPrint(indentLevel);
            }
        }

        public class Statement : SyntaxNode
        {
            public enum Type
            {
                SubBlock,
                ExpressionStatement,
                SelectionStatement,
                IterationStatement,
                JumpStatement,
                AssignmentStatement
            }

            public Statement(SyntaxWalker walker, StatementContext context) : base(walker, context)
            {
                if (context.expressionStatement() is not null)
                {
                    StatementType = Type.ExpressionStatement;
                    ExpressionStatement = new ExpressionStatement(walker, context.expressionStatement());
                }
                else if (context.selectionStatement() is not null)
                {
                    StatementType = Type.SelectionStatement;
                    SelectionStatement = new SelectionStatement(walker, context.selectionStatement());
                }
                else if (context.iterationStatement() is not null)
                {
                    StatementType = Type.IterationStatement;
                    IterationStatement = new IterationStatement(walker, context.iterationStatement());
                }
                else if (context.jumpStatement() is not null)
                {
                    StatementType = Type.JumpStatement;
                    JumpStatement = new JumpStatement(walker, context.jumpStatement());
                }
                else if (context.assignmentStatement() is not null)
                {
                    StatementType = Type.AssignmentStatement;
                    AssignmentStatement = new AssignmentStatement(walker, context.assignmentStatement());
                }
                else
                {
                    StatementType = Type.SubBlock;
                    CodeBlock = new CodeBlock(walker, context.codeBlock());
                }

            }

            public Type StatementType { get; }

            public CodeBlock? CodeBlock { get; }

            public ExpressionStatement? ExpressionStatement { get; }

            public SelectionStatement? SelectionStatement { get; }

            public IterationStatement? IterationStatement { get; }

            public JumpStatement? JumpStatement { get; }

            public AssignmentStatement? AssignmentStatement { get; }


            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return StatementType switch
                {
                    Type.SubBlock => CodeBlock!.PrettyPrint(indentLevel + 1),
                    Type.ExpressionStatement => ExpressionStatement!.PrettyPrint(indentLevel),
                    Type.SelectionStatement => SelectionStatement!.PrettyPrint(indentLevel),
                    Type.IterationStatement => IterationStatement!.PrettyPrint(indentLevel),
                    Type.JumpStatement => JumpStatement!.PrettyPrint(indentLevel),
                    Type.AssignmentStatement => AssignmentStatement!.PrettyPrint(indentLevel),
                    _ => throw new System.NotImplementedException($"Invalid statement type: {StatementType}"),
                };
            }
        }

        public class ExpressionStatement : SyntaxNode
        {
            public ExpressionStatement(SyntaxWalker walker, ExpressionStatementContext context) : base(walker, context)
            {
                Expression = new Expression(walker, context.expression());

            }

            public Expression Expression { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return Expression.PrettyPrint(indentLevel);
            }
        }

        public class SelectionStatement : SyntaxNode
        {
            public SelectionStatement(SyntaxWalker walker, SelectionStatementContext context) : base(walker, context)
            {
            }
        }

        public class IterationStatement : SyntaxNode
        {
            public IterationStatement(SyntaxWalker walker, IterationStatementContext context) : base(walker, context)
            {
            }
        }

        public class JumpStatement : SyntaxNode
        {
            public JumpStatement(SyntaxWalker walker, JumpStatementContext context) : base(walker, context)
            {
            }
        }

        public class AssignmentStatement : SyntaxNode
        {
            public AssignmentStatement(SyntaxWalker walker, AssignmentStatementContext context) : base(walker, context)
            {
                LeftHandSide = new Identifier(walker, context.identifier(), false);
                RightHandSide = new Expression(walker, context.expression());
                AssignmentOperator = context.assignmentOperator().GetText() switch
                {
                    "=" => AssignmentOperatorEnum.Assign,
                    "+=" => AssignmentOperatorEnum.AddAssign,
                    "-=" => AssignmentOperatorEnum.SubtractAssign,
                    "*=" => AssignmentOperatorEnum.MultiplyAssign,
                    "/=" => AssignmentOperatorEnum.DivideAssign,
                    "%=" => AssignmentOperatorEnum.ModuloAssign,
                    "&=" => AssignmentOperatorEnum.BitwiseAndAssign,
                    "|=" => AssignmentOperatorEnum.BitwiseOrAssign,
                    "^=" => AssignmentOperatorEnum.BitwiseXorAssign,
                    "<<=" => AssignmentOperatorEnum.LeftShiftAssign,
                    ">>=" => AssignmentOperatorEnum.RightShiftAssign,
                    _ => throw new System.NotImplementedException($"Invalid assignment operator: {context.assignmentOperator().GetText()}"),
                };
            }

            public Identifier LeftHandSide { get; }
            public Expression RightHandSide { get; }
            public AssignmentOperatorEnum AssignmentOperator { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel).Concat(
                    LeftHandSide.PrettyPrint(indentLevel + 1)
                ).Concat(
                    RightHandSide.PrettyPrint(indentLevel + 1)
                );
            }
        }

        public class Identifier : SyntaxNode
        {
            public Identifier(SyntaxWalker walker, IdentifierContext context, bool isType) : base(walker, context)
            {
                this.IsType = isType;
                this.LiteralName = context.GetText();
            }

            public Identifier(SyntaxWalker walker, TypeSpecifierContext context, bool isType) : base(walker, context)
            {
                this.IsType = isType;
                this.LiteralName = context.GetText();
            }

            public Identifier(SyntaxWalker walker, string liternalName, ParserRuleContext context, bool isType) : base(walker, context)
            {
                this.IsType = isType;
                this.LiteralName = liternalName;
            }

            public string? ResolvedFullname { get; }

            public Declaration? ResolvedDeclaration { get; }

            public bool IsType { get; }

            public string LiteralName { get; }

            public string Name => LiteralName;
        }

        public class TypeSpecifier : SyntaxNode
        {
            public TypeSpecifier(SyntaxWalker walker, TypeSpecifierContext context) : base(walker, context)
            {
                var identifier = new Identifier(walker, context.GetText(), context, true);
                Name = identifier.Name;
            }

            public TypeSpecifier(SyntaxWalker walker, string liternalName, ParserRuleContext context) : base(walker, context)
            {
                Name = liternalName;
            }

            public string Name { get; }
        }

        public class Declaration : SyntaxNode
        {
            public Declaration(SyntaxWalker walker, PenguinLangParser.DeclarationContext context) : base(walker, context)
            {
                Identifier = new Identifier(walker, context.identifier(), false);
                TypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
                IsReadonly = context.declarationKeyword().GetText() == "val";
                walker.DefineSymbol(Name, TypeSpecifier.Name, this);

                if (context.expression() != null)
                    InitializeExpression = new Expression(walker, context.expression());
            }

            public Identifier Identifier { get; }
            public TypeSpecifier TypeSpecifier { get; }
            public Expression? InitializeExpression { get; }
            public bool IsReadonly;
            public string Name => Identifier.Name;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel).Concat(
                    Identifier.PrettyPrint(indentLevel + 1)
                ).Concat(
                    TypeSpecifier.PrettyPrint(indentLevel + 1, "(type)")
                ).Concat(
                    InitializeExpression?.PrettyPrint(indentLevel + 1, "(initializer)") ?? []
                );
            }
        }

        public class Expression(SyntaxWalker walker, PenguinLangParser.ExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public LogicalOrExpression SubExpression { get; }
                = new LogicalOrExpression(walker, context.logicalOrExpression());

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpression.PrettyPrint(indentLevel);
            }

            public bool IsSimple => SubExpression.IsSimple;
        }

        public class LogicalOrExpression(SyntaxWalker walker, LogicalOrExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<LogicalAndExpression> SubExpressions { get; }
                = context.children.OfType<LogicalAndExpressionContext>()
                   .Select(x => new LogicalAndExpression(walker, x))
                   .ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class LogicalAndExpression(SyntaxWalker walker, LogicalAndExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<InclusiveOrExpression> SubExpressions { get; }
                = context.children.OfType<InclusiveOrExpressionContext>()
                   .Select(x => new InclusiveOrExpression(walker, x))
                   .ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class InclusiveOrExpression(SyntaxWalker walker, InclusiveOrExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<ExclusiveOrExpression> SubExpressions { get; }
                = context.children.OfType<ExclusiveOrExpressionContext>()
                   .Select(x => new ExclusiveOrExpression(walker, x))
                   .ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class ExclusiveOrExpression(SyntaxWalker walker, ExclusiveOrExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<AndExpression> SubExpressions { get; }
                = context.children.OfType<AndExpressionContext>()
                   .Select(x => new AndExpression(walker, x))
                   .ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class AndExpression(SyntaxWalker walker, AndExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<EqualityExpression> SubExpressions { get; }
                = context.children.OfType<EqualityExpressionContext>()
                   .Select(x => new EqualityExpression(walker, x))
                   .ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class EqualityExpression(SyntaxWalker walker, EqualityExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<RelationalExpression> SubExpressions { get; }
                = context.children.OfType<RelationalExpressionContext>()
                   .Select(x => new RelationalExpression(walker, x))
                   .ToList();

            public List<BinaryOperatorEnum> Operators { get; } = context.equalityOperator().Select(x => x.GetText() switch
            {
                "==" => BinaryOperatorEnum.Equal,
                "!=" => BinaryOperatorEnum.NotEqual,
                _ => throw new System.NotImplementedException("Invalid equality operator"),
            }).ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class RelationalExpression(SyntaxWalker walker, PenguinLangParser.RelationalExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<ShiftExpression> SubExpressions { get; } = context.children.OfType<ShiftExpressionContext>()
                   .Select(x => new ShiftExpression(walker, x))
                   .ToList();

            public List<BinaryOperatorEnum> Operators { get; } = context.relationalOperator().Select(x => x.GetText() switch
                    {
                        "<" => BinaryOperatorEnum.LessThan,
                        ">" => BinaryOperatorEnum.GreaterThan,
                        "<=" => BinaryOperatorEnum.LessThanOrEqual,
                        ">=" => BinaryOperatorEnum.GreaterThanOrEqual,
                        _ => throw new System.NotImplementedException("Invalid relational operator")
                    }).ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class ShiftExpression(SyntaxWalker walker, ShiftExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<AdditiveExpression> SubExpressions { get; }
                = context.children.OfType<AdditiveExpressionContext>()
                   .Select(x => new AdditiveExpression(walker, x))
                   .ToList();

            public List<BinaryOperatorEnum> Operators { get; } = context.shiftOperator().Select(x => x.GetText() switch
                    {
                        "<<" => BinaryOperatorEnum.LeftShift,
                        ">>" => BinaryOperatorEnum.RightShift,
                        _ => throw new System.NotImplementedException("Invalid shift operator")
                    }).ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class AdditiveExpression(SyntaxWalker walker, AdditiveExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<MultiplicativeExpression> SubExpressions { get; }
                = context.children.OfType<MultiplicativeExpressionContext>()
                   .Select(x => new MultiplicativeExpression(walker, x))
                   .ToList();

            public List<BinaryOperatorEnum> Operators { get; } = context.additiveOperator().Select(x => x.GetText() switch
                    {
                        "+" => BinaryOperatorEnum.Add,
                        "-" => BinaryOperatorEnum.Subtract,
                        _ => throw new System.NotImplementedException("Invalid additive operator")
                    }).ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class MultiplicativeExpression(SyntaxWalker walker, MultiplicativeExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
        {
            public List<CastExpression> SubExpressions { get; }
                = context.children.OfType<CastExpressionContext>()
                   .Select(x => new CastExpression(walker, x))
                   .ToList();

            public List<BinaryOperatorEnum> Operators { get; } = context.multiplicativeOperator().Select(x => x.GetText() switch
                    {
                        "*" => BinaryOperatorEnum.Multiply,
                        "/" => BinaryOperatorEnum.Divide,
                        "%" => BinaryOperatorEnum.Modulo,
                        _ => throw new System.NotImplementedException("Invalid multiplicative operator")
                    }).ToList();

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
            }

            public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
        }

        public class CastExpression : SyntaxNode, ISyntaxExpression
        {
            public CastExpression(SyntaxWalker walker, CastExpressionContext context) : base(walker, context)
            {
                if (context.children.OfType<CastExpressionContext>().Any())
                {
                    SubCastExpression = new CastExpression(walker, context.castExpression());
                    CastTypeIdentifier = new Identifier(walker, context.identifier(), true);
                }
                else
                {
                    SubUnaryExpression = new UnaryExpression(walker, context.unaryExpression());
                }
            }

            public Identifier? CastTypeIdentifier { get; }

            public CastExpression? SubCastExpression { get; }

            public UnaryExpression? SubUnaryExpression { get; }

            public bool IsTypeCast => SubCastExpression is not null;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                if (IsTypeCast)
                    return base.PrettyPrint(indentLevel).Concat(SubUnaryExpression!.PrettyPrint(indentLevel + 1));
                else
                    return SubUnaryExpression!.PrettyPrint(indentLevel);
            }

            public bool IsSimple => IsTypeCast ? false : SubUnaryExpression!.IsSimple;
        }

        public class UnaryExpression : SyntaxNode, ISyntaxExpression
        {
            public UnaryExpression(SyntaxWalker walker, UnaryExpressionContext context) : base(walker, context)
            {
                if (context.children.OfType<UnaryOperatorContext>().Any())
                {
                    UnaryOperator = context.unaryOperator().GetText() switch
                    {
                        "&" => (UnaryOperatorEnum?)UnaryOperatorEnum.Ref,
                        "*" => (UnaryOperatorEnum?)UnaryOperatorEnum.Deref,
                        "+" => (UnaryOperatorEnum?)UnaryOperatorEnum.Plus,
                        "-" => (UnaryOperatorEnum?)UnaryOperatorEnum.Minus,
                        "!" => (UnaryOperatorEnum?)UnaryOperatorEnum.LogicalNot,
                        "~" => (UnaryOperatorEnum?)UnaryOperatorEnum.BitwiseNot,
                        _ => throw new System.NotImplementedException("Invalid unary operator"),
                    };
                }
                SubExpression = new PostfixExpression(walker, context.postfixExpression());
            }

            public PostfixExpression SubExpression { get; }

            public UnaryOperatorEnum? UnaryOperator { get; }

            public bool HasUnaryOperator => UnaryOperator is not null;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                if (HasUnaryOperator)
                    return base.PrettyPrint(indentLevel).Concat(SubExpression!.PrettyPrint(indentLevel + 1));
                else
                    return SubExpression!.PrettyPrint(indentLevel);
            }

            public bool IsSimple => HasUnaryOperator ? false : SubExpression!.IsSimple;
        }

        public class PostfixExpression : SyntaxNode, ISyntaxExpression
        {
            public enum Type
            {
                PrimaryExpression,
                Slicing,
                FunctionCall,
                MemberAccess,
            }

            public PostfixExpression(SyntaxWalker walker, PostfixExpressionContext context) : base(walker, context)
            {
                if (context.children.OfType<PrimaryExpressionContext>().Any())
                {
                    SubPrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
                    PostfixExpressionType = Type.PrimaryExpression;
                }
                else if (context.children.OfType<SlicingExpressionContext>().Any())
                {
                    SubSlicingExpression = new SlicingExpression(walker, context.slicingExpression());
                    PostfixExpressionType = Type.Slicing;
                }
                else if (context.children.OfType<FunctionCallExpressionContext>().Any())
                {
                    SubFunctionCallExpression = new FunctionCallExpression(walker, context.functionCallExpression());
                    PostfixExpressionType = Type.FunctionCall;
                }
                else if (context.children.OfType<MemberAccessExpressionContext>().Any())
                {
                    SubMemberAccessExpression = new MemberAccessExpression(walker, context.memberAccessExpression());
                    PostfixExpressionType = Type.MemberAccess;
                }
                else
                {
                    throw new System.NotImplementedException("Invalid postfix expression");
                }
            }

            public Type PostfixExpressionType { get; }

            public PrimaryExpression? SubPrimaryExpression { get; }

            public SlicingExpression? SubSlicingExpression { get; }

            public FunctionCallExpression? SubFunctionCallExpression { get; }

            public MemberAccessExpression? SubMemberAccessExpression { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return PostfixExpressionType switch
                {
                    Type.PrimaryExpression => SubPrimaryExpression!.PrettyPrint(indentLevel),
                    Type.Slicing => SubSlicingExpression!.PrettyPrint(indentLevel),
                    Type.FunctionCall => SubFunctionCallExpression!.PrettyPrint(indentLevel),
                    Type.MemberAccess => SubMemberAccessExpression!.PrettyPrint(indentLevel),
                    _ => throw new System.NotImplementedException("Invalid postfix expression type"),
                };
            }

            public bool IsSimple => PostfixExpressionType == Type.PrimaryExpression ? SubPrimaryExpression!.IsSimple : false;
        }

        public class PrimaryExpression : SyntaxNode, ISyntaxExpression
        {
            public enum Type
            {
                Identifier,
                Constant,
                StringLiteral,
                BoolLiteral,
                ParenthesizedExpression,
            }

            public PrimaryExpression(SyntaxWalker walker, PrimaryExpressionContext context) : base(walker, context)
            {
                if (context.children.OfType<IdentifierContext>().Any())
                {
                    Identifier = new Identifier(walker, context.identifier(), false);
                    PrimaryExpressionType = Type.Identifier;
                }
                else if (context.Constant() != null)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.Constant;
                }
                else if (context.StringLiteral().Length > 0)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.StringLiteral;
                }
                else if (context.boolLiteral() != null)
                {
                    Literal = context.GetText();
                    PrimaryExpressionType = Type.BoolLiteral;
                }
                else if (context.children.OfType<ExpressionContext>().Any())
                {
                    ParenthesizedExpression = new Expression(walker, context.expression());
                    PrimaryExpressionType = Type.ParenthesizedExpression;
                }
                else
                {
                    throw new System.NotImplementedException("Invalid primary expression");
                }
            }

            public Type PrimaryExpressionType { get; }

            public Identifier? Identifier { get; }

            public string? Literal { get; }

            public Expression? ParenthesizedExpression { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return PrimaryExpressionType switch
                {
                    Type.Identifier => Identifier!.PrettyPrint(indentLevel, note),
                    Type.Constant => [IPrettyPrint.PrintText(indentLevel, "ConstantLiteral: " + Literal)],
                    Type.StringLiteral => [IPrettyPrint.PrintText(indentLevel, "StringLiteral: " + Literal)],
                    Type.BoolLiteral => [IPrettyPrint.PrintText(indentLevel, "BoolLiteral: " + Literal)],
                    Type.ParenthesizedExpression => ParenthesizedExpression!.PrettyPrint(indentLevel, note),
                    _ => throw new NotImplementedException(),
                };
            }

            public bool IsSimple => PrimaryExpressionType switch
            {
                Type.Identifier => true,
                Type.Constant => true,
                Type.StringLiteral => true,
                Type.BoolLiteral => true,
                Type.ParenthesizedExpression => ParenthesizedExpression!.IsSimple,
                _ => throw new System.NotImplementedException("Invalid primary expression type"),
            };
        }

        public class SlicingExpression : SyntaxNode, ISyntaxExpression
        {
            public SlicingExpression(SyntaxWalker walker, SlicingExpressionContext context) : base(walker, context)
            {
                PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
                IndexExpression = new Expression(walker, context.expression());
            }

            public PrimaryExpression PrimaryExpression { get; }

            public Expression IndexExpression { get; }

            public bool IsSimple => false;

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel)
                    .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Slicable)"))
                    .Concat(IndexExpression.PrettyPrint(indentLevel + 1, "(Index)"));
            }
        }

        public class FunctionCallExpression : SyntaxNode, ISyntaxExpression
        {
            public FunctionCallExpression(SyntaxWalker walker, FunctionCallExpressionContext context) : base(walker, context)
            {
                PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
                ArgumentsExpression = context.children.OfType<ExpressionContext>()
                   .Select(x => new Expression(walker, x))
                   .ToList();
            }

            public PrimaryExpression PrimaryExpression { get; }

            public List<Expression> ArgumentsExpression { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel)
                    .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Function)"))
                    .Concat(ArgumentsExpression.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")));
            }

            public bool IsSimple => false;
        }

        public class MemberAccessExpression : SyntaxNode, ISyntaxExpression
        {
            public MemberAccessExpression(SyntaxWalker walker, MemberAccessExpressionContext context) : base(walker, context)
            {
                PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
                MemberIdentifier = new Identifier(walker, context.identifier(), false);
            }

            public PrimaryExpression PrimaryExpression { get; }

            public Identifier MemberIdentifier { get; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel)
                    .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Object)"))
                    .Concat(MemberIdentifier.PrettyPrint(indentLevel + 1, "(Member)"));
            }

            public bool IsSimple => false;
        }

        public class FunctionDefinition : SyntaxNode, ISyntaxScope
        {
            public FunctionDefinition(SyntaxWalker walker, FunctionDefinitionContext context) : base(walker, context)
            {
                walker.PushScope(SyntaxScopeType.Function, this);

                this.FunctionIdentifier = new Identifier(walker, context.identifier(), false);

                walker.DefineSymbol(this.Name, "fun", this);

                if (context.parameterList().children == null)
                {
                    this.Parameters = [];
                }
                else
                {
                    this.Parameters = context.parameterList().children.OfType<DeclarationContext>()
                        .Select(x => new Declaration(walker, x)).ToList();
                }

                if (context.typeSpecifier() == null)
                {
                    this.ReturnType = new TypeSpecifier(walker, "void", context);
                }
                else
                {
                    this.ReturnType = new TypeSpecifier(walker, context.typeSpecifier());
                }
                this.CodeBlock = new CodeBlock(walker, context.codeBlock());

                walker.PopScope();
            }
            public Identifier FunctionIdentifier { get; }
            public List<Declaration> Parameters { get; }
            public TypeSpecifier ReturnType { get; }
            public CodeBlock CodeBlock { get; }
            public string Name => FunctionIdentifier.Name;
            public SyntaxScopeType ScopeType => SyntaxScopeType.Function;
            public List<SyntaxSymbol> Symbols { get; } = [];
            public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];
            public ISyntaxScope? ParentScope { get; set; }
            public bool IsAnonymous => false;
            public uint ScopeDepth { get; set; }

            public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            {
                return base.PrettyPrint(indentLevel)
                    .Concat(FunctionIdentifier.PrettyPrint(indentLevel + 1, "(FunctionName)"))
                    .Concat(Parameters.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")))
                    .Concat(ReturnType.PrettyPrint(indentLevel + 1, "(ReturnType)"))
                    .Concat(CodeBlock.PrettyPrint(indentLevel + 1));
            }
        }

        public class ClassDefinition : SyntaxNode, ISyntaxScope
        {
            public ClassDefinition(SyntaxWalker walker, ClassDefinitionContext context) : base(walker, context)
            {
                walker.PushScope(SyntaxScopeType.Class, this);

                this.ClassIdentifier = new Identifier(walker, context.identifier(), false);

                walker.PopScope();
            }

            public Identifier ClassIdentifier { get; }

            public string Name => ClassIdentifier.Name;

            public SyntaxScopeType ScopeType => SyntaxScopeType.Class;
            public List<SyntaxSymbol> Symbols { get; } = [];
            public bool IsAnonymous => false;
            public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];
            public ISyntaxScope? ParentScope { get; set; }
            public uint ScopeDepth { get; set; }
        }
    }
}