using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;
using BabyPenguin;
using System.Reflection.Metadata.Ecma335;

namespace BabyPenguin.Semantic
{
    /// <summary>
    /// Represents a type in the semantic model.
    /// </summary>
    public class TypeInfo : IEquatable<TypeInfo>
    {
        /// <summary>
        /// Full name of the type, including namespace and generic arguments, e.g. MyNamespace.MyType<int,bool>
        /// </summary>
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

        public static readonly Dictionary<string, TypeInfo> BuiltinTypes = new Dictionary<string, TypeInfo> {
            { "bool", new TypeInfo( "bool", "", []) },
            { "double", new TypeInfo( "double", "", []) },
            {"float", new TypeInfo( "float", "", [])},
            {"string", new TypeInfo( "string", "", [])},
            {"void", new TypeInfo( "void", "", [])},
            {"u8", new TypeInfo( "u8", "", [])},
            {"u16", new TypeInfo( "u16", "", [])},
            {"u32", new TypeInfo( "u32", "", [])},
            {"u64", new TypeInfo( "u64", "", [])},
            {"i8", new TypeInfo( "i8", "", [])},
            {"i16", new TypeInfo( "i16", "", [])},
            {"i32", new TypeInfo( "i32", "", [])},
            {"i64", new TypeInfo( "i64", "", [])},
            {"char", new TypeInfo( "char","", [])},
        };

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

        bool IEquatable<TypeInfo>.Equals(TypeInfo? other)
        {
            if (other == null) return false;

            return other.FullName == FullName;
        }

        public override string ToString() => FullName;

        public bool IsStringType => Type == TypeEnum.String;
        public bool IsSignedIntType => Type == TypeEnum.I8 || Type == TypeEnum.I16 || Type == TypeEnum.I32 || Type == TypeEnum.I64;
        public bool IsUnsignedIntType => Type == TypeEnum.I8 || Type == TypeEnum.I16 || Type == TypeEnum.I32 || Type == TypeEnum.I64;
        public bool IsIntType => IsSignedIntType || IsUnsignedIntType;
        public bool IsFloatType => Type == TypeEnum.Float || Type == TypeEnum.Double;
        public bool IsNumericType => IsIntType || IsFloatType;
        public bool IsBoolType => Type == TypeEnum.Bool;
        public bool IsFunctionType => Type == TypeEnum.Fun;
        public bool IsVoidType => Type == TypeEnum.Void;

        public static TypeInfo? ResolveLiteralType(string literal)
        {
            if (literal.StartsWith('"') && literal.EndsWith('"'))
            {
                return BuiltinTypes["string"];
            }
            else if (float.TryParse(literal, out var _))
            {
                return BuiltinTypes["float"];
            }
            else if (double.TryParse(literal, out var _))
            {
                return BuiltinTypes["double"];
            }
            else if (byte.TryParse(literal, out var _))
            {
                return BuiltinTypes["u8"];
            }
            else if (sbyte.TryParse(literal, out var _))
            {
                return BuiltinTypes["i8"];
            }
            else if (ushort.TryParse(literal, out var _))
            {
                return BuiltinTypes["u16"];
            }
            else if (short.TryParse(literal, out var _))
            {
                return BuiltinTypes["i16"];
            }
            else if (uint.TryParse(literal, out var _))
            {
                return BuiltinTypes["u32"];
            }
            else if (int.TryParse(literal, out var _))
            {
                return BuiltinTypes["i32"];
            }
            else if (ulong.TryParse(literal, out var _))
            {
                return BuiltinTypes["u64"];
            }
            else if (long.TryParse(literal, out var _))
            {
                return BuiltinTypes["i64"];
            }
            else if (literal == "true" || literal == "false")
            {
                return BuiltinTypes["bool"];
            }
            else if (literal.StartsWith('\'') && literal.EndsWith('\''))
            {
                return BuiltinTypes["char"];
            }
            else
            {
                return null;
            }
        }
    }

    public interface ISymbol
    {
        string FullName => Parent.FullName + "." + Name;

        string Name { get; }

        /// <summary>
        /// Original name of the symbol, before any renaming or aliasing.
        /// Symbol renaming may happen when a symbol is redecalred in a sub-scope.
        /// </summary>
        string OriginName { get; }

