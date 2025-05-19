namespace BabyPenguin.SemanticPass
{
    public interface ISemanticScope : ISemanticNode
    {
        ISemanticScope? Parent { get; set; }

        IEnumerable<ISemanticScope> Children { get; }

        List<NamespaceImport> ImportedNamespaces { get; }

        void Traverse(Action<ISemanticScope> action)
        {
            action(this);
            foreach (var child in Children)
                child.Traverse(action);
        }

        ISemanticScope? FindAncestorIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                return this;

            return FindAncestor(predicate);
        }

        ISemanticScope? FindAncestor(Predicate<ISemanticScope> predicate)
        {
            if (Parent == null)
                return null;

            if (predicate(Parent))
                return Parent;

            return Parent.FindAncestor(predicate);
        }

        ISemanticScope? FindChildIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                return this;

            foreach (var child in Children)
            {
                var result = child.FindChild(predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        ISemanticScope? FindChild(Predicate<ISemanticScope> predicate)
        {
            foreach (var child in Children)
            {
                var result = child.FindChildIncludingSelf(predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        IEnumerable<ISemanticScope> FindChildrenIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                yield return this;

            if (this is IType typeObj)
                foreach (var specialization in typeObj.GenericInstances.Cast<ISemanticScope>())
                    foreach (var res in specialization.FindChildrenIncludingSelf(predicate))
                        yield return res;

            foreach (var child in Children)
                foreach (var res in child.FindChildrenIncludingSelf(predicate))
                    yield return res;
        }

        IEnumerable<ISemanticScope> FindChildren(Predicate<ISemanticScope> predicate)
        {
            foreach (var child in Children)
                foreach (var res in child.FindChildrenIncludingSelf(predicate))
                    yield return res;
        }

        IEnumerable<MergedNamespace> GetImportedNamespaces(bool includeBuiltin = true)
        {
            return ImportedNamespaces.Select(i =>
                    Model.Namespaces.Find(n => n.Name == i.Namespace) ??
                        throw new BabyPenguinException($"Namespace '{i}' not found.", i.SourceLocation))
                .Concat(
                    Parent?.GetImportedNamespaces(false) ?? []
                ).Concat(
                    includeBuiltin ? [Model.BuiltinNamespace] : Array.Empty<MergedNamespace>()
                ).Concat(
                    this is MergedNamespace ns ? [ns] : Array.Empty<MergedNamespace>()
                );
        }
    }

    public interface ITypeContainer : ISemanticScope
    {
        void AddClass(Class cls)
        {
            Classes.Add(cls);
            cls.Parent = this;
        }

        void AddEnum(SemanticNode.Enum enm)
        {
            Enums.Add(enm);
            enm.Parent = this;
        }

        void AddInterface(Interface intf)
        {
            Interfaces.Add(intf);
            intf.Parent = this;
        }

        static ulong counter = 0;
        MemberAccessExpression CreateMemberAccess(bool isRead, Identifier identifier)
        {
            MemberAccessExpression result = isRead ? new ReadMemberAccessExpression() : new WriteMemberAccessExpression();
            result.Text = $"this.{identifier.Name}";
            result.SourceLocation = identifier.SourceLocation;
            result.ScopeDepth = identifier.ScopeDepth;
            result.PrimaryExpression = new PrimaryExpression
            {
                Text = "this",
                SourceLocation = identifier.SourceLocation,
                ScopeDepth = identifier.ScopeDepth,
                PrimaryExpressionType = PrimaryExpression.Type.Identifier,
                Identifier = new SymbolIdentifier
                {
                    Text = "this",
                    SourceLocation = identifier.SourceLocation,
                    ScopeDepth = identifier.ScopeDepth,
                    LiteralName = "this",
                }
            };
            result.MemberIdentifiers = [ new SymbolIdentifier
                {
                    Text = identifier.Name,
                    SourceLocation = identifier.SourceLocation,
                    ScopeDepth = identifier.ScopeDepth,
                    LiteralName = identifier.Name
                }
            ];
            return result;
        }

        public IClass AddLambdaClass(string nameHint, SyntaxNode? syntaxNode, List<FunctionParameter> parameters, IType returnType, List<ISymbol> closureSymbols, SourceLocation sourceLocation, uint scopeDepth, bool isPure = false, bool returnValueIsReadonly = false, bool? isAsync = false)
        {
            var parametersString = string.Join(", ", parameters.Select(p => $"{(p.IsReadonly ? "val" : "var")} {p.Name} : {p.Type.FullName}"));
            var declarationStrings = closureSymbols.Select(s => $"{(s.IsReadonly ? "val" : "var")} {s.Name} : {s.TypeInfo.FullName}").ToList();

            var name = $"__lambda_{nameHint}_{counter++}";
            string text = "";
            if (syntaxNode != null)
            {
                syntaxNode.TraverseChildren((node, parent) =>
                {
                    if (node is IdentifierOrMemberAccess identifierOrMember)
                    {
                        if (identifierOrMember.Identifier != null && closureSymbols.Any(s => s.Name == identifierOrMember.Identifier.Name))
                        {
                            identifierOrMember.MemberAccess = CreateMemberAccess(false, identifierOrMember.Identifier);
                            identifierOrMember.Identifier = null;
                        }
                    }
                    else if (node is PrimaryExpression primary)
                    {
                        if (primary.PrimaryExpressionType == PrimaryExpression.Type.Identifier &&
                            primary.Identifier != null &&
                            closureSymbols.Any(s => s.Name == primary.Identifier.Name))
                        {
                            primary.PrimaryExpressionType = PrimaryExpression.Type.ParenthesizedExpression;
                            primary.ParenthesizedExpression = CreateMemberAccess(true, primary.Identifier);
                        }
                    }
                    return true;
                });
                text = syntaxNode.BuildSourceText();
            }

            var source = @$"
                class {name} {{
                    {string.Join("\n", declarationStrings.Select(i => i + ";"))}
                    fun new(var this: {name}{(declarationStrings.Count > 0 ? ", " : "")}{string.Join(", ", declarationStrings)}) {{
                        {string.Join("\n", closureSymbols.Select(s => $"this.{s.Name} = {s.Name};"))}
                    }}
                    fun call(var this: {name}{(!string.IsNullOrEmpty(parametersString) ? ", " : "")}{parametersString}) -> {returnType.FullName} {{
                        {text}
                    }}
                }}
            ";

            var classDefinition = new ClassDefinition();
            classDefinition.FromString(source, (this.SyntaxNode?.ScopeDepth ?? 0) + 1, Reporter);
            var cls = new Class(Model, classDefinition);

            AddClass(cls);

            Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Adding lambda class/function {cls.Name}");
            Model.CatchUp(cls);

            return cls;
        }

        List<Class> Classes { get; }

        List<SemanticNode.Enum> Enums { get; }

        List<Interface> Interfaces { get; }
    }

    public interface IRoutineContainer : ISemanticScope
    {
        void AddInitialRoutine(IInitialRoutine routine)
        {
            InitialRoutines.Add(routine);
            routine.Parent = this;
        }

        void AddFunction(IFunction function)
        {
            Functions.Add(function);
            function.Parent = this;
        }

        List<IInitialRoutine> InitialRoutines { get; }

        List<IFunction> Functions { get; }
    }

    public record NamespaceImport(string Namespace, PenguinLangSyntax.SourceLocation SourceLocation);

    public class SemanticScopingPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            switch (obj)
            {
                case MergedNamespace mns:
                    {
                        foreach (var ns in mns.Namespaces)
                        {
                            Process(ns);
                        }
                        break;
                    }
                case INamespace ns:
                    if (ns.SyntaxNode is NamespaceDefinition namespaceSyntax)
                    {
                        foreach (var classNode in namespaceSyntax.Classes)
                        {
                            var class_ = new Class(Model, classNode);
                            if (ns.Classes.Any(c => c.Name == class_.Name))
                                throw new BabyPenguinException($"Class '{class_.Name}' already exists in namespace '{ns.Name}'.", classNode.SourceLocation);
                            ns.AddClass(class_);
                            Process(class_);
                        }

                        foreach (var initialRoutineNode in namespaceSyntax.InitialRoutines)
                        {
                            var initialRoutine = new InitialRoutine(Model, initialRoutineNode);
                            if (ns.InitialRoutines.Any(c => c.Name == initialRoutine.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutine.Name}' already exists in namespace '{ns.Name}'.", initialRoutineNode.SourceLocation);
                            ns.AddInitialRoutine(initialRoutine);
                        }

                        foreach (var func in namespaceSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (ns.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in namespace '{ns.Name}'.", func.SourceLocation);
                            ns.AddFunction(function);
                        }

                        foreach (var enumNode in namespaceSyntax.Enums)
                        {
                            var enum_ = new SemanticNode.Enum(Model, enumNode);
                            if (ns.Enums.Any(c => c.Name == enum_.Name))
                                throw new BabyPenguinException($"Enum '{enum_.Name}' already exists in namespace '{ns.Name}'.", enumNode.SourceLocation);
                            ns.AddEnum(enum_);
                            Process(enum_);
                        }

                        foreach (var intf in namespaceSyntax.Interfaces)
                        {
                            var interface_ = new Interface(Model, intf);
                            if (ns.Interfaces.Any(c => c.Name == interface_.Name))
                                throw new BabyPenguinException($"Interface '{interface_.Name}' already exists in namespace '{ns.Name}'.", intf.SourceLocation);
                            ns.AddInterface(interface_);
                            Process(interface_);
                        }
                    }
                    break;
                case IClass cls:
                    if (cls.SyntaxNode is ClassDefinition classSyntax)
                    {
                        foreach (var initialRoutineNode in classSyntax.InitialRoutines)
                        {
                            var initialRoutine = new InitialRoutine(Model, initialRoutineNode);
                            if (cls.InitialRoutines.Any(c => c.Name == initialRoutine.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutine.Name}' already exists in class '{cls.Name}'.", initialRoutineNode.SourceLocation);
                            cls.AddInitialRoutine(initialRoutine);
                        }
                        foreach (var func in classSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (cls.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in class '{cls.Name}'.", func.SourceLocation);
                            cls.AddFunction(function);
                        }
                    }
                    break;
                case IInterface intf:
                    if (intf.SyntaxNode is InterfaceDefinition interfaceSyntax)
                    {
                        foreach (var func in interfaceSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (intf.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in interface '{intf.Name}'.", func.SourceLocation);
                            if (function.Name == "new")
                                throw new BabyPenguinException($"Function 'new' is not allowed in interface '{intf.Name}'.", func.SourceLocation);
                            intf.AddFunction(function);
                        }
                    }
                    break;
                case IEnum enm:
                    if (enm.SyntaxNode is EnumDefinition enumSyntax)
                    {
                        foreach (var initialRoutineNode in enumSyntax.InitialRoutines)
                        {
                            var initialRoutine = new InitialRoutine(Model, initialRoutineNode);
                            if (enm.InitialRoutines.Any(c => c.Name == initialRoutine.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutine.Name}' already exists in enum '{enm.Name}'.", initialRoutineNode.SourceLocation);
                            enm.AddInitialRoutine(initialRoutine);
                        }
                        foreach (var func in enumSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (enm.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in enum '{enm.Name}'.", func.SourceLocation);
                            enm.AddFunction(function);
                        }
                    }
                    break;
                default:
                    break;
            }

            obj.PassIndex = PassIndex;
        }

        public void Process()
        {
            foreach (var ns in Model.Namespaces)
                Process(ns);
        }

        public string Report
        {
            get
            {
                var table = new ConsoleTable("Name", "Type");
                Model.Traverse(t => table.AddRow(t.FullName, t.GetType().Name));
                return table.ToMarkDownString();
            }
        }
    }
}