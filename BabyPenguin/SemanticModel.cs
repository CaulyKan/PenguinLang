using System.Reflection;

namespace BabyPenguin
{
    public partial class SemanticModel
    {
        public List<MergedNamespace> Namespaces { get; } = [];
        public IEnumerable<Namespace> AllNamespaces => Namespaces.SelectMany(n => n.Namespaces);
        public IEnumerable<ClassNode> Classes => FindAll(s => s is ClassNode).Cast<ClassNode>();
        public IEnumerable<InterfaceNode> Interfaces => FindAll(s => s is InterfaceNode).Cast<InterfaceNode>();
        public IEnumerable<SemanticNode.EnumNode> Enums => FindAll(s => s is SemanticNode.EnumNode).Cast<SemanticNode.EnumNode>();
        public IEnumerable<ITypeNode> Types => FindAll(s => s is ITypeNode).Cast<ITypeNode>();
        public ErrorReporter Reporter { get; } = new ErrorReporter();
        public IEnumerable<ISymbol> Symbols => FindAll(s => s is ISymbolContainer).Cast<ISymbolContainer>().SelectMany(c => c.Symbols);
        public List<ISemanticPass> Passes { get; }
        public MergedNamespace BuiltinNamespace => Namespaces.Find(n => n.Name == "__builtin") ?? throw new BabyPenguinException("Builtin namespace not found.", SourceLocation.Empty());

        public BasicTypeNodes BasicTypeNodes { get; }

