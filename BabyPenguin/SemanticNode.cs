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
    public class TypeInfo
    {
        public string FullName
        {
            get
            {
                var n = string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}.{Name}";
                if (GenericArguments.Count > 0)
                {
                    n += "<" + string.Join(",", GenericArguments.Select(t => t.FullName)) + ">";
                }
                return n;
            }
        }

        public static readonly TypeInfo[] BuiltinTypes = [
            new TypeInfo( "bool", "", []),
            new TypeInfo( "double", "", []),
            new TypeInfo( "float", "", []),
            new TypeInfo( "string", "", []),
            new TypeInfo( "void", "", []),
            new TypeInfo( "u8", "", []),
            new TypeInfo( "u16", "", []),
            new TypeInfo( "u32", "", []),
            new TypeInfo( "u64", "", []),
            new TypeInfo( "i8", "", []),
            new TypeInfo( "i16", "", []),
            new TypeInfo( "i32", "", []),
            new TypeInfo( "i64", "", []),
            new TypeInfo( "char","", []),
        ];

        public string Name { get; }
        public string Namespace { get; }
        public TypeEnum Type { get; }
        public List<TypeInfo> GenericArguments { get; } = [];

        public TypeInfo(string name, string namespace_, List<TypeInfo> genericArguments)
        {
            if (Enum.GetNames<TypeEnum>().FirstOrDefault(i => i.Equals(name, StringComparison.CurrentCultureIgnoreCase)) is string n)
            {
                Type = (TypeEnum)Enum.Parse(typeof(TypeEnum), n);
            }
            else
            {
                Type = TypeEnum.Other;
            }

            Name = name;
            Namespace = namespace_;
            GenericArguments = genericArguments;
        }
    }

    public interface ISymbol
    {
        string FullName => Parent.FullName + "." + Name;
        string Name { get; }
        ISymbolContainer Parent { get; }
        TypeInfo Type { get; }
        SourceLocation SourceLocation { get; }
        bool IsLocal { get; }
    }

    public class VaraibleSymbol : ISymbol
    {
        public VaraibleSymbol(ISymbolContainer parent, bool isLocal, string name, TypeInfo type, SourceLocation sourceLocation)
        {
            Parent = parent;
            Name = name;
            Type = type;
            SourceLocation = sourceLocation;
            IsLocal = isLocal;
        }

        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; }
        public ISymbolContainer Parent { get; }
        public TypeInfo Type { get; }
        public SourceLocation SourceLocation { get; }
        public bool IsLocal { get; }
    }

    public class FunctionSymbol : ISymbol
    {
        public FunctionSymbol(ISymbolContainer parent, bool isLocal, string name, SourceLocation sourceLocation, TypeInfo returnType, Dictionary<string, TypeInfo> parameters)
        {
            Parent = parent;
            Name = name;
            ReturnType = returnType;
            SourceLocation = sourceLocation;
            Parameters = parameters;
            IsLocal = isLocal;

            var funTypeGenericArguments = new[] { returnType }.Concat(parameters.Values).ToList();
            Type = parent.Model.ResolveOrCreateType("fun", "", funTypeGenericArguments);
        }

        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; }
        public ISymbolContainer Parent { get; }
        public TypeInfo ReturnType { get; }
        public Dictionary<string, TypeInfo> Parameters { get; }
        public SourceLocation SourceLocation { get; }
        public TypeInfo Type { get; }
        public bool IsLocal { get; }
    }

    public abstract class SemanticNode : IPrettyPrint
    {
        public SemanticModel Model { get; }
        public SourceLocation SourceLocation { get; }
        public Syntax.SyntaxNode? SyntaxNode { get; }
        public SemanticNode(SemanticModel model)
        {
            Model = model;
            SourceLocation = new SourceLocation("<annonymous>", "<annonymous>_" + Guid.NewGuid().ToString().Replace("-", ""), 0, 0, 0, 0);
        }

        public SemanticNode(SemanticModel model, Syntax.SyntaxNode syntaxNode)
        {
            Model = model;
            SourceLocation = syntaxNode.SourceLocation;
            SyntaxNode = syntaxNode;
        }
    }

    public abstract class CodeContainer(SemanticModel model, Syntax.SyntaxNode syntaxNode) : SemanticNode(model, syntaxNode)
    {
        public List<IStatement> Statements { get; } = [];
        public List<Syntax.Declaration> Declarations { get; } = [];
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

    public interface ISymbolContainer
    {
        string FullName { get; }

        List<ISymbol> Symbols { get; }

        ErrorReporter Reporter => Model.Reporter;

        SemanticModel Model { get; }

        void ResolveSyntaxSymbols();

        void AddSymbol(string Name, bool isLocal, string typeName, SourceLocation SourceLocation, ISyntaxScope? scope = null)
        {
            var type = Model.ResolveType(typeName, scope);
            if (type == null)
            {
                Model.Reporter.Write(DiagnosticLevel.Error, $"Cant resolve type '{typeName}' for '{Name}'", SourceLocation);
                throw new InvalidDataException();
            }
            AddSymbol(Name, isLocal, type, SourceLocation);
        }

        void AddSymbol(string Name, bool isLocal, TypeInfo Type, SourceLocation SourceLocation)
        {
            var symbol = new VaraibleSymbol(this, isLocal, Name, Type, SourceLocation);
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName))
                {
                    Reporter.Write(DiagnosticLevel.Error, $"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                    throw new InvalidDataException();
                }
                Model.Symbols.Add(symbol);
            }
            Symbols.Add(symbol);
        }

        void AddFunctionSymbol(string name, bool isLocal, TypeInfo returnType, Dictionary<string, TypeInfo> parameters, SourceLocation sourceLocation)
        {
            var symbol = new FunctionSymbol(this, isLocal, name, sourceLocation, returnType, parameters);
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName))
                {
                    Reporter.Write(DiagnosticLevel.Error, $"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                    throw new InvalidDataException();
                }
                Model.Symbols.Add(symbol);
            }
            Symbols.Add(symbol);
        }
    }

    public interface IClassContainer
    {
        Class AddClass(string name)
        {
            var class_ = new Class(Model, this, name);

            return AddClass(class_);
        }

        Class AddClass(Syntax.ClassDefinition syntaxNode)
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
            Model.CreateType(class_.Name, FullName, []);

            return class_;
        }

        ErrorReporter Reporter => Model.Reporter;
        SemanticModel Model { get; }
        List<Class> Classes { get; }
        string FullName { get; }
    }

    public class Namespace : SemanticNode, IClassContainer, ISymbolContainer
    {
        public string Name { get; }
        public string FullName => Name;
        public List<Syntax.Declaration> Declarations { get; } = [];
        public List<InitialRoutine> InitialRoutines { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<ISymbol> Symbols { get; } = [];

        public Namespace(SemanticModel model, string name) : base(model)
        {
            Name = name;
        }

        public Namespace(SemanticModel model, Syntax.Namespace syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            foreach (var classNode in syntaxNode.Classes)
                (this as IClassContainer).AddClass(classNode);
        }

        public void ResolveSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.Namespace syntax)
            {
                foreach (var decl in syntax.Declarations)
                {
                    (this as ISymbolContainer).AddSymbol(decl.Name, false, decl.TypeSpecifier.Name, decl.SourceLocation, decl.Scope);
                }

                foreach (var func in syntax.Functions)
                {
                    var returnType = Model.ResolveType(func.ReturnType.Name, func.ReturnType.Scope);
                    if (returnType == null)
                    {
                        Model.Reporter.Write(DiagnosticLevel.Error, $"Cant resolve return type '{func.ReturnType.Name}' for function '{func.Name}'", func.SourceLocation);
                        throw new InvalidOperationException($"Cant resolve return type '{func.ReturnType.Name}' for function '{func.Name}'");
                    }
                    var parameters = new Dictionary<string, TypeInfo>();
                    foreach (var param in func.Parameters)
                    {
                        if (parameters.ContainsKey(param.Name))
                        {
                            Model.Reporter.Write(DiagnosticLevel.Error, $"Duplicate parameter name '{param.Name}' for function '{func.Name}'", param.SourceLocation);
                            throw new InvalidOperationException($"Duplicate parameter name '{param.Name}' for function '{func.Name}'");
                        }
                        else
                        {
                            var paramType = Model.ResolveType(param.TypeSpecifier.Name, param.TypeSpecifier.Scope);
                            if (paramType == null)
                            {
                                Model.Reporter.Write(DiagnosticLevel.Error, $"Cant resolve parameter type '{param.TypeSpecifier.Name}' for param '{param.Name}'", param.SourceLocation);
                                throw new InvalidOperationException($"Cant resolve parameter type '{param.TypeSpecifier.Name}' for param '{param.Name}'");
                            }
                            else
                            {
                                parameters.Add(param.Name, paramType);
                            }
                        }
                    }
                    (this as ISymbolContainer).AddFunctionSymbol(func.Name, false, returnType, parameters, func.SourceLocation);
                }
            }
        }
    }

    public class Class : SemanticNode
    {
        public Class(SemanticModel model, IClassContainer ns, Syntax.ClassDefinition syntaxNode) : base(model, syntaxNode)
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

}