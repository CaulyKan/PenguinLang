using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using static PenguinLangSyntax.PenguinLangParser;

namespace PenguinLangSyntax
{
    public abstract class SyntaxNode : IPrettyPrint
    {
        public SyntaxNode(SyntaxWalker walker, ParserRuleContext context)
        {
            Scope = walker.CurrentScope ?? this as NamespaceDefinition ?? throw new NotImplementedException();
            Context = context;
            Reporter = walker.Reporter;

            var fileNameIdentifier = Path.GetFileNameWithoutExtension(walker.FileName) + counter++;
            SourceLocation = new SourceLocation(walker.FileName, fileNameIdentifier, context.Start.Line, context.Start.Column, context.Stop.Line, context.Stop.Column);
        }

        static ulong counter = 0;
        public ISyntaxScope Scope { get; }
        public ParserRuleContext Context { get; }
        public virtual SourceLocation SourceLocation { get; }
        public ErrorReporter Reporter { get; }
        public virtual string Text =>
            Context.Start.InputStream.GetText(new Interval(Context.Start.StartIndex, Context.Stop.StopIndex));
        public override string ToString() => this.GetType().Name + ": " + shorten(Text.Trim().Replace("\n", " ").Replace("\r", ""));
        public virtual IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            yield return new string(' ', indentLevel * 2) + (note ?? " ") + ToString();
        }
        protected string shorten(string str) => str.Length > 30 ? str.Substring(0, 27) + "..." : str;
    }

    public class NamespaceDefinition : SyntaxNode, ISyntaxScope
    {
        public NamespaceDefinition(SyntaxWalker walker, NamespaceDefinitionContext context) : base(walker, context)
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

        public NamespaceDefinition(SyntaxWalker walker, CompilationUnitContext context) : base(walker, context)
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
                    .Select(x => new NamespaceDefinition(walker, x)));

            Functions.AddRange(
                 namespaceDeclarationContext.children.OfType<FunctionDefinitionContext>()
                    .Select(x => new FunctionDefinition(walker, x)));

            Classes.AddRange(
                 namespaceDeclarationContext.children.OfType<ClassDefinitionContext>()
                    .Select(x => new ClassDefinition(walker, x)));

            Enums.AddRange(
                 namespaceDeclarationContext.children.OfType<EnumDefinitionContext>()
                    .Select(x => new EnumDefinition(walker, x)));

            Interfaces.AddRange(
                 namespaceDeclarationContext.children.OfType<InterfaceDefinitionContext>()
                    .Select(x => new InterfaceDefinition(walker, x)));

            InterfaceImplementations.AddRange(
                 namespaceDeclarationContext.children.OfType<InterfaceForImplementationContext>()
                    .Select(x => new InterfaceForImplementation(walker, x)));
        }

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public List<Declaration> Declarations { get; } = [];

        public List<NamespaceDefinition> SubNamespaces { get; } = [];

        public List<FunctionDefinition> Functions { get; } = [];

        public List<ClassDefinition> Classes { get; } = [];

        public List<EnumDefinition> Enums { get; } = [];

        public List<InterfaceDefinition> Interfaces { get; } = [];

        public List<InterfaceForImplementation> InterfaceImplementations { get; } = [];

        public bool IsEmpty => InitialRoutines.Count == 0 && Declarations.Count == 0 && Functions.Count == 0 && Classes.Count == 0 && Enums.Count == 0 && Interfaces.Count == 0 && InterfaceImplementations.Count == 0;

        public string Name { get; }

        public SyntaxScopeType ScopeType => SyntaxScopeType.Namespace;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];
        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous { get; private set; }

        public uint ScopeDepth { get; set; } = 0;

        public string Fullname
        {
            get
            {
                string result = Name;
                var current = this as ISyntaxScope;
                while (current.ParentScope is not null)
                {
                    result = current.ParentScope.Name + "." + result;
                    current = current.ParentScope;
                }
                return result;
            }
        }

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
            ).Concat(
                Enums.SelectMany(x => x.PrettyPrint(indentLevel + 1))
            ).Concat(
                Interfaces.SelectMany(x => x.PrettyPrint(indentLevel + 1))
            );
        }
    }

    public class InitialRoutine : SyntaxNode, ISyntaxScope
    {
        public InitialRoutine(SyntaxWalker walker, InitialRoutineContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.InitialRoutine, this);

            CodeBlock = new CodeBlock(walker, context.codeBlock());
            Name = context.identifier() == null ? $"initial_{counter++}" : context.identifier().GetText();

            walker.PopScope();
        }

        static UInt64 counter = 0;

        public CodeBlock CodeBlock { get; }

        public string Name { get; }

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
            IfStatement,
            WhileStatement,
            ForStatement,
            JumpStatement,
            AssignmentStatement,
            ReturnStatement,
            YieldStatement,
        }

        public Statement(SyntaxWalker walker, StatementContext context) : base(walker, context)
        {
            if (context.expressionStatement() is not null)
            {
                StatementType = Type.ExpressionStatement;
                ExpressionStatement = new ExpressionStatement(walker, context.expressionStatement());
            }
            else if (context.ifStatement() is not null)
            {
                StatementType = Type.IfStatement;
                IfStatement = new IfStatement(walker, context.ifStatement());
            }
            else if (context.whileStatement() is not null)
            {
                StatementType = Type.WhileStatement;
                WhileStatement = new WhileStatement(walker, context.whileStatement());
            }
            else if (context.forStatement() is not null)
            {
                StatementType = Type.ForStatement;
                ForStatement = new ForStatement(walker, context.forStatement());
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
            else if (context.returnStatement() is not null)
            {
                StatementType = Type.ReturnStatement;
                ReturnStatement = new ReturnStatement(walker, context.returnStatement());
            }
            else if (context.yieldStatement() is not null)
            {
                StatementType = Type.YieldStatement;
                YieldStatement = new YieldStatement(walker, context.yieldStatement());
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

        public IfStatement? IfStatement { get; }

        public ForStatement? ForStatement { get; }

        public WhileStatement? WhileStatement { get; }

        public JumpStatement? JumpStatement { get; }

        public AssignmentStatement? AssignmentStatement { get; }

        public ReturnStatement? ReturnStatement { get; }

        public YieldStatement? YieldStatement { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return StatementType switch
            {
                Type.SubBlock => CodeBlock!.PrettyPrint(indentLevel + 1),
                Type.ExpressionStatement => ExpressionStatement!.PrettyPrint(indentLevel),
                Type.IfStatement => IfStatement!.PrettyPrint(indentLevel),
                Type.ForStatement => ForStatement!.PrettyPrint(indentLevel),
                Type.WhileStatement => WhileStatement!.PrettyPrint(indentLevel),
                Type.JumpStatement => JumpStatement!.PrettyPrint(indentLevel),
                Type.AssignmentStatement => AssignmentStatement!.PrettyPrint(indentLevel),
                Type.ReturnStatement => ReturnStatement!.PrettyPrint(indentLevel),
                Type.YieldStatement => YieldStatement!.PrettyPrint(indentLevel),
                _ => throw new NotImplementedException($"Invalid statement type: {StatementType}"),
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

    public class IfStatement : SyntaxNode
    {
        public IfStatement(SyntaxWalker walker, IfStatementContext context) : base(walker, context)
        {
            Condition = new Expression(walker, context.expression());
            var statements = context.children.OfType<StatementContext>().ToList();
            if (statements.Count == 1)
            {
                MainStatement = new Statement(walker, statements[0]);
            }
            else if (statements.Count == 2)
            {
                MainStatement = new Statement(walker, statements[0]);
                ElseStatement = new Statement(walker, statements[1]);
            }
            else
            {
                throw new System.NotImplementedException("Invalid number of statements in if statement");
            }
        }

        public Expression Condition { get; }

        public Statement MainStatement { get; }

        public Statement? ElseStatement { get; }

        public bool HasElse => ElseStatement is not null;

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                Condition.PrettyPrint(indentLevel + 1, "(condition)")
            ).Concat(
                MainStatement.PrettyPrint(indentLevel + 1, "(main statement)")
            ).Concat(
                HasElse ? ElseStatement!.PrettyPrint(indentLevel + 1, "(else statement)") : []
            );
        }
    }

    public class WhileStatement(SyntaxWalker walker, WhileStatementContext context) : SyntaxNode(walker, context)
    {
        public Expression Condition { get; } = new Expression(walker, context.expression());

        public Statement BodyStatement { get; } = new Statement(walker, context.statement());

        override public IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                Condition.PrettyPrint(indentLevel + 1, "(condition)")
            ).Concat(
                BodyStatement.PrettyPrint(indentLevel + 1, "(body)")
            );
        }
    }

    public class ForStatement(SyntaxWalker walker, ForStatementContext context) : SyntaxNode(walker, context)
    {
        public Declaration Declaration { get; } = new Declaration(walker, context.declaration());

        public Expression Expression { get; } = new Expression(walker, context.expression());

        public Statement BodyStatement { get; } = new Statement(walker, context.statement());

        override public IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                Expression.PrettyPrint(indentLevel + 1, "(expression)")
            ).Concat(
                BodyStatement.PrettyPrint(indentLevel + 1, "(body)")
            );
        }
    }

    public class JumpStatement : SyntaxNode
    {
        public enum Type
        {
            Break,
            Continue
        }

        public JumpStatement(SyntaxWalker walker, JumpStatementContext context) : base(walker, context)
        {
            if (context.jumpKeyword().GetText() == "break")
            {
                JumpType = Type.Break;
            }
            else if (context.jumpKeyword().GetText() == "continue")
            {
                JumpType = Type.Continue;
            }
            else
            {
                throw new NotImplementedException("Invalid jump statement type");
            }
        }

        public Type JumpType { get; }
    }

    public class ReturnStatement(SyntaxWalker walker, ReturnStatementContext context) : SyntaxNode(walker, context)
    {
        public Expression? ReturnExpression { get; } = context.expression() is not null ? new Expression(walker, context.expression()) : null;
    }

    public class YieldStatement(SyntaxWalker walker, YieldStatementContext context) : SyntaxNode(walker, context)
    {
        public Expression? YieldExpression { get; } = context.expression() is not null ? new Expression(walker, context.expression()) : null;
    }

    public class IdentifierOrMemberAccess : SyntaxNode
    {
        public IdentifierOrMemberAccess(SyntaxWalker walker, IdentifierOrMemberAccessContext context) : base(walker, context)
        {
            if (context.identifier() is not null)
            {
                Identifier = new Identifier(walker, context.identifier(), false);
            }
            else if (context.memberAccessExpression() is not null)
            {
                MemberAccess = new MemberAccessExpression(walker, context.memberAccessExpression(), true);
            }
            else
            {
                throw new NotImplementedException("Invalid identifier or member access");
            }
        }

        public Identifier? Identifier { get; }

        public MemberAccessExpression? MemberAccess { get; }

        public bool IsIdentifier => Identifier is not null;

        public bool IsMemberAccess => MemberAccess is not null;

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return Identifier is not null ? Identifier.PrettyPrint(indentLevel) : MemberAccess!.PrettyPrint(indentLevel + 1);
        }
    }

    public class AssignmentStatement : SyntaxNode
    {
        public AssignmentStatement(SyntaxWalker walker, AssignmentStatementContext context) : base(walker, context)
        {
            LeftHandSide = new IdentifierOrMemberAccess(walker, context.identifierOrMemberAccess());
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

        public IdentifierOrMemberAccess LeftHandSide { get; }
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

        public Identifier(SyntaxWalker walker, IdentifierWithGenericContext context, bool isType) : base(walker, context)
        {
            this.IsType = isType;
            this.LiteralName = context.GetText();
        }

        public bool IsType { get; }

        public string LiteralName { get; }

        public string Name => LiteralName;
    }

    public class TypeSpecifier : SyntaxNode
    {
        public TypeSpecifier(SyntaxWalker walker, TypeSpecifierContext context) : base(walker, context)
        {
            Name = context.GetText();
        }

        public TypeSpecifier(SyntaxWalker walker, string liternalName, ParserRuleContext context) : base(walker, context)
        {
            Name = liternalName;
        }

        public string Name { get; }
    }

    public class Declaration : SyntaxNode
    {
        public Declaration(SyntaxWalker walker, DeclarationContext context) : base(walker, context)
        {
            Identifier = new Identifier(walker, context.identifier(), false);
            if (context.typeSpecifier() != null)
            {
                TypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
                walker.DefineSymbol(Name, TypeSpecifier.Name, this);
            }
            else
            {
                throw new NotImplementedException("Type infer is not supported yet");
            }
            IsReadonly = context.declarationKeyword().GetText() == "val";

            if (context.expression() != null)
                InitializeExpression = new Expression(walker, context.expression());
        }

        public Identifier Identifier { get; }

        public TypeSpecifier? TypeSpecifier { get; }

        public Expression? InitializeExpression { get; }

        public bool IsReadonly;

        public string Name => Identifier.Name;

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel).Concat(
                Identifier.PrettyPrint(indentLevel + 1)
            ).Concat(
                TypeSpecifier?.PrettyPrint(indentLevel + 1, "(type)") ?? []
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
        public List<BitWiseOrExpression> SubExpressions { get; }
            = context.children.OfType<InclusiveOrExpressionContext>()
               .Select(x => new BitWiseOrExpression(walker, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class BitWiseOrExpression(SyntaxWalker walker, InclusiveOrExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
    {
        public List<BitwiseXorExpression> SubExpressions { get; }
            = context.children.OfType<ExclusiveOrExpressionContext>()
               .Select(x => new BitwiseXorExpression(walker, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class BitwiseXorExpression(SyntaxWalker walker, ExclusiveOrExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
    {
        public List<BitwiseAndExpression> SubExpressions { get; }
            = context.children.OfType<AndExpressionContext>()
               .Select(x => new BitwiseAndExpression(walker, x))
               .ToList();

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return SubExpressions.Count == 1 ? SubExpressions[0].PrettyPrint(indentLevel) : base.PrettyPrint(indentLevel).Concat(SubExpressions.SelectMany(x => x.PrettyPrint(indentLevel + 1)));
        }

        public bool IsSimple => SubExpressions.Count == 1 ? SubExpressions[0].IsSimple : false;
    }

    public class BitwiseAndExpression(SyntaxWalker walker, AndExpressionContext context) : SyntaxNode(walker, context), ISyntaxExpression
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

        public BinaryOperatorEnum? Operator { get; } = context.equalityOperator() is null ? null : context.equalityOperator().GetText() switch
        {
            "==" => BinaryOperatorEnum.Equal,
            "!=" => BinaryOperatorEnum.NotEqual,
            "is" => BinaryOperatorEnum.Is,
            _ => throw new System.NotImplementedException("Invalid equality operator"),
        };

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
            if (context.typeSpecifier() != null)
            {
                SubUnaryExpression = new UnaryExpression(walker, context.unaryExpression());
                CastTypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
            }
            else
            {
                SubUnaryExpression = new UnaryExpression(walker, context.unaryExpression());
            }
        }

        public TypeSpecifier? CastTypeSpecifier { get; }

        public UnaryExpression SubUnaryExpression { get; }

        public bool IsTypeCast => CastTypeSpecifier is not null;

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            if (IsTypeCast)
                return base.PrettyPrint(indentLevel).Concat(SubUnaryExpression.PrettyPrint(indentLevel + 1));
            else
                return SubUnaryExpression.PrettyPrint(indentLevel);
        }

        public bool IsSimple => IsTypeCast ? false : SubUnaryExpression.IsSimple;
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
            // Slicing,
            FunctionCall,
            MemberAccess,
            New,
        }

        public PostfixExpression(SyntaxWalker walker, PostfixExpressionContext context) : base(walker, context)
        {
            if (context.primaryExpression() != null)
            {
                SubPrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
                PostfixExpressionType = Type.PrimaryExpression;
            }
            // else if (context.children.OfType<SlicingExpressionContext>().Any())
            // {
            //     SubSlicingExpression = new SlicingExpression(walker, context.slicingExpression());
            //     PostfixExpressionType = Type.Slicing;
            // }
            else if (context.functionCallExpression() != null)
            {
                SubFunctionCallExpression = new FunctionCallExpression(walker, context.functionCallExpression());
                PostfixExpressionType = Type.FunctionCall;
            }
            else if (context.memberAccessExpression() != null)
            {
                SubMemberAccessExpression = new MemberAccessExpression(walker, context.memberAccessExpression(), false);
                PostfixExpressionType = Type.MemberAccess;
            }
            else if (context.newExpression() != null)
            {
                SubNewExpression = new NewExpression(walker, context.newExpression());
                PostfixExpressionType = Type.New;
            }
            else
            {
                throw new NotImplementedException("Invalid postfix expression");
            }
        }

        public Type PostfixExpressionType { get; }

        public PrimaryExpression? SubPrimaryExpression { get; }

        // public SlicingExpression? SubSlicingExpression { get; }

        public FunctionCallExpression? SubFunctionCallExpression { get; }

        public MemberAccessExpression? SubMemberAccessExpression { get; }

        public NewExpression? SubNewExpression { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return PostfixExpressionType switch
            {
                Type.PrimaryExpression => SubPrimaryExpression!.PrettyPrint(indentLevel),
                // Type.Slicing => SubSlicingExpression!.PrettyPrint(indentLevel),
                Type.FunctionCall => SubFunctionCallExpression!.PrettyPrint(indentLevel),
                Type.MemberAccess => SubMemberAccessExpression!.PrettyPrint(indentLevel),
                Type.New => SubNewExpression!.PrettyPrint(indentLevel),
                _ => throw new NotImplementedException("Invalid postfix expression type"),
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
            VoidLiteral,
            ParenthesizedExpression,
        }

        public PrimaryExpression(SyntaxWalker walker, PrimaryExpressionContext context) : base(walker, context)
        {
            if (context.children.OfType<IdentifierWithGenericContext>().Any())
            {
                Identifier = new Identifier(walker, context.identifierWithGeneric(), false);
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
            else if (context.voidLiteral() != null)
            {
                Literal = context.GetText();
                PrimaryExpressionType = Type.VoidLiteral;
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
                Type.VoidLiteral => [IPrettyPrint.PrintText(indentLevel, "VoidLiteral: " + Literal)],
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
            Type.VoidLiteral => true,
            Type.ParenthesizedExpression => ParenthesizedExpression!.IsSimple,
            _ => throw new System.NotImplementedException("Invalid primary expression type"),
        };
    }

    // public class SlicingExpression : SyntaxNode, ISyntaxExpression
    // {
    //     public SlicingExpression(SyntaxWalker walker, SlicingExpressionContext context) : base(walker, context)
    //     {
    //         PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
    //         IndexExpression = new Expression(walker, context.expression());
    //     }

    //     public PrimaryExpression PrimaryExpression { get; }

    //     public Expression IndexExpression { get; }

    //     public bool IsSimple => false;

    //     public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
    //     {
    //         return base.PrettyPrint(indentLevel)
    //             .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Slicable)"))
    //             .Concat(IndexExpression.PrettyPrint(indentLevel + 1, "(Index)"));
    //     }
    // }

    public class NewExpression : SyntaxNode, ISyntaxExpression
    {
        public NewExpression(SyntaxWalker walker, NewExpressionContext context) : base(walker, context)
        {
            TypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
            ArgumentsExpression = context.children.OfType<ExpressionContext>()
               .Select(x => new Expression(walker, x))
               .ToList();
        }

        public TypeSpecifier TypeSpecifier { get; }

        public List<Expression> ArgumentsExpression { get; }

        public override string ToString() => "new " + TypeSpecifier.Name;

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(ArgumentsExpression.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")));
        }

        public bool IsSimple => false;
    }

    public class FunctionCallExpression : SyntaxNode, ISyntaxExpression
    {
        public FunctionCallExpression(SyntaxWalker walker, FunctionCallExpressionContext context) : base(walker, context)
        {
            if (context.primaryExpression() != null)
            {
                PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
            }
            else if (context.memberAccessExpression() != null)
            {
                MemberAccessExpression = new MemberAccessExpression(walker, context.memberAccessExpression(), false);
            }
            ArgumentsExpression = context.children.OfType<ExpressionContext>()
               .Select(x => new Expression(walker, x))
               .ToList();
        }

        public PrimaryExpression? PrimaryExpression { get; }

        public MemberAccessExpression? MemberAccessExpression { get; }

        public bool IsMemberAccess => MemberAccessExpression is not null;

        public List<Expression> ArgumentsExpression { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(IsMemberAccess ? MemberAccessExpression!.PrettyPrint(indentLevel + 1, "(Function)") : PrimaryExpression!.PrettyPrint(indentLevel + 1, "(Function)"))
                .Concat(ArgumentsExpression.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")));
        }

        public bool IsSimple => false;
    }

    public class MemberAccessExpression : SyntaxNode, ISyntaxExpression
    {
        public MemberAccessExpression(SyntaxWalker walker, MemberAccessExpressionContext context, bool isWrite) : base(walker, context)
        {
            PrimaryExpression = new PrimaryExpression(walker, context.primaryExpression());
            MemberIdentifiers = context.children.OfType<IdentifierWithGenericContext>()
               .Select(x => new Identifier(walker, x, false))
               .ToList();
            IsWrite = isWrite;
        }

        public PrimaryExpression PrimaryExpression { get; }

        public List<Identifier> MemberIdentifiers { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(PrimaryExpression.PrettyPrint(indentLevel + 1, "(Object)"))
                .Concat(MemberIdentifiers.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Member)")));
        }

        public bool IsSimple => false;

        public bool IsWrite { get; }
    }

    public class FunctionDefinition : SyntaxNode, ISyntaxScope
    {
        public FunctionDefinition(SyntaxWalker walker, FunctionDefinitionContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Function, this);

            if (context.identifier() != null)
                FunctionIdentifier = new Identifier(walker, context.identifier(), false);
            else
                FunctionIdentifier = new Identifier(walker, "new", context, false);

            walker.DefineSymbol(Name, "fun", this);

            if (context.parameterList().children == null)
            {
                Parameters = [];
            }
            else
            {
                Parameters = context.parameterList().children.OfType<DeclarationContext>()
                    .Select(x => new Declaration(walker, x)).ToList();
            }

            if (context.typeSpecifier() == null)
            {
                ReturnType = new TypeSpecifier(walker, "void", context);
            }
            else
            {
                ReturnType = new TypeSpecifier(walker, context.typeSpecifier());
            }

            IsExtern = false;
            IsAsync = false;
            IsPure = null;

            foreach (var specifierContext in context.children.OfType<FunctionSpecifierContext>())
            {
                if (specifierContext.GetText() == "extern")
                {
                    IsExtern = true;
                }
                else if (specifierContext.GetText() == "pure")
                {
                    IsPure = true;
                }
                else if (specifierContext.GetText() == "!pure")
                {
                    IsPure = false;
                }
                else if (specifierContext.GetText() == "async")
                {
                    IsAsync = true;
                }
            }

            if (context.codeBlock() != null)
                CodeBlock = new CodeBlock(walker, context.codeBlock());

            if (context.declarationKeyword() != null)
                ReturnValueIsReadonly = context.declarationKeyword().GetText() == "val";

            walker.PopScope();
        }

        public Identifier FunctionIdentifier { get; }

        public List<Declaration> Parameters { get; }

        public TypeSpecifier ReturnType { get; }

        public bool? ReturnValueIsReadonly { get; }

        public CodeBlock? CodeBlock { get; }

        public string Name => FunctionIdentifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Function;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsAnonymous => false;

        public uint ScopeDepth { get; set; }

        public bool IsExtern { get; }

        public bool? IsPure { get; }

        public bool IsAsync { get; }

        public override IEnumerable<string> PrettyPrint(int indentLevel, string? note = null)
        {
            return base.PrettyPrint(indentLevel)
                .Concat(FunctionIdentifier.PrettyPrint(indentLevel + 1, "(FunctionName)"))
                .Concat(Parameters.SelectMany(x => x.PrettyPrint(indentLevel + 1, "(Parameter)")))
                .Concat(ReturnType.PrettyPrint(indentLevel + 1, "(ReturnType)"))
                .Concat(CodeBlock?.PrettyPrint(indentLevel + 1) ?? []);
        }
    }

    public class ClassDefinition : SyntaxNode, ISyntaxScope
    {
        public ClassDefinition(SyntaxWalker walker, ClassDefinitionContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Class, this);

            ClassIdentifier = new Identifier(walker, context.identifier(), false);
            ClassDeclarations = context.children.OfType<ClassDeclarationContext>()
               .Select(x => new ClassDeclaration(walker, x))
               .ToList();
            Functions = context.children.OfType<FunctionDefinitionContext>()
               .Select(x => new FunctionDefinition(walker, x))
               .ToList();
            GenericDefinitions = context.genericDefinitions() != null ? new GenericDefinitions(walker, context.genericDefinitions()) : null;
            InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
               .Select(x => new InterfaceImplementation(walker, x))
               .ToList();

            walker.PopScope();
        }

        public Identifier ClassIdentifier { get; }

        public string Name => ClassIdentifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public List<FunctionDefinition> Functions { get; } = [];

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public uint ScopeDepth { get; set; }

        public List<ClassDeclaration> ClassDeclarations { get; } = [];

        public GenericDefinitions? GenericDefinitions { get; } = null;

        public List<InterfaceImplementation> InterfaceImplementations { get; } = [];
    }

    public class GenericDefinitions : SyntaxNode
    {
        public GenericDefinitions(SyntaxWalker walker, GenericDefinitionsContext context) : base(walker, context)
        {
            TypeParameters = context.children.OfType<IdentifierContext>()
               .Select(x => new Identifier(walker, x, false))
               .ToList();
        }

        public List<Identifier> TypeParameters { get; } = [];
    }

    public class GenericArguments : SyntaxNode
    {
        public GenericArguments(SyntaxWalker walker, GenericArgumentsContext context) : base(walker, context)
        {
            TypeParameters = context.children.OfType<TypeSpecifierContext>()
               .Select(x => new TypeSpecifier(walker, x))
               .ToList();
        }

        public List<TypeSpecifier> TypeParameters { get; } = [];
    }

    public class ClassDeclaration : SyntaxNode, ISyntaxScope
    {
        public ClassDeclaration(SyntaxWalker walker, ClassDeclarationContext context) : base(walker, context)
        {
            Identifier = new Identifier(walker, context.identifier(), false);
            TypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
            IsReadonly = context.declarationKeyword().GetText() == "val";
            Initializer = context.expression() != null ? new Expression(walker, context.expression()) : null;
        }

        public Identifier Identifier { get; }

        public TypeSpecifier TypeSpecifier { get; }

        public Expression? Initializer { get; }

        public string Name => Identifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public bool IsAnonymous => false;

        public uint ScopeDepth { get; set; }

        public ISyntaxScope? ParentScope { get; set; }

        public bool IsReadonly { get; }
    }


    public class EnumDefinition : SyntaxNode, ISyntaxScope
    {
        public EnumDefinition(SyntaxWalker walker, EnumDefinitionContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Enum, this);

            EnumIdentifier = new Identifier(walker, context.identifier(), false);
            EnumDeclarations = context.children.OfType<EnumDeclarationContext>()
               .Select(x => new EnumDeclaration(walker, x))
               .ToList();
            Functions = context.children.OfType<FunctionDefinitionContext>()
               .Select(x => new FunctionDefinition(walker, x))
               .ToList();
            InitialRoutines = context.children.OfType<InitialRoutineContext>()
                               .Select(x => new InitialRoutine(walker, x))
                               .ToList();
            GenericDefinitions = context.genericDefinitions() != null ? new GenericDefinitions(walker, context.genericDefinitions()) : null;
            InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
                .Select(x => new InterfaceImplementation(walker, x))
                .ToList();


            walker.PopScope();
        }

        public Identifier EnumIdentifier { get; }

        public string Name => EnumIdentifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Class;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public List<FunctionDefinition> Functions { get; } = [];

        public List<InitialRoutine> InitialRoutines { get; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public uint ScopeDepth { get; set; }

        public List<EnumDeclaration> EnumDeclarations { get; } = [];

        public GenericDefinitions? GenericDefinitions { get; } = null;

        public List<InterfaceImplementation> InterfaceImplementations { get; } = [];
    }

    public class EnumDeclaration : SyntaxNode, ISyntaxScope
    {
        public EnumDeclaration(SyntaxWalker walker, EnumDeclarationContext context) : base(walker, context)
        {
            Identifier = new Identifier(walker, context.identifier(), false);
            TypeSpecifier = context.typeSpecifier() != null ? new TypeSpecifier(walker, context.typeSpecifier()) : null;
        }

        public Identifier Identifier { get; }

        public TypeSpecifier? TypeSpecifier { get; }

        public string Name => Identifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Enum;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public bool IsAnonymous => false;

        public uint ScopeDepth { get; set; }

        public ISyntaxScope? ParentScope { get; set; }
    }

    public class InterfaceDefinition : SyntaxNode, ISyntaxScope
    {
        public InterfaceDefinition(SyntaxWalker walker, InterfaceDefinitionContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Interface, this);

            InterfaceIdentifier = new Identifier(walker, context.identifier(), false);
            Functions = context.children.OfType<FunctionDefinitionContext>()
               .Select(x => new FunctionDefinition(walker, x))
               .ToList();
            GenericDefinitions = context.genericDefinitions() != null ? new GenericDefinitions(walker, context.genericDefinitions()) : null;
            InterfaceImplementations = context.children.OfType<InterfaceImplementationContext>()
               .Select(x => new InterfaceImplementation(walker, x))
               .ToList();

            walker.PopScope();
        }

        public Identifier InterfaceIdentifier { get; }

        public string Name => InterfaceIdentifier.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.Interface;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public List<FunctionDefinition> Functions { get; } = [];

        public bool IsAnonymous => false;

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public ISyntaxScope? ParentScope { get; set; }

        public uint ScopeDepth { get; set; }

        public GenericDefinitions? GenericDefinitions { get; } = null;

        public List<InterfaceImplementation> InterfaceImplementations { get; } = [];
    }

    public interface IInterfaceImplementation
    {
        TypeSpecifier InterfaceType { get; }

        string Name => InterfaceType.Name;

        SyntaxScopeType ScopeType { get; }

        List<SyntaxSymbol> Symbols { get; }

        Dictionary<string, ISyntaxScope> SubScopes { get; }

        ISyntaxScope? ParentScope { get; set; }

        List<FunctionDefinition> Functions { get; }

        WhereDefinition? WhereDefinition { get; set; }
    }

    public class InterfaceImplementation : SyntaxNode, ISyntaxScope, IInterfaceImplementation
    {
        public InterfaceImplementation(SyntaxWalker walker, InterfaceImplementationContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Interface, this);

            InterfaceType = new TypeSpecifier(walker, context.typeSpecifier());
            Functions = context.children.OfType<FunctionDefinitionContext>()
               .Select(x => new FunctionDefinition(walker, x))
               .ToList();
            WhereDefinition = context.whereDefinition() != null ? new WhereDefinition(walker, context.whereDefinition()) : null;

            walker.PopScope();
        }

        public TypeSpecifier InterfaceType { get; }

        public string Name => InterfaceType.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.InterfaceImplementation;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public bool IsAnonymous => false;

        public uint ScopeDepth { get; set; }

        public ISyntaxScope? ParentScope { get; set; }

        public List<FunctionDefinition> Functions { get; } = [];

        public WhereDefinition? WhereDefinition { get; set; }
    }

    public class InterfaceForImplementation : SyntaxNode, ISyntaxScope, IInterfaceImplementation
    {
        public InterfaceForImplementation(SyntaxWalker walker, InterfaceForImplementationContext context) : base(walker, context)
        {
            walker.PushScope(SyntaxScopeType.Interface, this);

            InterfaceType = new TypeSpecifier(walker, context.typeSpecifier()[0]);
            ForType = new TypeSpecifier(walker, context.typeSpecifier()[1]);
            Functions = context.children.OfType<FunctionDefinitionContext>()
               .Select(x => new FunctionDefinition(walker, x))
               .ToList();
            WhereDefinition = context.whereDefinition() != null ? new WhereDefinition(walker, context.whereDefinition()) : null;

            walker.PopScope();
        }

        public TypeSpecifier InterfaceType { get; }

        public TypeSpecifier ForType { get; }

        public string Name => InterfaceType.Name;

        public SyntaxScopeType ScopeType => SyntaxScopeType.InterfaceImplementation;

        public List<SyntaxSymbol> Symbols { get; } = [];

        public Dictionary<string, ISyntaxScope> SubScopes { get; } = [];

        public bool IsAnonymous => false;

        public uint ScopeDepth { get; set; }

        public ISyntaxScope? ParentScope { get; set; }

        public List<FunctionDefinition> Functions { get; } = [];

        public WhereDefinition? WhereDefinition { get; set; }
    }

    public class WhereClause : SyntaxNode
    {
        public WhereClause(SyntaxWalker walker, WhereClauseContext context) : base(walker, context)
        {
            Identifier = new Identifier(walker, context.identifier(), false);
            TypeSpecifier = new TypeSpecifier(walker, context.typeSpecifier());
        }

        public Identifier Identifier { get; }

        public TypeSpecifier TypeSpecifier { get; }
    }

    public class WhereDefinition : SyntaxNode
    {
        public WhereDefinition(SyntaxWalker walker, WhereDefinitionContext context) : base(walker, context)
        {
            WhereClauses = context.children.OfType<WhereClauseContext>()
               .Select(x => new WhereClause(walker, x))
               .ToList();
        }

        public List<WhereClause> WhereClauses { get; } = [];
    }
}