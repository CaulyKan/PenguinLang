using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;

namespace BabyPenguin
{
    using BabyPenguin.Syntax;
    using Semantic;

    public record TypeInfo(TypeEnum Type, string Name, string Namespace, Syntax.SyntaxNode? SyntaxNode)
    {
        public string FullName => string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}.{Name}";

        public static readonly TypeInfo[] BuiltinTypes = [
            new TypeInfo(TypeEnum.Bool, "bool", "", null),
            new TypeInfo(TypeEnum.Double, "double", "", null),
            new TypeInfo(TypeEnum.Float, "float", "", null),
            new TypeInfo(TypeEnum.String, "string", "", null),
            new TypeInfo(TypeEnum.Void, "void", "", null),
            new TypeInfo(TypeEnum.U8, "u8", "", null),
            new TypeInfo(TypeEnum.U16, "u16", "", null),
            new TypeInfo(TypeEnum.U32, "u32", "", null),
            new TypeInfo(TypeEnum.U64, "u64", "", null),
            new TypeInfo(TypeEnum.I8, "i8", "", null),
            new TypeInfo(TypeEnum.I16, "i16", "", null),
            new TypeInfo(TypeEnum.I32, "i32", "", null),
            new TypeInfo(TypeEnum.I64, "i64", "", null),
            new TypeInfo(TypeEnum.Fun, "fun", "", null),
        ];


    }

    public class SemanticModel
    {
        public SemanticModel(ErrorReporter reporter)
        {
            Reporter = reporter;
        }

        public void Compile(IEnumerable<SyntaxCompiler> compilers)
        {
            foreach (var compiler in compilers)
            {
                foreach (var ns in compiler.Namespaces)
                {
                    Namespaces.Add(new Semantic.Namespace(this, ns));
                }
            }
        }

        public TypeInfo? ResolveType(string name, SyntaxWalker? walker = null)
        {
            var builtin = TypeInfo.BuiltinTypes.FirstOrDefault(t => t.Name == name);
            if (builtin != null)
            {
                return builtin;
            }

            if (walker == null)
            {
                return Types.FirstOrDefault(t => t.FullName == name);
            }
            else
            {
                // TODO: resolve relative type name
                throw new NotImplementedException();
            }
        }


        public List<Semantic.Namespace> Namespaces { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<TypeInfo> Types { get; } = [];
        public ErrorReporter Reporter { get; }
    }

    namespace Semantic
    {
        public enum SymbolTypeEnum
        {
            LocalVariable,
            ClassField,
            ClassMethod,
            GlobalVariable,
            GlobalFunction,
        }

        public abstract class SemanticNode : IPrettyPrint
        {
            public SemanticModel Model { get; }
            public SourceLocation SourceLocation { get; }
            public SemanticNode(SemanticModel model)
            {
                Model = model;
                SourceLocation = new SourceLocation("<annonymous>", "<annonymous>_" + Guid.NewGuid().ToString().Replace("-", ""), 0, 0, 0, 0);
            }

            public SemanticNode(SemanticModel model, Syntax.SyntaxNode syntaxNode)
            {
                Model = model;
                SourceLocation = syntaxNode.SourceLocation;
            }
        }

        public abstract class CodeContainer(SemanticModel model, SyntaxNode syntaxNode) : SemanticNode(model, syntaxNode)
        {
            public List<IStatement> Statements { get; } = [];
            public List<Declaration> Declarations { get; } = [];
            public void AddCodeBlock(Syntax.CodeBlock codeBlock)
            {

                // foreach (var item in codeBlock.BlockItems)
                // {
                //     if (item.IsDeclaration)
                //     {
                //         Declarations.Add(new Declaration(item.Declaration!));
                //     }
                //     else
                //     {
                //         switch (item.Statement!.StatementType)
                //         {
                //             case Statement.Type.AssignmentStatement:
                //                 Statements.Add(new AssignmentStatement(item.Statement!.AssignmentStatement!));
                //                 break;
                //             case Statement.Type.ExpressionStatement:
                //                 Statements.Add(new ExpressionStatement(item.Statement!.ExpressionStatement!));
                //                 break;
                //             case Statement.Type.SubBlock:
                //                 AddCodeBlock(item.Statement!.CodeBlock!);
                //                 break;
                //             default:
                //                 throw new NotImplementedException();
                //         }
                //     }
                // }
            }
        }


        public interface IClassContainer
        {
            public Class AddClass(string name)
            {
                var class_ = new Class(Model, this, name);

                return AddClass(class_);
            }

            public Class AddClass(ClassDefinition syntaxNode)
            {
                var class_ = new Class(Model, this, syntaxNode);

                return AddClass(class_);
            }

            Class AddClass(Class class_)
            {
                if (this.Classes.Any(c => c.Name == class_.Name))
                {
                    Reporter.Write(DiagnosticLevel.Error, $"Class '{class_.Name}' already exists in '{FullName}'", class_.SourceLocation);
                    throw new InvalidDataException();
                }

                Classes.Add(class_);
                Model.Classes.Add(class_);
                Model.Types.Add(new TypeInfo(TypeEnum.Other, class_.Name, FullName, null));

                return class_;
            }

            ErrorReporter Reporter => Model.Reporter;
            SemanticModel Model { get; }
            List<Class> Classes { get; }
            string FullName { get; }
        }

        public class Namespace : SemanticNode, IClassContainer
        {
            public string Name { get; }
            public string FullName => Name;
            public List<Declaration> Declarations { get; } = [];
            public List<InitialRoutine> InitialRoutines { get; } = [];
            public List<Function> Functions { get; } = [];
            public List<Class> Classes { get; } = [];

            public Namespace(SemanticModel model, Syntax.Namespace syntaxNode) : base(model, syntaxNode)
            {
                Name = syntaxNode.Name;
                foreach (var classNode in syntaxNode.Classes)
                    (this as IClassContainer).AddClass(classNode);
            }

        }
        public class Class : SemanticNode
        {
            public Class(SemanticModel model, IClassContainer ns, ClassDefinition syntaxNode) : base(model, syntaxNode)
            {
                Name = syntaxNode.Name;
                Parent = ns;
            }

            public Class(SemanticModel model, IClassContainer ns, string name) : base(model)
            {
                Name = name;
                Parent = ns;
            }

            public string Name { get; }
            public IClassContainer Parent { get; }
        }

        public class Function : SemanticNode
        {
            public Function(SemanticModel model, Syntax.FunctionDefinition syntaxNode) : base(model, syntaxNode)
            {
                Name = syntaxNode.Name;
            }

            public string Name { get; }
        }

        public class InitialRoutine : CodeContainer
        {
            public InitialRoutine(SemanticModel model, Syntax.InitialRoutine syntaxNode) : base(model, syntaxNode)
            {
                Name = syntaxNode.Name;
                AddCodeBlock(syntaxNode.CodeBlock);
            }

            public string Name { get; }
        }

        public interface IStatement { }

        public class AssignmentStatement : SemanticNode, IStatement
        {
            public AssignmentStatement(SemanticModel model, Syntax.AssignmentStatement syntaxNode) : base(model, syntaxNode)
            {
            }
        }

        public class ExpressionStatement : SemanticNode, IStatement
        {
            public ExpressionStatement(SemanticModel model, Syntax.ExpressionStatement syntaxNode) : base(model, syntaxNode)
            {

            }
        }

        public class Declaration : SemanticNode
        {
            public Declaration(SemanticModel model, Syntax.Declaration syntaxNode) : base(model, syntaxNode)
            {
            }
        }
    }
}