        /// <summary>
        /// Scope depth of the symbol, each '{}' block increases the depth by 1.
        /// </summary>
        uint ScopeDepth { get; }
        ISymbolContainer Parent { get; }
        TypeInfo Type { get; }
        SourceLocation SourceLocation { get; }
        bool IsLocal { get; }
    }

    public class VaraibleSymbol : ISymbol
    {
        public VaraibleSymbol(ISymbolContainer parent, bool isLocal, string name, TypeInfo type, SourceLocation sourceLocation, uint scopeDepth, string originName)
        {
            Parent = parent;
            Name = name;
            Type = type;
            SourceLocation = sourceLocation;
            IsLocal = isLocal;
            ScopeDepth = scopeDepth;
            OriginName = originName;
        }

        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; }
        public ISymbolContainer Parent { get; }
        public TypeInfo Type { get; }
        public SourceLocation SourceLocation { get; }
        public bool IsLocal { get; }
        public string OriginName { get; }
        public uint ScopeDepth { get; }

        public override string ToString()
        {
            return $"{Name} ({Type})";
        }
    }

    public class FunctionSymbol : ISymbol
    {
        public FunctionSymbol(ISymbolContainer parent, bool isLocal, string name, SourceLocation sourceLocation, TypeInfo returnType, Dictionary<string, TypeInfo> parameters, uint scopeDepth, string originName)
        {
            Parent = parent;
            Name = name;
            ReturnType = returnType;
            SourceLocation = sourceLocation;
            Parameters = parameters;
            IsLocal = isLocal;
            ScopeDepth = scopeDepth;
            OriginName = originName;

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
        public string OriginName { get; }
        public uint ScopeDepth { get; }

        public override string ToString()
        {
            return $"{Name} ({Type})";
        }
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

    public interface ISemanticScope : IPrettyPrint
    {
        SemanticModel Model { get; }
        ISemanticScope? Parent { get; }
        List<ISemanticScope> Children { get; }
        string Name { get; }
        string FullName => Parent == null ? Name : Parent.FullName + "." + Name;

        IEnumerable<string> IPrettyPrint.PrettyPrint(int indentLevel, string? prefix)
        {
            return new[] { new string(' ', indentLevel * 2) + (prefix ?? " ") + FullName + $" ({GetType()})" }
                .Concat(Children.SelectMany(c => c.PrettyPrint(indentLevel + 1, prefix)));
        }
    }

    public interface ISymbolContainer : ISemanticScope
    {
        List<ISymbol> Symbols { get; }

        ErrorReporter Reporter => Model.Reporter;

        void ElabSyntaxSymbols();

        static ulong counter = 0;

        ISymbol AllocTempSymbol(TypeInfo type, SourceLocation sourceLocation)
        {
            var name = $"__temp_{counter++}";
            return new VaraibleSymbol(this, true, name, type, sourceLocation, 0, name);
        }

        ISymbol? ResolveSymbol(string name, uint scopeDepth, bool isOriginName = true)
        {
            var symbol = Symbols.FirstOrDefault(s => (isOriginName ? s.OriginName : s.Name) == name && s.ScopeDepth <= scopeDepth);
            if (symbol == null && Parent is ISymbolContainer parentContainer)
            {
                return parentContainer.ResolveSymbol(name, scopeDepth);
            }
            return symbol;
        }

        ISymbol? ResolveFunctionSymbol(string name, uint scopeDepth, bool isOriginName = true)
        {
            var symbol = Symbols.FirstOrDefault(s => (isOriginName ? s.OriginName : s.Name) == name && s.ScopeDepth <= scopeDepth && s is FunctionSymbol);
            if (symbol == null && Parent is ISymbolContainer parentContainer)
            {
                return parentContainer.ResolveSymbol(name, scopeDepth);
            }
            return symbol;
        }

        ISymbol AddSymbol(string Name, bool isLocal, string typeName, SourceLocation sourceLocation, uint scopeDepth)
        {
            var type = Model.ResolveType(typeName);
            if (type == null)
            {
                Model.Reporter.Write(DiagnosticLevel.Error, $"Cant resolve type '{typeName}' for '{Name}'", sourceLocation);
                throw new InvalidDataException();
            }
            return AddSymbol(Name, isLocal, type, sourceLocation, scopeDepth);
        }

        ISymbol AddSymbol(string name, bool isLocal, TypeInfo type, SourceLocation sourceLocation, uint scopeDepth)
        {
            var originName = name;
            if (isLocal)
            {
                if (ResolveSymbol(name, scopeDepth) != null)
                {
                    int i = 0;
                    while (ResolveSymbol($"{name}_{i}", scopeDepth) != null)
                    {
                        i++;
                    }
                    name = $"{name}_{i}";
                }
            }

            var symbol = new VaraibleSymbol(this, isLocal, name, type, sourceLocation, scopeDepth, originName);
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
            return symbol;
        }

        ISymbol AddFunctionSymbol(string name, bool isLocal, TypeInfo returnType, Dictionary<string, TypeInfo> parameters, SourceLocation sourceLocation, uint scopeDepth)
        {
            var originName = name;
            if (isLocal)
            {
                if (ResolveSymbol(name, scopeDepth) != null)
                {
                    int i = 0;
                    while (ResolveSymbol($"{name}_{i}", scopeDepth) != null)
                    {
                        i++;
                    }
                    name = $"{name}_{i}";
                }
            }

            var symbol = new FunctionSymbol(this, isLocal, name, sourceLocation, returnType, parameters, scopeDepth, originName);
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
            return symbol;
        }
    }

    public interface ICompilable
    {
        void CompileSyntaxStatements();
    }

    public interface IRoutineContainer : ISemanticScope
    {
        List<InitialRoutine> InitialRoutines { get; }
        void AddInitialRoutine(Syntax.InitialRoutine syntaxNode)
        {
            InitialRoutines.Add(new InitialRoutine(Model, this, syntaxNode));
        }
    }

    public interface IClassContainer : ISemanticScope
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
        List<Class> Classes { get; }
    }

    public class Namespace : SemanticNode, IClassContainer, ISymbolContainer, IRoutineContainer
    {
        public string Name { get; }
        public string FullName => Name;
        public List<Syntax.Declaration> Declarations { get; } = [];
        public List<InitialRoutine> InitialRoutines { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<ISymbol> Symbols { get; } = [];
        public ISemanticScope? Parent => null;
        public List<ISemanticScope> Children { get; } = [];

        public Namespace(SemanticModel model, string name) : base(model)
        {
            Name = name;
        }

        public Namespace(SemanticModel model, Syntax.Namespace syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            foreach (var classNode in syntaxNode.Classes)
                (this as IClassContainer).AddClass(classNode);
            foreach (var initialRoutine in syntaxNode.InitialRoutines)
                (this as IRoutineContainer).AddInitialRoutine(initialRoutine);
        }

        public void ElabSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.Namespace syntax)
            {
                foreach (var decl in syntax.Declarations)
                {
                    (this as ISymbolContainer).AddSymbol(decl.Name, false, decl.TypeSpecifier.Name, decl.SourceLocation, decl.Scope.ScopeDepth);
                }

                foreach (var func in syntax.Functions)
                {
                    var returnType = Model.ResolveType(func.ReturnType.Name);
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
                            var paramType = Model.ResolveType(param.TypeSpecifier.Name);
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
                    (this as ISymbolContainer).AddFunctionSymbol(func.Name, false, returnType, parameters, func.SourceLocation, func.Scope.ScopeDepth);
                }
            }

            foreach (var cls in Classes)
            {
                cls.ElabSyntaxSymbols();
            }

            foreach (var initialRoutine in InitialRoutines)
            {
                initialRoutine.ElabSyntaxSymbols();
            }

            foreach (var func in Functions)
            {
                func.ElabSyntaxSymbols();
            }
        }

    }

    public class Class : SemanticNode, ISymbolContainer
    {
        public Class(SemanticModel model, IClassContainer ns, Syntax.ClassDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Parent = ns;
            Parent.Children.Add(this);
        }

        public Class(SemanticModel model, IClassContainer ns, string name) : base(model)
        {
            Name = name;
            Parent = ns;
            Parent.Children.Add(this);
        }

        public string Name { get; }

        public List<ISymbol> Symbols { get; } = [];

        public IClassContainer Parent { get; }

        public List<ISemanticScope> Children { get; } = [];

        ISemanticScope? ISemanticScope.Parent => Parent;

        public void ElabSyntaxSymbols()
        {
            // TODO: Implement class symbol resolving
        }
    }

    public class CodeContainer : SemanticNode, ICompilable, ISymbolContainer
    {
        public CodeContainer(SemanticModel model, ISemanticScope parent, Syntax.CodeBlock codeBlock) : base(model, codeBlock)
        {
            Parent = parent;
            Parent.Children.Add(this);
            Model.CompileTasks.Add(this);
        }

        public CodeContainer(SemanticModel model, ISemanticScope parent, Syntax.Statement codeBlock) : base(model, codeBlock)
        {
            Parent = parent;
            Parent.Children.Add(this);
            Model.CompileTasks.Add(this);
        }

        public CodeContainer(SemanticModel model, ISemanticScope parent) : base(model)
        {
            Parent = parent;
            Parent.Children.Add(this);
            Model.CompileTasks.Add(this);
        }

        public List<ISemanticCommand> Statements { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public ISemanticScope? Parent { get; }

        public List<ISemanticCommand> Commands { get; } = [];

        static ulong counter = 0;

        public string Name { get; } = $"CodeContainer_{counter++}";

        public List<ISemanticScope> Children { get; } = [];

        public void CompileSyntaxStatements()
        {
            if (SyntaxNode is Syntax.CodeBlock codeBlock)
            {
                foreach (var item in codeBlock.BlockItems)
                {
                    AddCodeBlockItem(item);
                }
            }
            else if (SyntaxNode is Syntax.Statement statement)
            {
                AddStatement(statement);
            }

            var msg = "\n\t" + string.Join("\n\t", Commands.Select(c => c.ToString()));
            Model.Reporter.Write(DiagnosticLevel.Debug, $"Compile Result For {(this as ISemanticScope).FullName}: {msg}");
        }


        public void AddCodeBlockItem(Syntax.CodeBlockItem item)
        {
            if (item.IsDeclaration)
            {
                AddDeclearation(item.Declaration!);
            }
            else
            {
                AddStatement(item.Statement!);
            }
        }

        public void AddDeclearation(Syntax.Declaration item)
        {
            var symbol = (this as ISymbolContainer).AddSymbol(item.Name, true, item.TypeSpecifier.Name, item.SourceLocation, item.Scope.ScopeDepth);
            if (item.InitializeExpression != null)
            {
                var temp = this.AddExpression(item.InitializeExpression);
                Commands.Add(new AssignmentCommand(temp, symbol));
            }
        }

        public void AddStatement(Syntax.Statement item)
        {
            switch (item.StatementType)
            {
                case Syntax.Statement.Type.AssignmentStatement:
                    var right_var = this.AddExpression(item.AssignmentStatement!.RightHandSide);
                    var target = (this as ISymbolContainer).ResolveSymbol(item.AssignmentStatement.LeftHandSide.Name, item.Scope.ScopeDepth);
                    if (target == null)
                    {
                        Model.Reporter.Throw($"Cant resolve symbol '{item.AssignmentStatement.LeftHandSide.Name}'", item.AssignmentStatement.LeftHandSide.SourceLocation);
                    }
                    else
                    {
                        if (item.AssignmentStatement.AssignmentOperator == AssignmentOperatorEnum.Assign)
                        {
                            AddCommand(new AssignmentCommand(right_var, target));
                        }
                        else
                        {
                            var op = item.AssignmentStatement.AssignmentOperator switch
                            {
                                AssignmentOperatorEnum.AddAssign => BinaryOperatorEnum.Add,
                                AssignmentOperatorEnum.SubtractAssign => BinaryOperatorEnum.Subtract,
                                AssignmentOperatorEnum.MultiplyAssign => BinaryOperatorEnum.Multiply,
                                AssignmentOperatorEnum.DivideAssign => BinaryOperatorEnum.Divide,
                                AssignmentOperatorEnum.ModuloAssign => BinaryOperatorEnum.Modulo,
                                AssignmentOperatorEnum.BitwiseAndAssign => BinaryOperatorEnum.BitwiseAnd,
                                AssignmentOperatorEnum.BitwiseOrAssign => BinaryOperatorEnum.BitwiseOr,
                                AssignmentOperatorEnum.BitwiseXorAssign => BinaryOperatorEnum.BitwiseXor,
                                AssignmentOperatorEnum.LeftShiftAssign => BinaryOperatorEnum.LeftShift,
                                AssignmentOperatorEnum.RightShiftAssign => BinaryOperatorEnum.RightShift,
                                _ => throw new NotImplementedException(),
                            };
                            var temp = (this as ISymbolContainer).AllocTempSymbol(target.Type, item.SourceLocation);
                            AddCommand(new BinaryOperationCommand(op, right_var, target, temp));
                            AddCommand(new AssignmentCommand(temp, target));
                            break;
                        }
                    }
                    break;
                case Syntax.Statement.Type.ExpressionStatement:
                    this.AddExpression(item.ExpressionStatement!.Expression);
                    break;
                case Syntax.Statement.Type.SubBlock:
                    foreach (var subItem in item.CodeBlock!.BlockItems)
                    {
                        AddCodeBlockItem(subItem);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public TypeInfo ResolveExpressionType(ISyntaxExpression expression)
        {
            TypeInfo check_types(List<TypeInfo> types)
            {
                foreach (var t in types.Skip(1))
                    if (t != types.First())
                        Model.Reporter.Throw($"Incompatible types in logical or expression, expected '{types.First()}' but got '{t}'", expression.SourceLocation);
                return types.First();
            }

            switch (expression)
            {
                case Syntax.Expression exp:
                    return ResolveExpressionType(exp.SubExpression);
                case Syntax.LogicalOrExpression exp:
                    var or_types = exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList();
                    check_types(or_types);
                    if (exp.SubExpressions.Count > 1)
                        return TypeInfo.BuiltinTypes["bool"];
                    else
                        return or_types.First();
                case Syntax.LogicalAndExpression exp:
                    var and_types = exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList();
                    check_types(and_types);
                    if (exp.SubExpressions.Count > 1)
                        return TypeInfo.BuiltinTypes["bool"];
                    else
                        return and_types.First();
                case Syntax.InclusiveOrExpression exp:
                    return check_types(exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList());
                case Syntax.ExclusiveOrExpression exp:
                    return check_types(exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList());
                case Syntax.AndExpression exp:
                    return check_types(exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList());
                case Syntax.EqualityExpression exp:
                    var equality_types = exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList();
                    check_types(equality_types);
                    if (exp.SubExpressions.Count > 1)
                        return TypeInfo.BuiltinTypes["bool"];
                    else
                        return equality_types.First();
                case Syntax.RelationalExpression exp:
                    var relational_types = exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList();
                    check_types(relational_types);
                    if (exp.SubExpressions.Count > 1)
                        return TypeInfo.BuiltinTypes["bool"];
                    else
                        return relational_types.First();
                case Syntax.ShiftExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var shift_type = ResolveExpressionType(exp.SubExpressions[0]);
                        if (!shift_type.IsIntType) Model.Reporter.Throw($"Shift expression requires integer type, but got '{shift_type}'", expression.SourceLocation);
                        foreach (var sub in exp.SubExpressions.Skip(1).Select(e => ResolveExpressionType(e)))
                            if (!sub.IsIntType) Model.Reporter.Throw($"Shift expression requires integer type, but got '{sub}'", expression.SourceLocation);
                        return shift_type;
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubExpressions[0]);
                    }
                case Syntax.AdditiveExpression exp:
                    return check_types(exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList());
                case Syntax.MultiplicativeExpression exp:
                    return check_types(exp.SubExpressions.Select(e => ResolveExpressionType(e)).ToList());
                case Syntax.CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        var t = Model.ResolveType(exp.CastTypeIdentifier!.Name);
                        if (t == null) Model.Reporter.Throw($"Cant resolve type '{exp.CastTypeIdentifier.Name}'", exp.CastTypeIdentifier.SourceLocation);
                        else return t;
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubUnaryExpression!);
                    }
                    break;
                case Syntax.UnaryExpression exp:
                    if (exp.HasUnaryOperator)
                    {
                        switch (exp.UnaryOperator)
                        {
                            case UnaryOperatorEnum.Deref:
                            case UnaryOperatorEnum.Ref:
                                throw new NotImplementedException();
                            case UnaryOperatorEnum.Minus:
                            case UnaryOperatorEnum.Plus:
                            case UnaryOperatorEnum.BitwiseNot:
                                return ResolveExpressionType(exp.SubExpression);
                            case UnaryOperatorEnum.LogicalNot:
                                return TypeInfo.BuiltinTypes["bool"];
                        }
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubExpression);
                    }
                    break;
                case Syntax.PostfixExpression exp:
                    switch (exp.PostfixExpressionType)
                    {
                        case Syntax.PostfixExpression.Type.FunctionCall:
                            return ResolveExpressionType(exp.SubFunctionCallExpression!);
                        case Syntax.PostfixExpression.Type.MemberAccess:
                            return ResolveExpressionType(exp.SubMemberAccessExpression!);
                        case Syntax.PostfixExpression.Type.Slicing:
                            return ResolveExpressionType(exp.SubSlicingExpression!);
                        case Syntax.PostfixExpression.Type.PrimaryExpression:
                            return ResolveExpressionType(exp.SubPrimaryExpression!);
                    }
                    break;
                case Syntax.FunctionCallExpression exp:
                    var func_type = ResolveExpressionType(exp.PrimaryExpression);
                    if (!func_type.IsFunctionType)
                        Model.Reporter.Throw($"Function call expects function symbol as primary expression, but got '{func_type}'", expression.SourceLocation);
                    return func_type.GenericArguments.First();
                case Syntax.MemberAccessExpression exp:
                    throw new NotImplementedException();
                case Syntax.SlicingExpression exp:
                    throw new NotImplementedException();
                case Syntax.PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case Syntax.PrimaryExpression.Type.Identifier:
                            var symbol = (this as ISymbolContainer).ResolveSymbol(exp.Identifier!.Name, exp.Scope.ScopeDepth);
                            if (symbol == null) Model.Reporter.Throw($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else return symbol.Type;
                            break;
                        case Syntax.PrimaryExpression.Type.Constant:
                            var t = TypeInfo.ResolveLiteralType(exp.Literal!);
                            if (t == null) Model.Reporter.Throw($"Cant resolve literal type '{exp.Literal}'", exp.SourceLocation);
                            else return t;
                            break;
                        case Syntax.PrimaryExpression.Type.StringLiteral:
                            return TypeInfo.BuiltinTypes["string"];
                        case Syntax.PrimaryExpression.Type.ParenthesizedExpression:
                            return ResolveExpressionType(exp.ParenthesizedExpression!);
                    }
                    break;
                default:
                    break;
            }
            Model.Reporter.Throw($"Unsupported expression type '{expression.GetType()}'", expression.SourceLocation);
            throw new NotImplementedException();
        }

        public ISymbol AddExpression(ISyntaxExpression expression, ISymbol? to = null)
        {
            to ??= (this as ISymbolContainer).AllocTempSymbol(ResolveExpressionType(expression), expression.SourceLocation);

            switch (expression)
            {
                case Syntax.Expression exp:
                    AddExpression(exp.SubExpression, to);
                    break;
                case Syntax.LogicalOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(BinaryOperatorEnum.LogicalOr, a, b, res));
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.LogicalAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(BinaryOperatorEnum.LogicalAnd, a, b, res));
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.InclusiveOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(BinaryOperatorEnum.BitwiseOr, a, b, res));
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.ExclusiveOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(BinaryOperatorEnum.BitwiseXor, a, b, res));
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.AndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(BinaryOperatorEnum.BitwiseAnd, a, b, res));
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.EqualityExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.RelationalExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.ShiftExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.AdditiveExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.MultiplicativeExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_vars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator();
                        var res_var = temp_vars.Aggregate((a, b) =>
                        {
                            var res = (this as ISymbolContainer).AllocTempSymbol(type, expression.SourceLocation);
                            AddCommand(new BinaryOperationCommand(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddCommand(new AssignmentCommand(res_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        var type = ResolveExpressionType(exp);
                        var temp_var = AddExpression(exp.SubCastExpression!);
                        AddCommand(new CastCommand(temp_var, type, to));
                    }
                    else
                    {
                        AddExpression(exp.SubUnaryExpression!, to);
                    }
                    break;
                case Syntax.UnaryExpression exp:
                    if (exp.HasUnaryOperator)
                    {
                        var temp_var = AddExpression(exp.SubExpression);
                        AddCommand(new UnaryOperationCommand((UnaryOperatorEnum)exp.UnaryOperator!, temp_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpression, to);
                    }
                    break;
                case Syntax.PostfixExpression exp:
                    switch (exp.PostfixExpressionType)
                    {
                        case Syntax.PostfixExpression.Type.FunctionCall:
                            AddExpression(exp.SubFunctionCallExpression!, to);
                            break;
                        case Syntax.PostfixExpression.Type.MemberAccess:
                            AddExpression(exp.SubMemberAccessExpression!, to);
                            break;
                        case Syntax.PostfixExpression.Type.Slicing:
                            AddExpression(exp.SubSlicingExpression!, to);
                            break;
                        case Syntax.PostfixExpression.Type.PrimaryExpression:
                            AddExpression(exp.SubPrimaryExpression!, to);
                            break;
                    }
                    break;
                case Syntax.FunctionCallExpression exp:
                    var param_vars = exp.ArgumentsExpression.Select(e => AddExpression(e)).ToList();
                    var func_var = AddExpression(exp.PrimaryExpression);
                    if (!func_var.Type.IsFunctionType)
                    {
                        Model.Reporter.Throw($"Function call expects function symbol as primary expression, but got '{func_var.Type}'", exp.SourceLocation);
                    }
                    AddCommand(new FunctionCallCommand(func_var, param_vars, to));
                    break;
                case Syntax.MemberAccessExpression exp:
                    throw new NotImplementedException();
                case Syntax.SlicingExpression exp:
                    throw new NotImplementedException();
                case Syntax.PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case Syntax.PrimaryExpression.Type.Identifier:
                            var symbol = (this as ISymbolContainer).ResolveSymbol(exp.Identifier!.Name, exp.Scope.ScopeDepth);
                            if (symbol == null) Model.Reporter.Throw($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else AddCommand(new AssignmentCommand(symbol, to));
                            break;
                        case Syntax.PrimaryExpression.Type.Constant:
                            var t = TypeInfo.ResolveLiteralType(exp.Literal!);
                            if (t == null) Model.Reporter.Throw($"Cant resolve Type '{exp.Literal}'", exp.SourceLocation);
                            else AddCommand(new AssignLiteralToSymbolCommand(to, t, exp.Literal!));
                            break;
                        case Syntax.PrimaryExpression.Type.StringLiteral:
                            AddCommand(new AssignLiteralToSymbolCommand(to, TypeInfo.BuiltinTypes["string"], exp.Literal!));
                            break;
                        default:
                            break;
                    }
                    break;
            }
            return to;
        }

        public void AddCommand(ISemanticCommand command)
        {
            Commands.Add(command);
        }

        public void ElabSyntaxSymbols()
        {
            // Do nothing
        }
    }

    public class InitialRoutine : SemanticNode, ISemanticScope, ISymbolContainer
    {
        public InitialRoutine(SemanticModel model, IRoutineContainer parent, Syntax.InitialRoutine syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Parent = parent;
            Parent.Children.Add(this);
            Code = new CodeContainer(model, this, syntaxNode.CodeBlock);
        }

        public string Name { get; }

        public CodeContainer Code { get; }

        public ISemanticScope? Parent { get; }

        public List<ISemanticScope> Children { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public void ElabSyntaxSymbols()
        {
            // Do nothing
        }
    }


    public class Function : SemanticNode, ISemanticScope, ISymbolContainer
    {
        public Function(SemanticModel model, IRoutineContainer parent, Syntax.FunctionDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Parent = parent;
            Parent.Children.Add(this);
            Code = new CodeContainer(model, this, syntaxNode.CodeBlock);
        }

        public CodeContainer Code { get; }

        public string Name { get; }

        public ISemanticScope? Parent { get; }

        public List<ISemanticScope> Children { get; } = [];

        public Dictionary<string, TypeInfo> parameters = [];

        public TypeInfo ReturnType { get; private set; } = TypeInfo.BuiltinTypes["void"];

        public List<ISymbol> Symbols { get; } = [];

        public void ElabSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.FunctionDefinition func)
            {
                foreach (var param in func.Parameters)
                {
                    var type = Model.ResolveType(param.TypeSpecifier.Name);
                    if (type == null)
                    {
                        Model.Reporter.Throw($"Cant resolve type '{param.TypeSpecifier.Name}'", param.SourceLocation);
                    }
                    else
                    {
                        this.parameters.Add(param.Identifier.Name, type);
                    }
                }

                var ret_type = Model.ResolveType(func.ReturnType.Name);
                if (ret_type == null)
                {
                    Model.Reporter.Throw($"Cant resolve return type '{func.ReturnType.Name}'", func.SourceLocation);
                }
                else
                {
                    ReturnType = ret_type;
                }
            }
        }
    }
}