        public SemanticModel(bool addBuiltin = true, ErrorReporter? reporter = null)
        {
            Reporter = reporter ?? new ErrorReporter();
            BasicTypeNodes = new BasicTypeNodes(this);

            if (addBuiltin)
            {
                var builtinFile = Path.GetFullPath(Environment.GetEnvironmentVariable("PENGUINLANG_BUILTIN") ??
                    (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/Builtin.penguin"));
                var builtinCode = File.ReadAllText(builtinFile);
                this.AddSource(builtinCode, builtinFile);
            }

            Passes = [
                new SemanticScopingPass(this, 1),
                new TypeElaboratePass(this, 2),
                new SymbolElaboratePass(this, 3),
                new ConstructorPass(this, 4),
                new InterfaceImplementationPass(this, 5),
                new SyntaxRewritingPass(this, 6),
                new CodeGenerationPass(this, 7),
                new MainFunctionGenerationPass(this, 8),
                new CheckReturnValuePass(this, 9),
            ];
        }

        public T GetPass<T>() where T : ISemanticPass
        {
            return Passes.OfType<T>().First();
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

        public void AddNamespace(Namespace ns)
        {
            if (Namespaces.Find(n => n.Name == ns.Name) is MergedNamespace existing)
            {
                existing.Namespaces.Add(ns);
                ns.Parent = existing;
            }
            else
            {
                var merged = new MergedNamespace(this, ns.Name);
                merged.Namespaces.Add(ns);
                Namespaces.Add(merged);
                ns.Parent = merged;
            }
        }

        public ISemanticScope? GetScopeFromSourceLocation(SourceLocation location)
        {
            ISemanticScope? result = null;

            void FindSmallestScope(ISemanticScope scope)
            {
                if (scope.SourceLocation.Contains(location))
                {
                    result = scope;
                    foreach (var child in scope.Children)
                    {
                        FindSmallestScope(child);
                    }
                }
            }

            foreach (var ns in AllNamespaces)
            {
                FindSmallestScope(ns);
            }

            return result;
        }

        public ISymbol? ResolveSymbol(string name, Predicate<ISymbol>? predicate = null, ISemanticScope? scope = null, bool isOriginName = true, uint scopeDepth = uint.MaxValue, bool checkImportedNamespaces = true)
        {
            var nameComponents = NameComponents.ParseName(name);
            if (nameComponents.Prefix.Count == 0)
                return ResolveShortSymbol(name, predicate, scope, isOriginName, scopeDepth, checkImportedNamespaces);

            var type = ResolveTypeNode(nameComponents.PrefixString, scope: scope);
            if (type as ISemanticScope != null)
                return ResolveShortSymbol(nameComponents.Name, predicate, type as ISemanticScope, isOriginName, scopeDepth, checkImportedNamespaces, nameComponents.IsMutable);

            var ns = Namespaces.FirstOrDefault(n => n.Name == nameComponents.PrefixString);
            if (ns != null)
                return ResolveShortSymbol(nameComponents.Name, predicate, ns, isOriginName, scopeDepth, checkImportedNamespaces);

            var prefixSymbol = ResolveSymbol(nameComponents.PrefixString, predicate, scope, isOriginName, scopeDepth, checkImportedNamespaces);
            if (prefixSymbol != null)
            {
                if (prefixSymbol.WithoutMutability() is FunctionSymbol functionSymbol)
                {
                    var symbol = ResolveShortSymbol(nameComponents.Name, predicate, functionSymbol.CodeContainer, isOriginName, scopeDepth, checkImportedNamespaces);
                    if (symbol != null) return symbol;
                }
                else if (prefixSymbol.WithoutMutability() is VariableSymbol variableSymbol)
                {
                    var parentType = variableSymbol.TypeInfo.TypeNode as ISymbolContainer ??
                        throw new BabyPenguinException($"${nameComponents.PrefixString} is expected to be a Type");
                    var symbol = ResolveShortSymbol(nameComponents.Name, predicate, parentType, isOriginName, scopeDepth, checkImportedNamespaces, prefixSymbol.IsMutable);
                    if (symbol != null)
                        return symbol;
                }
            }
            return null;
        }

        public ISymbol? ResolveShortSymbol(string name, Predicate<ISymbol>? predicate = null, ISemanticScope? scope = null, bool isOriginName = true, uint scopeDepth = uint.MaxValue, bool checkImportedNamespaces = true, Mutability parentMutability = Mutability.Immutable)
        {
            ISymbol? symbol = null;
            var predicate_ = predicate ?? (s => true);

            if (scope == null)
            {
                symbol = Symbols.OrderByDescending(s => s.ScopeDepth)
                   .FirstOrDefault(s => s.FullName() == name && s.ScopeDepth <= scopeDepth && predicate_(s));
            }
            else
            {
                if (scope is MergedNamespace mergedNamespace)
                {
                    symbol = mergedNamespace.Symbols.OrderByDescending(s => s.ScopeDepth)
                        .FirstOrDefault(s => (isOriginName ? s.OriginName : s.Name) == name && s.ScopeDepth <= scopeDepth && predicate_(s));
                }
                else if (scope is ISymbolContainer symbolContainer)
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

            if (symbol?.IsMutable == Mutability.Auto)
            {
                symbol = new MutableSymbolProxy(symbol, parentMutability);
            }
            return symbol;
        }

        public ITypeNode? ResolveTypeNode(string name, Predicate<ITypeNode>? predicate = null, ISemanticScope? scope = null)
        {
            var predicate_ = predicate ?? (t => true);
            var nameComponents = NameComponents.ParseName(name);
            if (nameComponents.IsMutable != Mutability.Auto)
                throw new BabyPenguinException("ResolveTypeNode dont support mutability");

            // check if is a built-in type
            if (BasicTypeNodes.Nodes.TryGetValue(name, out BasicTypeNode? value))
                return value;

            // check if is 'Self'
            if (name == "Self")
            {
                if (scope is null)
                    throw new BabyPenguinException("Self is not allowed here.", SourceLocation.Empty());
                if (scope is ITypeNode typ)
                    return typ;
                else
                    return ResolveTypeNode(name, predicate, scope.Parent);
            }

            // check if is a function type
            if (nameComponents.NameWithPrefix == "fun" || nameComponents.NameWithPrefix == "async_fun")
            {
                var genericArgumentsFromName = nameComponents.Generics.Select(g => ResolveType(g, scope: scope)).ToList();
                if (genericArgumentsFromName.Any(a => a == null))
                    return null;
                var funType = nameComponents.NameWithPrefix == "async_fun" ? BasicTypeNodes.AsyncFun : BasicTypeNodes.Fun;
                return funType.Specialize(genericArgumentsFromName!);
            }

            // check if is a generic definition
            if (scope != null && nameComponents.Prefix.Count == 0 && nameComponents.Generics.Count == 0)
            {
                var genericAncestor = scope.FindAncestorIncludingSelf(s => s is ITypeNode genericable && genericable.IsGeneric &&
                    genericable.IsSpecialized && genericable.GenericDefinitions.Contains(name)) as ITypeNode;
                if (genericAncestor != null)
                {
                    var genericArgument = genericAncestor.GenericArguments[genericAncestor.GenericDefinitions.IndexOf(name)];
                    var res = genericArgument.TypeNode;
                    if (res == null) return null;
                    else if (predicate_(res))
                        return res;
                }
            }

            // check local type symbol
            if (scope is ISymbolContainer symbolContainer)
            {
                if (symbolContainer.Symbols.FirstOrDefault(s => s is TypeReferenceSymbol t && t.Name == name
                    && t.TypeReference.TypeNode != null && predicate_(t.TypeReference.TypeNode)) is TypeReferenceSymbol typeRefSymbol)
                {
                    return typeRefSymbol.TypeReference.TypeNode;
                }
            }

            // check avaiable namespaces
            var namespace_ = Namespaces.FirstOrDefault(ns => ns.Name == nameComponents.PrefixString);
            if (namespace_ == null && scope == null) return null;
            var namespaceCandidates = namespace_ != null ? [namespace_] : scope!.GetImportedNamespaces().ToArray();

            // determine type without generic arguments
            ITypeNode? typeCandidate = null;
            foreach (var ns in namespaceCandidates)
            {
                if (ns.Classes.FirstOrDefault(c => c.Name == nameComponents.Name && predicate_(c)) is ITypeNode cls)
                {
                    typeCandidate = cls;
                    break;
                }
                else if (ns.Enums.FirstOrDefault(e => e.Name == nameComponents.Name && predicate_(e)) is ITypeNode enm)
                {
                    typeCandidate = enm;
                    break;
                }
                else if (ns.Interfaces.FirstOrDefault(e => e.Name == nameComponents.Name && predicate_(e)) is ITypeNode intf)
                {
                    typeCandidate = intf;
                    break;
                }
                else if (ns.Symbols.FirstOrDefault(symbol => symbol is TypeReferenceSymbol s && s.Name == nameComponents.Name
                    && s.TypeReference.TypeNode != null && predicate_(s.TypeReference.TypeNode)) is TypeReferenceSymbol typeRefSymbol)
                {
                    typeCandidate = typeRefSymbol.TypeReference.TypeNode;
                    break;
                }
            }

            if (typeCandidate is null)
            {
                // Reporter.Write(DiagnosticLevel.Warning, $"Cant resolve type {nameComponents.NameWithPrefix}");
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
                    var genericArgumentsFromName = nameComponents.Generics.Select(g => ResolveType(g, scope: scope)).ToList();
                    for (int i = 0; i < genericArgumentsFromName.Count; i++)
                    {
                        if (genericArgumentsFromName[i] == null)
                        {
                            // Reporter.Write(DiagnosticLevel.Warning, $"Cant resolve type for {nameComponents.Generics[i]}");
                            return null;
                        }
                    }
                    var genericTypeInfo = typeCandidate.GenericInstances.FirstOrDefault(i =>
                        i.GenericArguments.Select(i => i.FullName()).ToList().SequenceEqual(
                            genericArgumentsFromName.Select(j => j!.FullName()).ToList()));

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

        public IType? ResolveType(string name, Predicate<IType>? predicate = null, ISemanticScope? scope = null, bool useImmutableAsDefault = true)
        {
            var predicate_ = predicate ?? (t => true);
            var nameComponents = NameComponents.ParseName(name);
            var nameWithoutMut = nameComponents.ToStringWithoutMutability();
            var isMutable = nameComponents.IsMutable;
            if (useImmutableAsDefault && isMutable == Mutability.Auto)
                isMutable = Mutability.Immutable;

            var typeNode = ResolveTypeNode(nameWithoutMut, t => predicate_(t.ToType(isMutable)), scope);
            if (typeNode == null)
                return null;

            // double check the generic type for correct mutability
            if (scope != null && nameComponents.Prefix.Count == 0 && nameComponents.Generics.Count == 0)
            {
                var genericAncestor = scope.FindAncestorIncludingSelf(s => s is ITypeNode genericable && genericable.IsGeneric &&
                    genericable.IsSpecialized && genericable.GenericDefinitions.Contains(name)) as ITypeNode;
                if (genericAncestor != null)
                {
                    var genericArgument = genericAncestor.GenericArguments[genericAncestor.GenericDefinitions.IndexOf(name)];
                    if (!useImmutableAsDefault && isMutable != Mutability.Auto)
                        throw new BabyPenguinException($"Setting mutability of generic argument is not allowed");
                    else
                        isMutable = genericArgument.IsMutable;
                }
            }

            return typeNode.ToType(isMutable);
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
                    {
                        pass.Process(child);
                    }
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
                    Reporter.Write(DiagnosticLevel.Debug, $"Pass {pass.GetType().Name} report:\n" + pass.Report);
                Reporter.Write(DiagnosticLevel.Info, $"Pass {pass.GetType().Name} completed");
            }
        }

        public List<string> Files { get; } = [];

        public void AddSource(string? source, string fileName)
        {
            var context = PenguinParser.Parse(source ?? File.ReadAllText(fileName), fileName, Reporter);
            var syntaxCompiler = new SyntaxCompiler(fileName, context, Reporter);

            syntaxCompiler.Compile();

            foreach (var ns in syntaxCompiler.Namespaces)
            {
                AddNamespace(new Namespace(this, ns));
            }
            Files.Add(fileName);
        }

        public void WriteReport(string file)
        {
            if (File.Exists(file))
                File.Delete(file);
            using (var f = File.OpenWrite(file))
            using (var sb = new StreamWriter(f))
            {
                sb.WriteLine($"BabyPenguin Compile report for: ");
                foreach (var s in Files.Select(i => Path.GetFullPath(file)))
                    sb.WriteLine($"    {s}");
                sb.WriteLine("");

                sb.WriteLine("=======================================================");
                sb.WriteLine($"Symbols:");
                sb.WriteLine("=======================================================");

                foreach (var obj in FindAll(o => o is ISymbolContainer).Cast<ISymbolContainer>())
                {
                    sb.WriteLine("----------------------------------------------");
                    sb.WriteLine($"{obj.FullName()}");
                    sb.WriteLine("----------------------------------------------");
                    sb.WriteLine(obj.PrintSymbolTable());
                    sb.WriteLine();

                    if (obj is ITypeNode typeNode)
                    {
                        foreach (var inst in typeNode.GenericInstances.Cast<ISymbolContainer>())
                        {
                            sb.WriteLine("----------------------------------------------");
                            sb.WriteLine($"{obj.FullName()}");
                            sb.WriteLine("----------------------------------------------");
                            sb.WriteLine(obj.PrintSymbolTable());
                            sb.WriteLine();
                        }
                    }
                }

                sb.WriteLine("=======================================================");
                sb.WriteLine($"Compiled IL:");
                sb.WriteLine("=======================================================");

                foreach (var obj in FindAll(o => o is ICodeContainer))
                {
                    sb.WriteLine("----------------------------------------------");
                    sb.WriteLine($"{obj.FullName()}");
                    sb.WriteLine("----------------------------------------------");
                    if (obj.SyntaxNode == null)
                        sb.WriteLine("No AST available.");
                    else
                        sb.WriteLine(Tools.FormatPenguinLangSource(obj.SyntaxNode.BuildText()));
                    if (obj is ICodeContainer codeContainer && codeContainer.Instructions.Count > 0)
                    {
                        sb.WriteLine("----------------------------------------------");
                        sb.WriteLine(codeContainer.PrintInstructionsTable());
                        sb.WriteLine();
                    }
                }
            }
        }

        public Or<ISymbol, IType>? GetDefinitionFromSourceLocation(SourceLocation sourceLocation)
        {
            var scope = GetScopeFromSourceLocation(sourceLocation);
            if (scope?.SyntaxNode == null) return null;

            Or<ISymbol, IType>? result = null;

            scope.SyntaxNode.TraverseChildren((node, parent) =>
            {
                if (node.SourceLocation.Contains(sourceLocation))
                {
                    if (node is TypeSpecifier typeSpecifier)
                    {
                        var type = ResolveType(typeSpecifier.Name, scope: scope);
                        if (type != null)
                            result = new(type);
                    }
                    else if (node is Identifier identifier)
                    {
                        var symbol = ResolveSymbol(identifier.Name, scope: scope);
                        if (symbol != null)
                            result = new(symbol);
                    }
                    else return true;

                    return false;
                }
                return true;
            });

            return result;
        }
    }
}