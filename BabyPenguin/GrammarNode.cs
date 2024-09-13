using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Mono.Cecil.Cil;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;

namespace BabyPenguin
{

    public abstract class GrammarNode : IPrettyPrint
    {
        public GrammarNode(ICompilerInfo compilerInfo, ParserRuleContext context)
        {
            Context = context;
            Reporter = compilerInfo.Reporter;
            FileName = compilerInfo.FileName;
        }

        public GrammarNode(ParserRuleContext context, ErrorReporter reporter, String file)
        {
            Context = context;
            Reporter = reporter;
            FileName = file;
        }

        public T GetContext<T>() where T : ParserRuleContext
        {
            if (Context == null)
                throw new System.InvalidCastException($"Current Context is null, Expected: {typeof(T).Name}");
            else if (Context is T res)
                return res;
            else
                throw new System.InvalidCastException($"Current Context {this.Context.GetType().Name}, Expected: {typeof(T).Name}");
        }

        public ParserRuleContext Context { get; }
        public int RowStart => Context.Start.Line;
        public int RowEnd => Context.Stop.Line;
        public int ColStart => Context.Start.Column;
        public int ColEnd => Context.Stop.Column;


        public ErrorReporter Reporter { get; }
        public String FileName { get; }
        public String FileNameIdentifier => System.IO.Path.GetFileName(FileName) + "_" + System.IO.Path.GetFullPath(FileName).GetHashCode();
        public string GetText() =>
            Context.Start.InputStream.GetText(new Interval(Context.Start.StartIndex, Context.Stop.StopIndex));
        public override string ToString() => this.GetType().Name + ": " + GetText().Trim().Replace("\n", " ").Replace("\r", "");
        public virtual IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            yield return new string(' ', indentLevel * 2) + (note ?? " ") + ToString();
        }
    }


    public class Namespace : GrammarNode, IScope
    {
        public Namespace(ICompilerInfo compilerInfo, NamespaceDefinitionContext context) : base(compilerInfo, context)
        {
            Name = context.identifier().GetText();

            compilerInfo.PushScope(ScopeType.Namespace, this);

            foreach (var namespaceDeclarationContext in context.namespaceDeclaration())
            {
                processNamespace(compilerInfo, namespaceDeclarationContext);
            }

            compilerInfo.PopScope();
        }

        public Namespace(ICompilerInfo compilerInfo, CompilationUnitContext context) : base(compilerInfo, context)
        {
            Name = "_global@" + FileNameIdentifier;

            compilerInfo.PushScope(ScopeType.Namespace, this);

            foreach (var namespaceDeclarationContext in context.namespaceDeclaration())
            {
                processNamespace(compilerInfo, namespaceDeclarationContext);
            }
        }

        private void processNamespace(ICompilerInfo compilerInfo, NamespaceDeclarationContext namespaceDeclarationContext)
        {
            InitialRoutines.AddRange(
                 namespaceDeclarationContext.children.OfType<InitialRoutineContext>()
                    .Select(x => new InitialRoutine(compilerInfo, x)));

            Declarations.AddRange(
                 namespaceDeclarationContext.children.OfType<DeclarationContext>()
                    .Select(x => new Declaration(compilerInfo, x)));

            SubNamespaces.AddRange(
                 namespaceDeclarationContext.children.OfType<NamespaceDefinitionContext>()
                    .Select(x => new Namespace(compilerInfo, x)));

            Functions.AddRange(
                 namespaceDeclarationContext.children.OfType<FunctionDefinitionContext>()
                    .Select(x => new FunctionDefinition(compilerInfo, x)));
        }


        public Namespace? ParentNamespace { get; }
        public List<InitialRoutine> InitialRoutines { get; } = new();
        public List<Declaration> Declarations { get; } = new();
        public List<Namespace> SubNamespaces { get; } = new();
        public List<FunctionDefinition> Functions { get; } = new();
        public string Name { get; }

        public string Id => Name;

        public ScopeType ScopeType => ScopeType.Namespace;

        public List<Symbol> Symbols { get; } = [];

        public Dictionary<string, IScope> SubScopes { get; } = [];
        public IScope? ParentScope { get; set; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                Declarations.SelectMany(x => x.PrettyPrint(indentLevel + 1))
            ).Concat(
                InitialRoutines.SelectMany(x => x.PrettyPrint(indentLevel + 1))
            ).Concat(
                Functions.SelectMany(x => x.PrettyPrint(indentLevel + 1))
            );
        }
    }

    public class InitialRoutine : GrammarNode, IScope
    {
        public InitialRoutine(ICompilerInfo compilerInfo, InitialRoutineContext context) : base(compilerInfo, context)
        {
            compilerInfo.PushScope(ScopeType.InitialRoutine, this);

            CodeBlock = new CodeBlock(compilerInfo, context.codeBlock());

            compilerInfo.PopScope();
        }

        public CodeBlock CodeBlock { get; }

        public string Name => $"initial@{FileNameIdentifier}:{RowStart}";

        public string Id => Name;

        public ScopeType ScopeType => ScopeType.InitialRoutine;

        public List<Symbol> Symbols { get; } = [];

        public Dictionary<string, IScope> SubScopes { get; } = [];
        public IScope? ParentScope { get; set; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                CodeBlock.PrettyPrint(indentLevel + 1)
            );
        }
    }

    public class CodeBlock : GrammarNode, IScope
    {
        public CodeBlock(ICompilerInfo compilerInfo, CodeBlockContext context) : base(compilerInfo, context)
        {
            compilerInfo.PushScope(ScopeType.CodeBlock, this);
            BlockItems = context.children.OfType<CodeBlockItemContext>()
                .Select(x => new CodeBlockItem(compilerInfo, x)).ToList();
            compilerInfo.PopScope();
        }

        public List<CodeBlockItem> BlockItems { get; } = new();

        public string Id => $"codeblock@{FileNameIdentifier}:{RowStart}";

        public ScopeType ScopeType => ScopeType.CodeBlock;

        public List<Symbol> Symbols { get; } = [];

        public Dictionary<string, IScope> SubScopes { get; } = [];

        public IScope? ParentScope { get; set; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
            => BlockItems.SelectMany(x => x.PrettyPrint(indentLevel));
    }

    public class CodeBlockItem : GrammarNode
    {
        public CodeBlockItem(ICompilerInfo compilerInfo, CodeBlockItemContext context) : base(compilerInfo, context)
        {
            if (context.statement() is not null)
            {
                Statement = new Statement(compilerInfo, context.statement());
            }
            else if (context.declaration() is not null)
            {
                Declaration = new Declaration(compilerInfo, context.declaration());
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

    public class Statement : GrammarNode
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

        public Statement(ICompilerInfo compilerInfo, StatementContext context) : base(compilerInfo, context)
        {
            if (context.expressionStatement() is not null)
            {
                StatementType = Type.ExpressionStatement;
                ExpressionStatement = new ExpressionStatement(compilerInfo, context.expressionStatement());
            }
            else if (context.selectionStatement() is not null)
            {
                StatementType = Type.SelectionStatement;
                SelectionStatement = new SelectionStatement(compilerInfo, context.selectionStatement());
            }
            else if (context.iterationStatement() is not null)
            {
                StatementType = Type.IterationStatement;
                IterationStatement = new IterationStatement(compilerInfo, context.iterationStatement());
            }
            else if (context.jumpStatement() is not null)
            {
                StatementType = Type.JumpStatement;
                JumpStatement = new JumpStatement(compilerInfo, context.jumpStatement());
            }
            else if (context.assignmentStatement() is not null)
            {
                StatementType = Type.AssignmentStatement;
                AssignmentStatement = new AssignmentStatement(compilerInfo, context.assignmentStatement());
            }
            else
            {
                StatementType = Type.SubBlock;
                CodeBlock = new CodeBlock(compilerInfo, context.codeBlock());
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
                _ => throw new System.InvalidOperationException($"Invalid statement type: {StatementType}"),
            };
        }
    }

    public class ExpressionStatement : GrammarNode
    {
        public ExpressionStatement(ICompilerInfo compilerInfo, ExpressionStatementContext context) : base(compilerInfo, context)
        {
            Expression = new Expression(compilerInfo, context.expression());

        }

        public Expression Expression { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return Expression.PrettyPrint(indentLevel);
        }
    }

    public class SelectionStatement : GrammarNode
    {
        public SelectionStatement(ICompilerInfo compilerInfo, SelectionStatementContext context) : base(compilerInfo, context)
        {
        }
    }

    public class IterationStatement : GrammarNode
    {
        public IterationStatement(ICompilerInfo compilerInfo, IterationStatementContext context) : base(compilerInfo, context)
        {
        }
    }

    public class JumpStatement : GrammarNode
    {
        public JumpStatement(ICompilerInfo compilerInfo, JumpStatementContext context) : base(compilerInfo, context)
        {
        }
    }

    public class AssignmentStatement : GrammarNode
    {
        public AssignmentStatement(ICompilerInfo compilerInfo, AssignmentStatementContext context) : base(compilerInfo, context)
        {
            LeftHandSide = new Identifier(compilerInfo, context.identifier(), false);
            RightHandSide = new Expression(compilerInfo, context.expression());
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
                _ => throw new System.InvalidOperationException($"Invalid assignment operator: {context.assignmentOperator().GetText()}"),
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

    public class Identifier : GrammarNode
    {
        public Identifier(ICompilerInfo compilerInfo, IdentifierContext context, bool isType) : base(compilerInfo, context)
        {
            this.IsType = isType;
            this.LiteralName = context.GetText();
        }

        public Identifier(ICompilerInfo compilerInfo, TypeSpecifierContext context, bool isType) : base(compilerInfo, context)
        {
            this.IsType = isType;
            this.LiteralName = context.GetText();
        }

        public string? ResolvedFullname { get; }

        public Declaration? ResolvedDeclaration { get; }

        public bool IsType { get; }

        public string LiteralName { get; }

        public string Name => LiteralName;
    }

    public class TypeSpecifier : GrammarNode
    {
        public TypeSpecifier(ICompilerInfo compilerInfo, TypeSpecifierContext context) : base(compilerInfo, context)
        {
            if (context.GetText() == "void")
            {
                Type = TypeSpecifierEnum.Void;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "u8")
            {
                Type = TypeSpecifierEnum.U8;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "i8")
            {
                Type = TypeSpecifierEnum.I8;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "u16")
            {
                Type = TypeSpecifierEnum.U16;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "i16")
            {
                Type = TypeSpecifierEnum.I16;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "u32")
            {
                Type = TypeSpecifierEnum.U32;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "i32")
            {
                Type = TypeSpecifierEnum.I32;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "u64")
            {
                Type = TypeSpecifierEnum.U64;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "i64")
            {
                Type = TypeSpecifierEnum.I64;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "float")
            {
                Type = TypeSpecifierEnum.Float;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "double")
            {
                Type = TypeSpecifierEnum.Double;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "bool")
            {
                Type = TypeSpecifierEnum.Bool;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else if (context.GetText() == "string")
            {
                Type = TypeSpecifierEnum.String;
                Identifier = new Identifier(compilerInfo, context, true);
            }
            else
            {
                Type = TypeSpecifierEnum.Other;
                Identifier = new Identifier(compilerInfo, context.identifier(), true);
            }
        }

        public TypeSpecifierEnum Type { get; }

        public Identifier Identifier { get; }

        public string Name => Identifier.Name;
    }

    public class Declaration : GrammarNode
    {
        public Declaration(ICompilerInfo compilerInfo, PenguinLangParser.DeclarationContext context) : base(compilerInfo, context)
        {
            Identifier = new Identifier(compilerInfo, context.identifier(), false);
            TypeSpecifier = new TypeSpecifier(compilerInfo, context.typeSpecifier());

            if (context.expression() != null)
                InitializeExpression = new Expression(compilerInfo, context.expression());
        }

        public Identifier Identifier { get; }
        public TypeSpecifier TypeSpecifier { get; }
        public Expression? InitializeExpression { get; }

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

    public class Expression(ICompilerInfo compilerInfo, PenguinLangParser.ExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public LogicalOrExpression SubExpression { get; }
            = new LogicalOrExpression(compilerInfo, context.logicalOrExpression());

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpression.PrettyPrint(indentLevel);
        }

        public bool IsSimple => SubExpression.IsSimple;
    }

    public class LogicalOrExpression(ICompilerInfo compilerInfo, LogicalOrExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<LogicalAndExpression> SubExpressions { get; }
            = context.children.OfType<LogicalAndExpressionContext>()
               .Select(x => new LogicalAndExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class LogicalAndExpression(ICompilerInfo compilerInfo, LogicalAndExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<InclusiveOrExpression> SubExpressions { get; }
            = context.children.OfType<InclusiveOrExpressionContext>()
               .Select(x => new InclusiveOrExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class InclusiveOrExpression(ICompilerInfo compilerInfo, InclusiveOrExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<ExclusiveOrExpression> SubExpressions { get; }
            = context.children.OfType<ExclusiveOrExpressionContext>()
               .Select(x => new ExclusiveOrExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class ExclusiveOrExpression(ICompilerInfo compilerInfo, ExclusiveOrExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<AndExpression> SubExpressions { get; }
            = context.children.OfType<AndExpressionContext>()
               .Select(x => new AndExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class AndExpression(ICompilerInfo compilerInfo, AndExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<EqualityExpression> SubExpressions { get; }
            = context.children.OfType<EqualityExpressionContext>()
               .Select(x => new EqualityExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class EqualityExpression(ICompilerInfo compilerInfo, EqualityExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<RelationalExpression> SubExpressions { get; }
            = context.children.OfType<RelationalExpressionContext>()
               .Select(x => new RelationalExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class RelationalExpression(ICompilerInfo compilerInfo, RelationalExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<ShiftExpression> SubExpressions { get; }
            = context.children.OfType<ShiftExpressionContext>()
               .Select(x => new ShiftExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class ShiftExpression(ICompilerInfo compilerInfo, ShiftExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<AdditiveExpression> SubExpressions { get; }
            = context.children.OfType<AdditiveExpressionContext>()
               .Select(x => new AdditiveExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class AdditiveExpression(ICompilerInfo compilerInfo, AdditiveExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<MultiplicativeExpression> SubExpressions { get; }
            = context.children.OfType<MultiplicativeExpressionContext>()
               .Select(x => new MultiplicativeExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class MultiplicativeExpression(ICompilerInfo compilerInfo, MultiplicativeExpressionContext context) : GrammarNode(compilerInfo, context), IExpression
    {
        public List<CastExpression> SubExpressions { get; }
            = context.children.OfType<CastExpressionContext>()
               .Select(x => new CastExpression(compilerInfo, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class CastExpression : GrammarNode, IExpression
    {
        public CastExpression(ICompilerInfo compilerInfo, CastExpressionContext context) : base(compilerInfo, context)
        {
            if (context.children.OfType<CastExpressionContext>().Any())
            {
                SubCastExpression = new CastExpression(compilerInfo, context.castExpression());
                CastTypeIdentifier = new Identifier(compilerInfo, context.identifier(), true);
            }
            else
            {
                SubUnaryExpression = new UnaryExpression(compilerInfo, context.unaryExpression());
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

    public class UnaryExpression : GrammarNode, IExpression
    {
        public UnaryExpression(ICompilerInfo compilerInfo, UnaryExpressionContext context) : base(compilerInfo, context)
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
                    _ => throw new System.InvalidOperationException("Invalid unary operator"),
                };
            }
            SubExpression = new PostfixExpression(compilerInfo, context.postfixExpression());
        }

        public PostfixExpression? SubExpression { get; }

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

    public class PostfixExpression : GrammarNode, IExpression
    {
        public enum Type
        {
            PrimaryExpression,
            Slicing,
            FunctionCall,
            MemberAccess,
        }

        public PostfixExpression(ICompilerInfo compilerInfo, PostfixExpressionContext context) : base(compilerInfo, context)
        {
            if (context.children.OfType<PrimaryExpressionContext>().Any())
            {
                SubPrimaryExpression = new PrimaryExpression(compilerInfo, context.primaryExpression());
                PostfixExpressionType = Type.PrimaryExpression;
            }
            else if (context.children.OfType<SlicingExpressionContext>().Any())
            {
                SubSlicingExpression = new SlicingExpression(compilerInfo, context.slicingExpression());
                PostfixExpressionType = Type.Slicing;
            }
            else if (context.children.OfType<FunctionCallExpressionContext>().Any())
            {
                SubFunctionCallExpression = new FunctionCallExpression(compilerInfo, context.functionCallExpression());
                PostfixExpressionType = Type.FunctionCall;
            }
            else if (context.children.OfType<MemberAccessExpressionContext>().Any())
            {
                SubMemberAccessExpression = new MemberAccessExpression(compilerInfo, context.memberAccessExpression());
                PostfixExpressionType = Type.MemberAccess;
            }
            else
            {
                throw new System.InvalidOperationException("Invalid postfix expression");
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
                _ => throw new System.InvalidOperationException("Invalid postfix expression type"),
            };
        }

        public bool IsSimple => PostfixExpressionType == Type.PrimaryExpression ? SubPrimaryExpression!.IsSimple : false;
    }

    public class PrimaryExpression : GrammarNode, IExpression
    {
        public enum Type
        {
            Identifier,
            Constant,
            StringLiteral,
            ParenthesizedExpression,
        }

        public PrimaryExpression(ICompilerInfo compilerInfo, PrimaryExpressionContext context) : base(compilerInfo, context)
        {
            if (context.children.OfType<IdentifierContext>().Any())
            {
                Identifier = new Identifier(compilerInfo, context.identifier(), false);
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
            else if (context.children.OfType<ExpressionContext>().Any())
            {
                ParenthesizedExpression = new Expression(compilerInfo, context.expression());
                PrimaryExpressionType = Type.ParenthesizedExpression;
            }
            else
            {
                throw new System.InvalidOperationException("Invalid primary expression");
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
                Type.Constant => new string[] { IPrettyPrint.PrintText(indentLevel, "ConstantLiteral: " + Literal) },
                Type.StringLiteral => new string[] { IPrettyPrint.PrintText(indentLevel, "StringLiteral: " + Literal) },
                Type.ParenthesizedExpression => ParenthesizedExpression!.PrettyPrint(indentLevel, note),
                _ => throw new System.InvalidOperationException("Invalid primary expression type"),
            };
        }

        public bool IsSimple => PrimaryExpressionType switch
        {
            Type.Identifier => true,
            Type.Constant => true,
            Type.StringLiteral => true,
            Type.ParenthesizedExpression => ParenthesizedExpression!.IsSimple,
            _ => throw new System.InvalidOperationException("Invalid primary expression type"),
        };
    }

    public class SlicingExpression : GrammarNode
    {
        public SlicingExpression(ICompilerInfo compilerInfo, SlicingExpressionContext context) : base(compilerInfo, context)
        {
            PrimaryExpression = new PrimaryExpression(compilerInfo, context.primaryExpression());
            IndexExpression = new Expression(compilerInfo, context.expression());
        }

        public PrimaryExpression PrimaryExpression { get; }

        public Expression IndexExpression { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Slicable)"))
                .Concat(IndexExpression.PrettyPrint(indentLevel + 1, "(Index)"));
        }
    }

    public class FunctionCallExpression : GrammarNode, IExpression
    {
        public FunctionCallExpression(ICompilerInfo compilerInfo, FunctionCallExpressionContext context) : base(compilerInfo, context)
        {
            PrimaryExpression = new PrimaryExpression(compilerInfo, context.primaryExpression());
            ArgumentsExpression = context.children.OfType<ExpressionContext>()
               .Select(x => new Expression(compilerInfo, x))
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

    public class MemberAccessExpression : GrammarNode, IExpression
    {
        public MemberAccessExpression(ICompilerInfo compilerInfo, MemberAccessExpressionContext context) : base(compilerInfo, context)
        {
            PrimaryExpression = new PrimaryExpression(compilerInfo, context.primaryExpression());
            MemberIdentifier = new Identifier(compilerInfo, context.identifier(), false);
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

    public class FunctionDefinition : GrammarNode, IScope
    {
        public FunctionDefinition(ICompilerInfo compilerInfo, FunctionDefinitionContext context) : base(compilerInfo, context)
        {
            compilerInfo.PushScope(ScopeType.Function, this);

            this.FunctionIdentifier = new Identifier(compilerInfo, context.identifier(), false);
            this.Parameters = context.parameterList().children.OfType<DeclarationContext>()
                .Select(x => new Declaration(compilerInfo, x)).ToList();
            this.ReturnType = new TypeSpecifier(compilerInfo, context.typeSpecifier());
            this.CodeBlock = new CodeBlock(compilerInfo, context.codeBlock());

            compilerInfo.PopScope();
        }

        public Identifier FunctionIdentifier { get; }
        public List<Declaration> Parameters { get; }
        public TypeSpecifier ReturnType { get; }
        public CodeBlock CodeBlock { get; }
        public string Name => FunctionIdentifier.Name;
        public string Id => Name;
        public ScopeType ScopeType => ScopeType.Function;
        public List<Symbol> Symbols { get; } = [];
        public Dictionary<string, IScope> SubScopes { get; } = [];
        public IScope? ParentScope { get; set; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(FunctionIdentifier.PrettyPrint(indentLevel + 1, "(FunctionName)"))
                .Concat(Parameters.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")))
                .Concat(ReturnType.PrettyPrint(indentLevel + 1, "(ReturnType)"))
                .Concat(CodeBlock.PrettyPrint(indentLevel + 1));
        }
    }
}