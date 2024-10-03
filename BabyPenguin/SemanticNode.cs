using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;
using BabyPenguin;
using System.Reflection.Metadata.Ecma335;
using ConsoleTables;

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
                Type = TypeEnum.Class;
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

        static readonly Dictionary<TypeEnum, List<TypeEnum>> implicitlyCastOrders = new Dictionary<TypeEnum, List<TypeEnum>>{
            {
                TypeEnum.I8,
                [
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I16,
                [
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I32,
                [
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I64,
                [
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U8,
                [
                    TypeEnum.U16,
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U16,
                [
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U32,
                [
                    TypeEnum.U64,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U64,
                [
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Float,
                [
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Double,
                [
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Bool,
                [
                    TypeEnum.String
                ]
            },
        };

        public bool CanImplicitlyCastTo(TypeInfo other)
        {
            if (this == other) return true;
            return implicitlyCastOrders.ContainsKey(Type) && implicitlyCastOrders[Type].Contains(other.Type);
        }

        public static TypeInfo? ImplictlyCastResult(TypeInfo one, TypeInfo another)
        {
            if (one == another)
                return one;

            if (one.CanImplicitlyCastTo(another))
                return another;
            else if (another.CanImplicitlyCastTo(one))
                return one;
            else
                return null;
        }

        public override string ToString() => FullName;

        public bool IsStringType => Type == TypeEnum.String;
        public bool IsSignedIntType => Type == TypeEnum.I8 || Type == TypeEnum.I16 || Type == TypeEnum.I32 || Type == TypeEnum.I64;
        public bool IsUnsignedIntType => Type == TypeEnum.U8 || Type == TypeEnum.U16 || Type == TypeEnum.U32 || Type == TypeEnum.U64;
        public bool IsIntType => IsSignedIntType || IsUnsignedIntType;
        public bool IsFloatType => Type == TypeEnum.Float || Type == TypeEnum.Double;
        public bool IsNumericType => IsIntType || IsFloatType;
        public bool IsBoolType => Type == TypeEnum.Bool;
        public bool IsFunctionType => Type == TypeEnum.Fun;
        public bool IsVoidType => Type == TypeEnum.Void;
        public bool IsClassType => Type == TypeEnum.Class;

        public static TypeInfo? ResolveLiteralType(string literal)
        {
            if (literal.StartsWith('"') && literal.EndsWith('"'))
            {
                return BuiltinTypes["string"];
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
            else if (float.TryParse(literal, out var _))
            {
                return BuiltinTypes["float"];
            }
            else if (double.TryParse(literal, out var _))
            {
                return BuiltinTypes["double"];
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
        TypeInfo TypeInfo { get; }
        SourceLocation SourceLocation { get; }
        bool IsLocal { get; }
        bool IsTemp { get; }
        bool IsParameter { get; }
        int ParameterIndex { get; }
        bool IsReadonly { get; }
        bool IsClassMember { get; }
    }

    public class VaraibleSymbol(ISymbolContainer parent,
        bool isLocal,
        string name,
        TypeInfo type,
        SourceLocation sourceLocation,
        uint scopeDepth,
        string originName,
        bool isTemp,
        int? paramIndex,
        bool isReadonly,
        bool isClassMember) : ISymbol
    {
        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; } = name;
        public ISymbolContainer Parent { get; } = parent;
        public TypeInfo TypeInfo { get; } = type;
        public SourceLocation SourceLocation { get; } = sourceLocation;
        public bool IsLocal { get; } = isLocal;
        public string OriginName { get; } = originName;
        public uint ScopeDepth { get; } = scopeDepth;
        public bool IsTemp { get; } = isTemp;
        public bool IsParameter { get; } = paramIndex.HasValue && paramIndex >= 0;
        public int ParameterIndex { get; } = paramIndex ?? -1;
        public bool IsReadonly { get; set; } = isReadonly;
        public bool IsClassMember { get; } = isClassMember;

        public override string ToString()
        {
            return $"{Name}({TypeInfo})";
        }
    }

    public record FunctionParameter(string Name, TypeInfo Type, bool IsReadonly, int Index);

    public class FunctionSymbol : ISymbol
    {
        public FunctionSymbol(ISymbolContainer parent,
            Function func,
            bool isLocal,
            string name,
            SourceLocation sourceLocation,
            TypeInfo returnType,
            Dictionary<string, FunctionParameter> parameters,
            uint scopeDepth,
            string originName,
            bool isTemp,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember)
        {
            Parent = parent;
            Name = name;
            ReturnTypeInfo = returnType;
            SourceLocation = sourceLocation;
            Parameters = parameters;
            IsLocal = isLocal;
            ScopeDepth = scopeDepth;
            OriginName = originName;
            IsParameter = paramIndex.HasValue && paramIndex >= 0;
            ParameterIndex = paramIndex ?? -1;
            IsTemp = isTemp;
            IsReadonly = isReadonly;
            SemanticFunction = func;

            var funTypeGenericArguments = new[] { returnType }.Concat(parameters.Values.Select(p => p.Type)).ToList();
            TypeInfo = parent.Model.ResolveOrCreateType("fun", "", funTypeGenericArguments);
        }

        public string FullName => Parent.FullName + "." + Name;
        public string Name { get; }
        public ISymbolContainer Parent { get; }
        public TypeInfo ReturnTypeInfo { get; }
        public Dictionary<string, FunctionParameter> Parameters { get; }
        public SourceLocation SourceLocation { get; }
        public TypeInfo TypeInfo { get; }
        public bool IsLocal { get; }
        public string OriginName { get; }
        public uint ScopeDepth { get; }
        public bool IsTemp { get; }
        public bool IsParameter { get; }
        public int ParameterIndex { get; }
        public bool IsReadonly { get; set; }
        public Function SemanticFunction { get; }
        public bool IsClassMember { get; }

        public override string ToString()
        {
            return $"{Name}({TypeInfo})";
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
            SourceLocation = SourceLocation.Empty();
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
            var temp = new VaraibleSymbol(this, true, name, type, sourceLocation, 0, name, true, null, false, false);
            Symbols.Add(temp);
            return temp;
        }

        ISymbol? ResolveSymbol(string name, uint scopeDepth, bool isOriginName = true)
        {
            var symbol = Symbols.OrderByDescending(s => s.ScopeDepth).FirstOrDefault(s => (isOriginName ? s.OriginName : s.Name) == name && s.ScopeDepth <= scopeDepth);
            if (symbol == null)
            {
                if (Parent is ISymbolContainer parentContainer)
                {
                    return parentContainer.ResolveSymbol(name, scopeDepth);
                }
                else
                {
                    if (this != Model.BuiltinNamespace && Model.BuiltinNamespace != null)
                        return (Model.BuiltinNamespace as ISymbolContainer).ResolveSymbol(name, scopeDepth);
                }
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

        ISymbol AddSymbol(string Name,
            bool isLocal,
            string typeName,
            SourceLocation sourceLocation,
            uint scopeDepth,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember)
        {
            var type = Model.ResolveType(typeName, this);

            if (type == null)
            {
                Model.Reporter.Throw($"Cant resolve type '{typeName}' for '{Name}'", sourceLocation);
            }
            return AddSymbol(Name, isLocal, type!, sourceLocation, scopeDepth, paramIndex, isReadonly, isClassMember);
        }

        ISymbol AddSymbol(string name,
            bool isLocal,
            TypeInfo type,
            SourceLocation sourceLocation,
            uint scopeDepth,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember)
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

            var symbol = new VaraibleSymbol(this, isLocal, name, type, sourceLocation, scopeDepth, originName, false, paramIndex, isReadonly, isClassMember);
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName))
                {
                    Reporter.Throw($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                }
                Model.Symbols.Add(symbol);
            }
            Symbols.Add(symbol);
            return symbol;
        }

        ISymbol AddFunctionSymbol(Function func,
            bool isLocal,
            TypeInfo returnType,
            Dictionary<string, FunctionParameter> parameters,
            SourceLocation sourceLocation,
            uint scopeDepth,
            int? paramIndex,
            bool isReadonly,
            bool isClassMember)
        {
            var name = func.Name;
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

            var symbol = new FunctionSymbol(this, func, isLocal, name, sourceLocation, returnType, parameters, scopeDepth, originName, false, paramIndex, isReadonly, isClassMember);
            if (!isLocal)
            {
                if (Model.Symbols.Any(s => s.FullName == symbol.FullName))
                {
                    Reporter.Throw($"Symbol '{symbol.FullName}' already exists", symbol.SourceLocation);
                }
                Model.Symbols.Add(symbol);
            }
            Symbols.Add(symbol);
            return symbol;
        }
    }

    public interface IRoutineContainer : ISemanticScope, ISymbolContainer
    {
        List<InitialRoutine> InitialRoutines { get; }
        List<Function> Functions { get; }

        void AddInitialRoutine(Syntax.InitialRoutine syntaxNode)
        {
            var init = new InitialRoutine(Model, this, syntaxNode);
            AddInitialRoutine(init);
        }

        void AddInitialRoutine(InitialRoutine routine)
        {
            InitialRoutines.Add(routine);
        }

        void AddFunction(Syntax.FunctionDefinition func)
        {
            var funcInst = new Function(Model, this, func);
            AddFunction(funcInst);
        }

        void AddFunction(Function func)
        {
            Functions.Add(func);
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
                Reporter.Throw($"Class '{class_.Name}' already exists in '{FullName}'", class_.SourceLocation);
            }

            Classes.Add(class_);
            Model.Classes.Add(class_);
            Model.CreateType(class_.Name, FullName, []);

            return class_;
        }

        ErrorReporter Reporter => Model.Reporter;
        List<Class> Classes { get; }
    }

    public interface ICodeContainer : ISymbolContainer
    {
        List<SemanticInstruction> Instructions { get; }

        Syntax.SyntaxNode? CodeSyntaxNode { get; }

        TypeInfo ReturnTypeInfo { get; }

        void CompileSyntaxStatements()
        {
            if (CodeSyntaxNode is Syntax.CodeBlock codeBlock)
            {
                foreach (var item in codeBlock.BlockItems)
                {
                    AddCodeBlockItem(item);
                }
            }
            else if (CodeSyntaxNode is Syntax.Statement statement)
            {
                AddStatement(statement);
            }
            else if (CodeSyntaxNode is Syntax.Namespace)
            {
                foreach (var decl in (CodeSyntaxNode as Syntax.Namespace)!.Declarations)
                {
                    if (decl.InitializeExpression != null)
                    {
                        var symbol = ResolveSymbol(decl.Name, decl.Scope.ScopeDepth);
                        AddExpression(decl.InitializeExpression, symbol);
                    }
                }
            }

            if (Instructions.Count > 0)
                Model.Reporter.Write(DiagnosticLevel.Debug, $"Compile Result For {FullName}: \n" + PrintInstructionsTable());
        }

        public string PrintInstructionsTable()
        {
            var table = new ConsoleTable("Instruction", "OP1", "OP2", "Result", "Labels");
            foreach (var ins in Instructions)
            {
                table.AddRow(ins.StringCommand, ins.StringOP1, ins.StringOP2, ins.StringResult, ins.StringLabels);
            }
            return table.ToMarkDownString();
        }

        public void AddCodeBlockItem(Syntax.CodeBlockItem item)
        {
            if (item.IsDeclaration)
            {
                AddLocalDeclearation(item.Declaration!, null);
            }
            else
            {
                AddStatement(item.Statement!);
            }
        }

        public void AddLocalDeclearation(Syntax.Declaration item, int? paramIndex)
        {
            var typeName = item.TypeSpecifier!.Name; // TODO: type inference
            var symbol = AddSymbol(item.Name, true, typeName, item.SourceLocation, item.Scope.ScopeDepth, paramIndex, item.IsReadonly, false);
            if (item.InitializeExpression != null)
            {
                AddExpression(item.InitializeExpression, symbol);
            }
        }

        public string CreateLabel() => $"{Name}_{counter++}";

        public void AddStatement(Syntax.Statement item)
        {
            switch (item.StatementType)
            {
                case Syntax.Statement.Type.AssignmentStatement:
                    {
                        var rightVar = AddExpression(item.AssignmentStatement!.RightHandSide);
                        ISymbol? member = null;
                        ISymbol? target;
                        bool isMemberAccess = item.AssignmentStatement.LeftHandSide.IsMemberAccess;
                        if (!isMemberAccess)
                        {
                            target = ResolveSymbol(item.AssignmentStatement.LeftHandSide.Identifier!.Name, item.Scope.ScopeDepth);
                        }
                        else
                        {
                            var ma = item.AssignmentStatement.LeftHandSide.MemberAccess!;
                            if (ma.PrimaryExpression.IsSimple)
                            {
                                target = ResolveSymbol(ma.PrimaryExpression.Text, item.Scope.ScopeDepth);
                                if (target == null)
                                {
                                    Model.Reporter.Throw($"Cant resolve symbol '{ma.PrimaryExpression.Text}'", ma.PrimaryExpression.SourceLocation);
                                }
                            }
                            else
                            {
                                target = AddExpression(ma.PrimaryExpression);
                            }
                            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                            {
                                if (i == ma.MemberIdentifiers.Count - 1)
                                {
                                    member = Model.ResolveSymbol(target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name);
                                }
                                else
                                {
                                    member = Model.ResolveSymbol(target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name);
                                    if (member == null)
                                        Model.Reporter.Throw($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                                    var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                                    AddInstruction(new ReadMemberInstruction(member, target, temp));
                                    target = temp;
                                }
                            }
                        }

                        if (target == null)
                        {
                            Model.Reporter.Throw($"Cant resolve symbol '{item.AssignmentStatement.LeftHandSide}'", item.AssignmentStatement.LeftHandSide.SourceLocation);
                        }
                        else
                        {
                            if (item.AssignmentStatement.AssignmentOperator == AssignmentOperatorEnum.Assign)
                            {
                                if (!isMemberAccess)
                                    AddInstruction(new AssignmentInstruction(rightVar, target));
                                else
                                    AddInstruction(new WriteMemberInstruction(member!, rightVar, target));
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
                                if (!isMemberAccess)
                                {
                                    var temp = AllocTempSymbol(target.TypeInfo, item.SourceLocation);
                                    AddInstruction(new BinaryOperationInstruction(op, target, rightVar, temp));
                                    AddInstruction(new AssignmentInstruction(temp, target));
                                }
                                else
                                {
                                    var tempBeforeCalc = AddExpression(item.AssignmentStatement.LeftHandSide.MemberAccess!);
                                    var tempAfterCalc = AllocTempSymbol(ResolveBinaryOperationType(op, [tempBeforeCalc.TypeInfo, rightVar.TypeInfo], item.SourceLocation), item.SourceLocation);
                                    AddInstruction(new BinaryOperationInstruction(op, tempBeforeCalc, rightVar, tempAfterCalc));
                                    AddInstruction(new WriteMemberInstruction(member!, tempAfterCalc, target));
                                }
                                break;
                            }
                        }
                        break;
                    }
                case Syntax.Statement.Type.ExpressionStatement:
                    {
                        AddExpression(item.ExpressionStatement!.Expression);
                        break;
                    }
                case Syntax.Statement.Type.SubBlock:
                    {
                        foreach (var subItem in item.CodeBlock!.BlockItems)
                        {
                            AddCodeBlockItem(subItem);
                        }
                        break;
                    }
                case Syntax.Statement.Type.IfStatement:
                    {
                        var ifStatement = item.IfStatement!;
                        var conditionVar = AddExpression(ifStatement.Condition);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            Reporter.Throw($"If condition must be bool type, but got '{conditionVar.TypeInfo}'", ifStatement.SourceLocation);
                        if (ifStatement.HasElse)
                        {
                            var elseLabel = CreateLabel();
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(elseLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement);
                            AddInstruction(new GotoInstruction(endifLabel));
                            AddInstruction(new NopInstuction().WithLabel(elseLabel));
                            AddStatement(ifStatement.ElseStatement!);
                            AddInstruction(new NopInstuction().WithLabel(endifLabel));
                        }
                        else
                        {
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(endifLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement);
                            AddInstruction(new NopInstuction().WithLabel(endifLabel));
                        }
                        break;
                    }
                case Syntax.Statement.Type.WhileStatement:
                    {
                        var whileStatement = item.WhileStatement!;
                        var beginLabel = CreateLabel();
                        AddInstruction(new NopInstuction().WithLabel(beginLabel));
                        var conditionVar = AddExpression(whileStatement.Condition);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            Reporter.Throw($"While condition must be bool type, but got '{conditionVar.TypeInfo}'", whileStatement.SourceLocation);
                        var endLabel = CreateLabel();
                        AddInstruction(new GotoInstruction(endLabel, conditionVar, false));
                        AddStatement(whileStatement.BodyStatement);
                        AddInstruction(new GotoInstruction(beginLabel));
                        AddInstruction(new NopInstuction().WithLabel(endLabel));
                        break;
                    }
                case Syntax.Statement.Type.ForStatement:
                    throw new NotImplementedException();
                case Syntax.Statement.Type.ReturnStatement:
                    {
                        var returnStatement = item.ReturnStatement!;
                        if (returnStatement.ReturnExpression != null)
                        {
                            var returnVar = AddExpression(returnStatement.ReturnExpression);
                            AddInstruction(new ReturnInstruction(returnVar));
                        }
                        else
                        {
                            AddInstruction(new ReturnInstruction(null));
                        }
                        break;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public TypeInfo ResolveBinaryOperationType(BinaryOperatorEnum op, IEnumerable<TypeInfo> opType, SourceLocation sourceLocation)
        {
            var types = opType.ToList();
            if (types.Count == 0)
            {
                throw new NotImplementedException();
            }
            TypeInfo checkTypes(List<TypeInfo> types, bool allowImplicitConversion = true)
            {
                if (types.Find(i => i.IsClassType) != null)
                {
                    Model.Reporter.Throw($"Type {types.Find(i => i.IsClassType)} can't be used here", sourceLocation);
                }

                if (!allowImplicitConversion)
                {
                    foreach (var t in types.Skip(1))
                        if (t != types.First())
                            Model.Reporter.Throw($"Incompatible types in expression, expected '{types.First()}' but got '{t}'", sourceLocation);
                }
                else
                {
                    return types.Aggregate((a, b) =>
                    {
                        if (a.CanImplicitlyCastTo(b)) return b;
                        else if (b.CanImplicitlyCastTo(a)) return a;
                        else
                        {
                            Model.Reporter.Throw($"Incompatible types in expression, expected '{a}' but got '{b}'", sourceLocation); return a;
                        }
                    });
                }
                return types.First();
            }

            switch (op)
            {
                case BinaryOperatorEnum.Add:
                case BinaryOperatorEnum.Subtract:
                case BinaryOperatorEnum.Multiply:
                case BinaryOperatorEnum.Divide:
                case BinaryOperatorEnum.Modulo:
                case BinaryOperatorEnum.BitwiseAnd:
                case BinaryOperatorEnum.BitwiseOr:
                case BinaryOperatorEnum.BitwiseXor:
                    if (types.Count > 1)
                        return checkTypes(types, true);
                    else
                        return types.First();
                case BinaryOperatorEnum.LessThan:
                case BinaryOperatorEnum.GreaterThan:
                case BinaryOperatorEnum.LessThanOrEqual:
                case BinaryOperatorEnum.GreaterThanOrEqual:
                case BinaryOperatorEnum.Equal:
                case BinaryOperatorEnum.NotEqual:
                    if (types.Count > 1)
                    {
                        checkTypes(types, true);
                        return TypeInfo.BuiltinTypes["bool"];
                    }
                    else
                        return types.First();
                case BinaryOperatorEnum.LogicalAnd:
                case BinaryOperatorEnum.LogicalOr:
                    if (types.Count > 1)
                    {
                        checkTypes(types, false);
                        if (types.All(t => t.IsBoolType))
                        {
                            return TypeInfo.BuiltinTypes["bool"];
                        }
                        else
                        {
                            Model.Reporter.Throw($"Logical operators can only be used with bool types, but got '{types.First()}'", sourceLocation);
                        }
                    }
                    else
                        return types.First();
                    break;
                case BinaryOperatorEnum.LeftShift:
                case BinaryOperatorEnum.RightShift:
                    if (types.Count > 1)
                    {
                        var shiftType = types[0];
                        if (!shiftType.IsIntType) Model.Reporter.Throw($"Shift expression requires integer type, but got '{shiftType}'", sourceLocation);
                        foreach (var sub in types.Skip(1))
                            if (!sub.IsIntType) Model.Reporter.Throw($"Shift expression requires integer type, but got '{sub}'", sourceLocation);
                        return shiftType;
                    }
                    else
                    {
                        return types.First();
                    }
            }
            throw new NotImplementedException();
        }

        public TypeInfo ResolveMemberAccessExpressionOwnerType(Syntax.MemberAccessExpression expression)
        {
            var t = ResolveExpressionType(expression.PrimaryExpression);
            if (t == null)
                Model.Reporter.Throw($"Cant resolve owner type of member access expression", expression.SourceLocation);
            var ma = expression;
            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
            {
                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                var member = Model.ResolveSymbol(t.FullName + "." + ma.MemberIdentifiers[i].Name);
                if (member == null)
                    Model.Reporter.Throw($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                if (isLastRound) return t;
                t = member.TypeInfo;
            }
            throw new NotImplementedException();
        }

        public TypeInfo ResolveExpressionType(ISyntaxExpression expression)
        {

            switch (expression)
            {
                case Syntax.Expression exp:
                    return ResolveExpressionType(exp.SubExpression);
                case Syntax.LogicalOrExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LogicalOr, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.LogicalAndExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LogicalAnd, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.BitWiseOrExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseOr, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.BitwiseXorExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseXor, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.BitwiseAndExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseAnd, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.EqualityExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.Equal, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.RelationalExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.GreaterThan, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.ShiftExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LeftShift, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.AdditiveExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.Add, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.MultiplicativeExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.Multiply, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case Syntax.CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        var t = Model.ResolveType(exp.CastTypeSpecifier!.Name);
                        if (t == null) Model.Reporter.Throw($"Cant resolve type '{exp.CastTypeSpecifier.Name}'", exp.CastTypeSpecifier.SourceLocation);
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
                                var type = ResolveExpressionType(exp.SubExpression);
                                return type.Type switch
                                {
                                    TypeEnum.U8 => TypeInfo.BuiltinTypes["i16"],
                                    TypeEnum.U16 => TypeInfo.BuiltinTypes["i32"],
                                    TypeEnum.U32 => TypeInfo.BuiltinTypes["i64"],
                                    _ => type
                                };
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
                        // case Syntax.PostfixExpression.Type.Slicing:
                        //     return ResolveExpressionType(exp.SubSlicingExpression!);
                        case Syntax.PostfixExpression.Type.PrimaryExpression:
                            return ResolveExpressionType(exp.SubPrimaryExpression!);
                        case Syntax.PostfixExpression.Type.New:
                            {
                                var res = Model.ResolveType(exp.SubNewExpression!.TypeSpecifier.Name, this);
                                if (res == null)
                                {
                                    Model.Reporter.Throw($"Cant resolve type '{exp.SubNewExpression!.TypeSpecifier.Name}'", exp.SubNewExpression!.TypeSpecifier.SourceLocation);
                                }
                                return res;
                            }
                    }
                    break;
                case Syntax.FunctionCallExpression exp:
                    {
                        var funcType = ResolveExpressionType(exp.IsMemberAccess ? exp.MemberAccessExpression! : exp.PrimaryExpression!);
                        if (!funcType.IsFunctionType)
                            Model.Reporter.Throw($"Function call expects function symbol, but got '{funcType}'", expression.SourceLocation);
                        var actualParamsCount = exp.IsMemberAccess ? exp.ArgumentsExpression.Count + 1 : exp.ArgumentsExpression.Count;
                        if (funcType.GenericArguments.Count - 1 != actualParamsCount)
                            Model.Reporter.Throw($"Function expects {funcType.GenericArguments.Count - 1} params, but got '{actualParamsCount}'", expression.SourceLocation);
                        for (int i = 0; i < funcType.GenericArguments.Count - 1; i++)
                        {
                            var expectedType = funcType.GenericArguments[i + 1];
                            TypeInfo actualType;
                            if (i == 0 && exp.IsMemberAccess)
                            {
                                actualType = ResolveMemberAccessExpressionOwnerType(exp.MemberAccessExpression!);
                            }
                            else
                            {
                                actualType = ResolveExpressionType(exp.ArgumentsExpression[exp.IsMemberAccess ? i - 1 : i]);
                            }
                            if (!actualType.CanImplicitlyCastTo(expectedType))
                                Model.Reporter.Throw($"Function expects {expectedType} param, but got '{actualType}'", exp.ArgumentsExpression[i].SourceLocation);
                        }
                        return funcType.GenericArguments.First();
                    }
                case Syntax.MemberAccessExpression exp:
                    {
                        var t = ResolveExpressionType(exp.PrimaryExpression);
                        if (t == null)
                            Model.Reporter.Throw($"Cant resolve owner type of member access expression", exp.SourceLocation);
                        var ma = exp;
                        for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                        {
                            var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                            var member = Model.ResolveSymbol(t.FullName + "." + ma.MemberIdentifiers[i].Name);
                            if (member == null)
                                Model.Reporter.Throw($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                            t = member.TypeInfo;
                        }
                        return t;
                    }
                // case Syntax.SlicingExpression exp:
                //     throw new NotImplementedException();
                case Syntax.PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case Syntax.PrimaryExpression.Type.Identifier:
                            var symbol = (this as ISymbolContainer).ResolveSymbol(exp.Identifier!.Name, exp.Scope.ScopeDepth);
                            if (symbol == null) Model.Reporter.Throw($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else return symbol.TypeInfo;
                            break;
                        case Syntax.PrimaryExpression.Type.Constant:
                            var t = TypeInfo.ResolveLiteralType(exp.Literal!);
                            if (t == null) Model.Reporter.Throw($"Cant resolve literal type '{exp.Literal}'", exp.SourceLocation);
                            else return t;
                            break;
                        case Syntax.PrimaryExpression.Type.StringLiteral:
                            return TypeInfo.BuiltinTypes["string"];
                        case Syntax.PrimaryExpression.Type.BoolLiteral:
                            return TypeInfo.BuiltinTypes["bool"];
                        case Syntax.PrimaryExpression.Type.ParenthesizedExpression:
                            return ResolveExpressionType(exp.ParenthesizedExpression!);
                    }
                    break;
                case Syntax.NewExpression exp:
                    {
                        var res = Model.ResolveType(exp.TypeSpecifier.Name, this);
                        if (res == null)
                            Model.Reporter.Throw($"Cant resolve type '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);
                        return res;
                    }
                default:
                    break;
            }
            Model.Reporter.Throw($"Unsupported expression type '{expression.GetType()}'", expression.SourceLocation);
            throw new NotImplementedException();
        }

        public ISymbol AddMemberAccessExpression(Syntax.MemberAccessExpression exp, ISymbol to, out ISymbol owner)
        {
            ISymbol owner_var;
            if (exp.PrimaryExpression.IsSimple)
            {
                var temp = ResolveSymbol(exp.PrimaryExpression.Text, exp.Scope.ScopeDepth);
                if (temp == null)
                    Model.Reporter.Throw($"Cant resolve symbol '{exp.PrimaryExpression.Text}'", exp.PrimaryExpression.SourceLocation);
                owner_var = temp;
            }
            else
            {
                owner_var = AddExpression(exp.PrimaryExpression);
            }

            var ma = exp;
            ISymbol target = owner_var;
            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
            {
                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                var member = Model.ResolveSymbol(target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name);
                if (member == null)
                    Model.Reporter.Throw($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                if (isLastRound)
                {
                    AddInstruction(new ReadMemberInstruction(member, target, to));
                }
                else
                {
                    var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                    AddInstruction(new ReadMemberInstruction(member, target, temp));
                    target = temp;
                }
            }
            owner = target;
            return to;
        }

        public ISymbol AddExpression(ISyntaxExpression expression, ISymbol? to = null)
        {
            if (to != null)
            {
                var rightType = ResolveExpressionType(expression);
                if (!rightType.CanImplicitlyCastTo(to.TypeInfo))
                {
                    Model.Reporter.Throw($"Cant assign type '{rightType}' to type '{to.TypeInfo}'", expression.SourceLocation);
                }
            }
            else
            {
                to = AllocTempSymbol(ResolveExpressionType(expression), expression.SourceLocation);
            }

            switch (expression)
            {
                case Syntax.Expression exp:
                    {
                        AddExpression(exp.SubExpression, to);
                    }
                    break;
                case Syntax.LogicalOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.LogicalOr, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.LogicalAnd, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.BitWiseOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseOr, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.BitwiseXorExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseXor, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case Syntax.BitwiseAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseAnd, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
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
                        var temp_var = AddExpression(exp.SubUnaryExpression!);
                        AddInstruction(new CastInstruction(temp_var, type, to));
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
                        AddInstruction(new UnaryOperationInstruction((UnaryOperatorEnum)exp.UnaryOperator!, temp_var, to));
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
                        // case Syntax.PostfixExpression.Type.Slicing:
                        //     AddExpression(exp.SubSlicingExpression!, to);
                        //     break;
                        case Syntax.PostfixExpression.Type.PrimaryExpression:
                            AddExpression(exp.SubPrimaryExpression!, to);
                            break;
                        case Syntax.PostfixExpression.Type.New:
                            AddExpression(exp.SubNewExpression!, to);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case Syntax.FunctionCallExpression exp:
                    {
                        if (exp.IsMemberAccess)
                        {
                            var temp = AllocTempSymbol(ResolveExpressionType(exp.MemberAccessExpression!), expression.SourceLocation);
                            var func_var = AddMemberAccessExpression(exp.MemberAccessExpression!, temp, out ISymbol owner_var);
                            if (!func_var.TypeInfo.IsFunctionType)
                                Model.Reporter.Throw($"Function call expects function symbol, but got '{func_var.TypeInfo}'", exp.MemberAccessExpression!.SourceLocation);
                            var param_vars = new List<ISymbol> { owner_var };
                            param_vars.AddRange(exp.ArgumentsExpression.Select(e => AddExpression(e)));
                            AddInstruction(new FunctionCallInstruction(func_var, param_vars, to));
                        }
                        else
                        {
                            var param_vars = exp.ArgumentsExpression.Select(e => AddExpression(e)).ToList();
                            var func_var = AddExpression(exp.PrimaryExpression!);
                            if (!func_var.TypeInfo.IsFunctionType)
                                Model.Reporter.Throw($"Function call expects function symbol, but got '{func_var.TypeInfo}'", exp.PrimaryExpression!.SourceLocation);
                            AddInstruction(new FunctionCallInstruction(func_var, param_vars, to));
                        }
                    }
                    break;
                case Syntax.MemberAccessExpression exp:
                    {
                        AddMemberAccessExpression(exp, to, out ISymbol owner_var);
                    }
                    break;
                case Syntax.NewExpression exp:
                    {
                        AddInstruction(new NewInstanceInstruction(to));
                    }
                    break;
                // case Syntax.SlicingExpression exp:
                //     throw new NotImplementedException();
                case Syntax.PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case Syntax.PrimaryExpression.Type.Identifier:
                            var symbol = ResolveSymbol(exp.Identifier!.Name, exp.Scope.ScopeDepth);
                            if (symbol == null) Model.Reporter.Throw($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else AddInstruction(new AssignmentInstruction(symbol, to));
                            break;
                        case Syntax.PrimaryExpression.Type.Constant:
                            var t = TypeInfo.ResolveLiteralType(exp.Literal!);
                            if (t == null) Model.Reporter.Throw($"Cant resolve Type '{exp.Literal}'", exp.SourceLocation);
                            else AddInstruction(new AssignLiteralToSymbolInstruction(to, t, exp.Literal!));
                            break;
                        case Syntax.PrimaryExpression.Type.StringLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(to, TypeInfo.BuiltinTypes["string"], exp.Literal!));
                            break;
                        case Syntax.PrimaryExpression.Type.BoolLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(to, TypeInfo.BuiltinTypes["bool"], exp.Literal!));
                            break;
                        case Syntax.PrimaryExpression.Type.ParenthesizedExpression:
                            AddExpression(exp.ParenthesizedExpression!, to);
                            break;
                        default:
                            break;
                    }
                    break;
            }
            return to;
        }

        public void AddInstruction(SemanticInstruction instruction)
        {
            Instructions.Add(instruction);
        }
    }

    public class Namespace : SemanticNode, IClassContainer, ISymbolContainer, IRoutineContainer, ICodeContainer
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
        public List<SemanticInstruction> Instructions { get; } = [];
        public Syntax.SyntaxNode? CodeSyntaxNode { get; }
        public TypeInfo ReturnTypeInfo => TypeInfo.BuiltinTypes["void"];

        public Namespace(SemanticModel model, string name) : base(model)
        {
            Name = name;
            Model.CompileTasks.Add(this);
        }

        public Namespace(SemanticModel model, Syntax.Namespace syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            CodeSyntaxNode = syntaxNode;
            foreach (var classNode in syntaxNode.Classes)
                (this as IClassContainer).AddClass(classNode);
            foreach (var initialRoutine in syntaxNode.InitialRoutines)
                (this as IRoutineContainer).AddInitialRoutine(initialRoutine);
            foreach (var func in syntaxNode.Functions)
                (this as IRoutineContainer).AddFunction(func);
            Model.CompileTasks.Add(this);
        }

        public void ElabSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.Namespace syntax)
            {
                foreach (var decl in syntax.Declarations)
                {
                    var typeName = decl.TypeSpecifier!.Name; // TODO: type inference
                    (this as ISymbolContainer).AddSymbol(decl.Name, false, typeName, decl.SourceLocation, decl.Scope.ScopeDepth, null, decl.IsReadonly, false);
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

    public class InitialRoutine : SemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer
    {
        public InitialRoutine(SemanticModel model, IRoutineContainer parent, Syntax.InitialRoutine syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Parent = parent;
            Parent.Children.Add(this);
            Model.CompileTasks.Add(this);
            CodeSyntaxNode = syntaxNode.CodeBlock;
        }

        public string Name { get; }

        public ISemanticScope? Parent { get; }

        public List<ISemanticScope> Children { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public List<SemanticInstruction> Instructions { get; } = [];

        public Syntax.SyntaxNode? CodeSyntaxNode { get; }

        public TypeInfo ReturnTypeInfo { get; } = TypeInfo.BuiltinTypes["void"];

        public void ElabSyntaxSymbols()
        {
            // Do nothing
        }
    }

    public class Class : SemanticNode, ISymbolContainer, IRoutineContainer
    {
        public Class(SemanticModel model, IClassContainer ns, Syntax.ClassDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            Parent = ns;
            Parent.Children.Add(this);
            foreach (var func in syntaxNode.Functions)
                (this as IRoutineContainer).AddFunction(func);

            foreach (var initialRoutine in syntaxNode.InitialRoutines)
                (this as IRoutineContainer).AddInitialRoutine(initialRoutine);
        }

        public Class(SemanticModel model, IClassContainer ns, string name) : base(model)
        {
            Name = name;
            Parent = ns;
            Parent.Children.Add(this);
        }

        public string Name { get; }
        public List<ISymbol> Symbols { get; } = [];
        public List<Function> Functions { get; } = [];
        public IClassContainer Parent { get; }
        public List<ISemanticScope> Children { get; } = [];
        public List<InitialRoutine> InitialRoutines { get; } = [];
        ISemanticScope? ISemanticScope.Parent => Parent;

        public void ElabSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.ClassDefinition syntaxNode)
            {
                foreach (var member in syntaxNode.ClassDeclarations)
                {
                    (this as ISymbolContainer).AddSymbol(member.Name, false, member.TypeSpecifier.Name, member.SourceLocation, member.Scope.ScopeDepth, null, member.IsReadonly, true);
                }

                foreach (var func in Functions)
                {
                    func.ElabSyntaxSymbols();
                }

                foreach (var initialRoutine in InitialRoutines)
                {
                    initialRoutine.ElabSyntaxSymbols();
                }
            }
        }
    }

    public class Function : SemanticNode, ISemanticScope, ISymbolContainer, ICodeContainer
    {
        public Function(SemanticModel model, IRoutineContainer parent, Syntax.FunctionDefinition syntaxNode) : base(model, syntaxNode)
        {
            Name = syntaxNode.Name;
            IsExtern = syntaxNode.IsExtern;
            IsPure = syntaxNode.IsPure;
            Parent = parent;
            Parent.Children.Add(this);
            Model.CompileTasks.Add(this);
            CodeSyntaxNode = syntaxNode.CodeBlock;
        }

        public Function(SemanticModel model, IRoutineContainer parent, string name, Dictionary<string, FunctionParameter> parameters, TypeInfo returnType, bool isExtern = false, bool? isPure = null) : base(model)
        {
            Name = name;
            IsExtern = isExtern;
            IsPure = isPure;
            Parent = parent;
            Parent.Children.Add(this);
            Parameters = parameters;
            ReturnTypeInfo = returnType;
            Parent.AddFunctionSymbol(this, false, ReturnTypeInfo, parameters, SourceLocation.Empty(), 0, null, true, false);
            foreach (var param in parameters.Values)
            {
                (this as ISymbolContainer).AddSymbol(param.Name, true, param.Type, SourceLocation.Empty(), 0, param.Index, param.IsReadonly, false);
            }
            Model.CompileTasks.Add(this);
        }

        public string Name { get; }

        ISemanticScope? ISemanticScope.Parent => Parent;

        public IRoutineContainer Parent { get; }

        public List<ISemanticScope> Children { get; } = [];

        public Dictionary<string, FunctionParameter> Parameters = [];

        public TypeInfo ReturnTypeInfo { get; private set; } = TypeInfo.BuiltinTypes["void"];

        public List<ISymbol> Symbols { get; } = [];

        public FunctionSymbol? FunctionSymbol { get; private set; } = null;

        public List<SemanticInstruction> Instructions { get; } = [];

        public Syntax.SyntaxNode? CodeSyntaxNode { get; }

        public bool IsExtern { get; }

        public bool? IsPure { get; }


        public void ElabSyntaxSymbols()
        {
            if (SyntaxNode is Syntax.FunctionDefinition func)
            {
                var retType = Model.ResolveType(func.ReturnType.Name);
                if (retType == null)
                {
                    Model.Reporter.Throw($"Cant resolve return type '{func.ReturnType.Name}'", func.SourceLocation);
                }
                else
                {
                    ReturnTypeInfo = retType;
                }

                var parameters = new Dictionary<string, FunctionParameter>();
                int i = 0;
                foreach (var param in func.Parameters)
                {
                    if (parameters.ContainsKey(param.Name))
                    {
                        Model.Reporter.Throw($"Duplicate parameter name '{param.Name}' for function '{func.Name}'", param.SourceLocation);
                    }
                    else
                    {
                        var paramTypeName = param.TypeSpecifier!.Name; // TODO: type inference
                        var paramType = Model.ResolveType(paramTypeName, this);
                        if (paramType == null)
                        {
                            Model.Reporter.Throw($"Cant resolve parameter type '{paramTypeName}' for param '{param.Name}'", param.SourceLocation);
                        }
                        else
                        {
                            parameters.Add(param.Name, new FunctionParameter(param.Name, paramType, param.IsReadonly, i));
                            (this as ISymbolContainer).AddSymbol(param.Name, true, paramType, param.SourceLocation, param.Scope.ScopeDepth, i, param.IsReadonly, false);
                        }
                    }
                    i++;
                }
                var funcSymbol = Parent.AddFunctionSymbol(this, false, ReturnTypeInfo, parameters, func.SourceLocation, func.Scope.ScopeDepth, null, true, false);
                FunctionSymbol = (FunctionSymbol)funcSymbol;
            }
        }
    }
}