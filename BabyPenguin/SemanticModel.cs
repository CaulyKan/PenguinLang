namespace BabyPenguin
{
    public class SemanticModel
    {
        public List<SemanticNode.Namespace> Namespaces { get; }
        public IEnumerable<Class> Classes => FindAll(s => s is Class).Cast<Class>();
        public IEnumerable<Interface> Interfaces => FindAll(s => s is Interface).Cast<Interface>();
        public IEnumerable<SemanticNode.Enum> Enums => FindAll(s => s is SemanticNode.Enum).Cast<SemanticNode.Enum>();
        public IEnumerable<IType> Types => FindAll(s => s is IType).Cast<IType>();
        public ErrorReporter Reporter { get; } = new ErrorReporter();
        public IEnumerable<ISymbol> Symbols => FindAll(s => s is ISymbolContainer).Cast<ISymbolContainer>().SelectMany(c => c.Symbols);
        public List<ISemanticPass> Passes { get; }
        public SemanticNode.Namespace BuiltinNamespace { get; }

        public SemanticModel(ErrorReporter? reporter = null)
        {
            Reporter = reporter ?? new ErrorReporter();
            BuiltinNamespace = AddBuiltin();
            Namespaces = [BuiltinNamespace];
            Passes = new List<ISemanticPass>() {
                new SemanticScopingPass(this),
                new TypeElaboratePass(this),
                new SymbolElaboratePass(this),
                new ClassConstructorPass(this),
                new InterfaceImplementationPass(this),
                new CodeGenerationPass(this),
            };
        }

        private SemanticNode.Namespace AddBuiltin()
        {
            var ns = new SemanticNode.Namespace(this, "__builtin");

            var println = new Function(this, "println",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);
            var print = new Function(this, "print",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);

            (ns as IRoutineContainer).AddFunction(println);
            (ns as IRoutineContainer).AddFunction(print);

            return ns;
        }

        public void Traverse(Action<ISemanticScope> action)
        {
            foreach (var ns in Namespaces)
                (ns as ISemanticScope).Traverse(action);
        }

        public IEnumerable<ISemanticScope> FindAll(Predicate<ISemanticScope> predicate)
        {
            return Namespaces.SelectMany(ns => (ns as ISemanticScope).FindChildrenIncludingSelf(predicate));
        }

        public void AddNamespace(SemanticNode.Namespace ns)
        {
            Namespaces.Add(ns);
            ns.Parent = null;
        }

        public ISymbol? ResolveSymbol(string name, Predicate<ISymbol>? predicate = null, ISemanticScope? scope = null, bool isOriginName = true, uint scopeDepth = uint.MaxValue, bool checkImportedNamespaces = true)
        {
            var nameComponents = NameComponents.ParseName(name);
            if (nameComponents.Prefix.Count == 0)
                return ResolveShortSymbol(name, predicate, scope, isOriginName, scopeDepth, checkImportedNamespaces);

            var type = ResolveType(nameComponents.PrefixString, scope: scope);
            if (type as ISemanticScope != null)
                return ResolveShortSymbol(nameComponents.Name, predicate, type as ISemanticScope, isOriginName, scopeDepth, checkImportedNamespaces);

            var ns = Namespaces.FirstOrDefault(n => n.Name == nameComponents.PrefixString);
            if (ns != null)
                return ResolveShortSymbol(nameComponents.Name, predicate, ns, isOriginName, scopeDepth, checkImportedNamespaces);

            return null;
        }

        public ISymbol? ResolveShortSymbol(string name, Predicate<ISymbol>? predicate = null, ISemanticScope? scope = null, bool isOriginName = true, uint scopeDepth = uint.MaxValue, bool checkImportedNamespaces = true)
        {
            ISymbol? symbol = null;
            var predicate_ = predicate ?? (s => true);

            if (scope == null)
            {
                symbol = Symbols.OrderByDescending(s => s.ScopeDepth)
                   .FirstOrDefault(s => s.FullName == name && s.ScopeDepth <= scopeDepth && predicate_(s));
            }
            else
            {
                if (scope is ISymbolContainer symbolContainer)
                {
                    symbol = symbolContainer.Symbols.OrderByDescending(s => s.ScopeDepth)
                        .FirstOrDefault(s => (isOriginName ? s.OriginName : s.Name) == name && s.ScopeDepth <= scopeDepth && predicate_(s));
                }

                if (symbol == null && scope?.Parent != null)
                {
                    symbol = ResolveShortSymbol(name, predicate, scope.Parent, isOriginName, scopeDepth);
                }

                if (symbol == null && checkImportedNamespaces)
                {
                    foreach (var ns in scope!.GetImportedNamespaces())
                    {
                        symbol = ResolveShortSymbol(name, predicate, ns, isOriginName, scopeDepth, false);
                        if (symbol != null) break;
                    }
                }
            }
            return symbol;
        }

        public IType? ResolveType(string name, Predicate<IType>? predicate = null, ISemanticScope? scope = null)
        {
            var nameComponents = NameComponents.ParseName(name);
            var predicate_ = predicate ?? (t => true);

            // check if is a built-in type
            if (BasicType.BasicTypes.TryGetValue(name, out BasicType? value))
                return value;

            // check if is a generic definition
            if (scope != null && nameComponents.Prefix.Count == 0 && nameComponents.Generics.Count == 0)
            {
                var genericAncestor = scope.FindAncestorIncludingSelf(s => s is IType genericable && genericable.IsGeneric &&
                    genericable.IsSpecialized && genericable.GenericDefinitions.Contains(name)) as IType;
                if (genericAncestor != null)
                {
                    var genericArgument = genericAncestor.GenericArguments[genericAncestor.GenericDefinitions.IndexOf(name)];
                    if (predicate_(genericArgument))
                        return genericArgument;
                }
            }

            // check avaiable namespaces
            var namespace_ = Namespaces.FirstOrDefault(ns => ns.Name == nameComponents.PrefixString);
            if (namespace_ == null && scope == null) return null;
            var namespaceCandidates = namespace_ != null ? [namespace_] : scope!.GetImportedNamespaces().ToArray();

            // determine type without generic arguments
            IType? typeCandidate = null;
            foreach (var ns in namespaceCandidates)
            {
                if (ns.Classes.Find(c => c.Name == nameComponents.Name && predicate_(c)) is IType cls)
                {
                    typeCandidate = cls;
                    break;
                }
                else if (ns.Enums.Find(e => e.Name == nameComponents.Name && predicate_(e)) is IType enm)
                {
                    typeCandidate = enm;
                    break;
                }
                else if (ns.Interfaces.Find(e => e.Name == nameComponents.Name && predicate_(e)) is IType intf)
                {
                    typeCandidate = intf;
                    break;
                }
            }

            if (typeCandidate is null)
            {
                Reporter.Write(ErrorReporter.DiagnosticLevel.Warning, $"Cant resolve type {nameComponents.NameWithPrefix}");
                return null;
            }

            // specialize type with generic arguments if necessary
            if (typeCandidate.IsGeneric && nameComponents.Generics.Count > 0)
            {
                if (nameComponents.Generics.All(i => i == "?"))
                {
                    return typeCandidate;
                }
                else
                {
                    var genericArgumentsFromName = nameComponents.Generics.Select(g => ResolveType(g, predicate, scope)).ToList();
                    for (int i = 0; i < genericArgumentsFromName.Count; i++)
                    {
                        if (genericArgumentsFromName[i] == null)
                        {
                            Reporter.Write(ErrorReporter.DiagnosticLevel.Warning, $"Cant resolve type for {nameComponents.Generics[i]}");
                            return null;
                        }
                    }
                    var genericTypeInfo = typeCandidate.GenericInstances.FirstOrDefault(i =>
                        i.GenericArguments.SequenceEqual(genericArgumentsFromName));

                    genericTypeInfo ??= typeCandidate.Specialize(genericArgumentsFromName.Select(i => i!).ToList());

                    return genericTypeInfo;
                }
            }
            else if (typeCandidate.IsGeneric && nameComponents.Generics.Count == 0)
            {
                throw new BabyPenguinException("Resolving generic type without generic arguments is not supported. If non-specialized type is needed, use '?' as generic argument.");
            }
            else
            {
                return typeCandidate;
            }
        }

        public int CurrentPassIndex { get; set; } = 0;

        public void CatchUp(ISemanticNode node)
        {
            for (int i = 0; i <= Math.Min(CurrentPassIndex, Passes.Count - 1); i++)
            {
                var pass = Passes[i];
                if (node is ISemanticScope scp)
                {
                    foreach (var child in scp.FindChildrenIncludingSelf(s => true).ToList())
                        pass.Process(child);
                }
                else
                {
                    pass.Process(node);
                }
            }
        }

        public void Compile()
        {
            for (CurrentPassIndex = 0; CurrentPassIndex < Passes.Count; CurrentPassIndex++)
            {
                var pass = Passes[CurrentPassIndex];
                pass.Process();

                var report = pass.Report;
                if (!string.IsNullOrEmpty(report))
                    Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Pass {pass.GetType().Name} report:\n" + pass.Report);
                Reporter.Write(ErrorReporter.DiagnosticLevel.Info, $"Pass {pass.GetType().Name} completed");
            }
        }
    }
}