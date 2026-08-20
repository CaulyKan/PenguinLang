
namespace BabyPenguin.SemanticPass
{
    public class InterfaceImplementationPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            var items = Model.BasicTypeNodes.Nodes.Values.Concat(Model.FindAll(o => o is IVTableContainer)).ToList();
            foreach (var obj in items)
            {
                BuiltVTable(obj);
            }
            // Step 1: Auto-classify classes as IValueType or IReferenceType
            foreach (var cls in items.OfType<IClassNode>())
            {
                AutoClassifyClass(cls);
            }
            // Step 2: Auto-add ICopy<T> for IValueType classes that don't have it
            foreach (var cls in items.OfType<IClassNode>())
            {
                AutoAddICopy(cls);
            }
            foreach (var obj in items)
            {
                MergeVTables(obj);
            }
            foreach (var obj in items)
            {
                FinishVTable(obj);
            }
            // Validate that non-IRef interfaces are not used as class/enum fields or enum
            // payloads. Such interfaces have unknown size at the language level and can only
            // live as locals/parameters or behind an explicit Box<T>. (VTables are merged by
            // this point, so IsUnsizedInterface can resolve the IReferenceType marker.)
            ValidateInterfaceFieldTypes(items);
            foreach (var cls in items.OfType<IClassNode>())
            {
                CallInterfaceConstructor(cls);
            }
            foreach (var obj in items)
            {
                obj.PassIndex = PassIndex;
            }
        }

        /// <summary>
        /// Validates that no class field or enum variant payload has a non-IRef interface type.
        /// Non-IRef interfaces have unknown size at the language level and cannot be stored in
        /// fields; they must be wrapped in Box&lt;T&gt; or the interface must impl IReferenceType.
        /// Iterates specialized (concrete) types so generic instantiations like
        /// <c>_ListNode&lt;IFutureBase&gt;.value</c> are also checked.
        /// </summary>
        void ValidateInterfaceFieldTypes(IEnumerable<ISemanticNode> items)
        {
            foreach (var cls in items.OfType<IClassNode>())
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                    continue;
                foreach (var symbol in cls.Symbols)
                {
                    if (!symbol.IsClassMember || !symbol.IsVariable)
                        continue;
                    var fieldType = symbol.TypeInfo;
                    if (fieldType != null
                        && IRTypeClassifier.IsUnsizedInterface(fieldType))
                    {
                        throw new BabyPenguinException(
                            $"Field '{symbol.Name}' of class '{cls.FullName()}' has non-IRef interface type "
                            + $"'{fieldType.TypeNode!.FullName()}'. Interfaces without IReferenceType have unknown "
                            + $"size and cannot be used as fields. Use Box<{fieldType.TypeNode!.FullName()}> for "
                            + "explicit indirection, or add 'impl IReferenceType' to the interface.",
                            symbol.SourceLocation, code: ErrorCode.E_TYPE_MISMATCH);
                    }
                }
            }

            foreach (var enm in items.OfType<IEnumNode>())
            {
                if (enm.IsGeneric && !enm.IsSpecialized)
                    continue;
                foreach (var decl in enm.EnumDeclarations)
                {
                    var payloadType = decl.TypeInfo;
                    if (payloadType != null
                        && payloadType.TypeNode is IInterfaceNode
                        && IRTypeClassifier.IsUnsizedInterface(payloadType))
                    {
                        throw new BabyPenguinException(
                            $"Enum variant '{enm.FullName()}.{decl.Name}' has non-IRef interface payload type "
                            + $"'{payloadType.TypeNode!.FullName()}'. Interfaces without IReferenceType have unknown "
                            + $"size and cannot be used as enum payloads. Use Box<{payloadType.TypeNode!.FullName()}> "
                            + "for explicit indirection, or add 'impl IReferenceType' to the interface.",
                            decl.SourceLocation, code: ErrorCode.E_TYPE_MISMATCH);
                    }
                }
            }
        }

        IEnumerable<IInterfaceImplementation> CollectInterfaceForImplementation(IVTableContainer implementingClass)
        {
            foreach (var ns in Model.Namespaces.SelectMany(n => n.Namespaces))
            {
                if (ns.SyntaxNode is NamespaceDefinition namespaceDefinition)
                {
                    foreach (var impl in namespaceDefinition.InterfaceImplementations)
                    {
                        var interfaceTypeNode = Model.ResolveTypeNode(impl.InterfaceType!.Text, scope: ns);
                        if (interfaceTypeNode == null)
                            throw new BabyPenguinException($"Could not resolve type {impl.InterfaceType.Text} in namespace {ns.FullName()}", impl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                        if (interfaceTypeNode is IInterfaceNode intf && intf.HasDeclartion)
                            throw new BabyPenguinException($"Interface {intf.FullName()} has declarations, so it must be implemented in the scope of a class.", code: ErrorCode.E_INTERNAL);

                        var forType = Model.ResolveType(impl.ForType!.Text, scope: ns, useImmutableAsDefault: false);
                        if (forType == null)
                            throw new BabyPenguinException($"Could not resolve type {impl.ForType.Text} in namespace {ns.FullName()}", impl.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);

                        if (forType.TypeNode.FullName() == implementingClass.FullName())
                        {
                            // Orphan principle: at least one of (interface, target type) must be defined
                            // in the same namespace as the impl block, otherwise it's an orphan impl.
                            var implNsPrefix = ns.FullName() + ".";
                            bool interfaceLocal = interfaceTypeNode.FullName().StartsWith(implNsPrefix);
                            bool typeLocal = forType.FullName().StartsWith(implNsPrefix);
                            if (!interfaceLocal && !typeLocal)
                            {
                                throw new BabyPenguinException(
                                    $"Orphan interface implementation: impl {interfaceTypeNode.FullName()} "
                                    + $"for {forType.FullName()} in namespace {ns.FullName()} — "
                                    + $"neither type is local to this namespace.",
                                    impl.SourceLocation, code: ErrorCode.E_ORPHAN_IMPL);
                            }
                            yield return impl;
                        }
                    }
                }
            }
        }

        public void BuiltVTable(ISemanticNode obj)
        {
            if (obj is IVTableContainer container)
            {
                if (container.IsGeneric && !container.IsSpecialized)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Interface implementation for '{container.FullName()}' is skipped now because it is generic");
                }
                else
                {
                    List<IInterfaceImplementation> interfaceImplementations;
                    if (container.SyntaxNode is ClassDefinition classSyntax)
                    {
                        interfaceImplementations = classSyntax.InterfaceImplementations.Cast<IInterfaceImplementation>().ToList();
                    }
                    else if (container.SyntaxNode is InterfaceDefinition interfaceSyntax)
                    {
                        interfaceImplementations = interfaceSyntax.InterfaceImplementations.Cast<IInterfaceImplementation>().ToList();
                    }
                    else if (container.SyntaxNode is EnumDefinition enumSyntax)
                    {
                        interfaceImplementations = enumSyntax.InterfaceImplementations.Cast<IInterfaceImplementation>().ToList();
                    }
                    else interfaceImplementations = [];

                    interfaceImplementations.AddRange(CollectInterfaceForImplementation(container));

                    foreach (var implSyntax in interfaceImplementations)
                    {
                        // Pre-resolve the interface type with the correct namespace scope.
                        // For namespace-level impl blocks (e.g. `impl IFoo for Bar`), resolve
                        // from the containing namespace; for inline impls (inside class body),
                        // leave null so VTable constructor falls back to implementingClass scope.
                        IInterfaceNode? preResolvedInterface = null;
                        INamespace? implNamespace = null;
                        if (implSyntax is PenguinLangParser.SyntaxNodes.InterfaceForImplementation)
                        {
                            foreach (var ns in Model.Namespaces.SelectMany(n => n.Namespaces))
                            {
                                if (ns.SyntaxNode is PenguinLangParser.SyntaxNodes.NamespaceDefinition nsDef && nsDef.InterfaceImplementations.Contains(implSyntax))
                                {
                                    preResolvedInterface = Model.ResolveTypeNode(implSyntax.InterfaceType!.Text, s => s is IInterfaceNode, ns) as IInterfaceNode;
                                    implNamespace = ns;
                                    break;
                                }
                            }
                        }
                        var vtable = new VTable(Model, implSyntax, container, preResolvedInterface);
                        if (container.VTables.Find(v => v.Interface.FullName() == vtable.Interface.FullName()) is VTable existingVTable)
                        {
                            vtable = existingVTable;
                        }
                        else
                        {
                            container.VTables.Add(vtable);
                        }

                        foreach (var funcSyntax in implSyntax.Functions)
                        {
                            if (!vtable.Interface.Functions.Any(f => f.Name == funcSyntax.Name))
                                throw new BabyPenguinException($"Interface {vtable.Interface.Name} does not have a function {funcSyntax.Name} to implement in class {container.Name}", null, code: ErrorCode.E_INTERFACE_IMPL);

                            var func = new Function(Model, funcSyntax);
                            (vtable as IRoutineContainer).AddFunction(func);
                        }

                        if (implNamespace != null)
                        {
                            // Add the impl block's containing namespace to the VTable's imported
                            // namespaces BEFORE CatchUp so that parameter types (like `IFoo` in
                            // `this: IFoo`) can be resolved correctly during SymbolElaboratePass.
                            vtable.ImportedNamespaces.Add(new NamespaceImport(implNamespace.Name, PenguinLangParser.SourceLocation.Empty()));
                        }

                        Model.CatchUp(vtable);

                        foreach (var interfaceFunc in vtable.Interface.Functions)
                        {
                            if (vtable.Functions.Find(f => f.Name == interfaceFunc.Name) is IFunction implFunc)
                            {
                                if (implFunc.ReturnTypeInfo.TypeNode.FullName() != interfaceFunc.ReturnTypeInfo.TypeNode.FullName()
                                        || implFunc.Parameters.Count != interfaceFunc.Parameters.Count
                                        || implFunc.Parameters
                                            .Zip(interfaceFunc.Parameters, (p1, p2) => (impl: p1, intf: p2))
                                            .Where(pair => pair.impl.Name != "this")
                                            .Any(pair => pair.impl.Type.TypeNode.FullName() != pair.intf.Type.TypeNode.FullName()))
                                {
                                    throw new BabyPenguinException($"Function {interfaceFunc.Name} in interface {vtable.Interface.Name} does not match the implementation in class {container.Name}", null, code: ErrorCode.E_TYPE_MISMATCH);
                                }
                                vtable.Slots.RemoveAll(s => s.InterfaceSymbol.FullName() == interfaceFunc.FunctionSymbol!.FullName());
                                vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, implFunc.FunctionSymbol!));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Auto-classifies a class as IValueType or IReferenceType based on its field types.
        /// Skips if the class already explicitly implements IValueType or IReferenceType.
        /// Empty classes (no fields) are not auto-classified — they stay as reference types.
        /// </summary>
        void AutoClassifyClass(IClassNode cls)
        {
            if (cls.IsGeneric && !cls.IsSpecialized)
                return;

            // Skip if already explicitly has IValueType or IReferenceType
            bool hasValueType = HasExplicitInterface(cls, "__builtin.IValueType");
            bool hasRefType = HasExplicitInterface(cls, "__builtin.IReferenceType");
            if (hasValueType || hasRefType)
                return;

            // Count fields — skip empty classes (they remain reference types by default)
            bool hasFields = false;
            foreach (var s in cls.Symbols)
            {
                if (s.IsClassMember && s.IsVariable)
                {
                    hasFields = true;
                    break;
                }
            }
            if (!hasFields)
                return;

            // Determine if class should be IValueType based on fields
            HashSet<string> visiting = new HashSet<string>();
            bool allValue = AllFieldsAreValueTypes(cls, visiting);

            var interfaceName = allValue ? "__builtin.IValueType" : "__builtin.IReferenceType";
            var interfaceNode = Model.ResolveTypeNode(interfaceName) as IInterfaceNode;
            if (interfaceNode == null)
                return;

            // Check if already has this vtable (from previous auto-classification)
            if (cls.VTables.Any(v => v.Interface.FullName() == interfaceName))
                return;

            var vtable = new VTable(Model, cls, interfaceNode);
            Model.CatchUp(vtable);
            cls.VTables.Add(vtable);
        }

        /// <summary>
        /// Auto-adds ICopy&lt;T&gt; for IValueType classes that don't manually implement it.
        /// </summary>
        void AutoAddICopy(IClassNode cls)
        {
            try
            {
                AutoAddICopyImpl(cls);
            }
            catch
            {
                // Auto-ICopy is an optimization. If it fails (e.g., due to existing
                // implicit ICopy from conditional impls), silently skip.
            }
        }

        void AutoAddICopyImpl(IClassNode cls)
        {
            if (cls.IsGeneric && !cls.IsSpecialized)
                return;

            // Only for IValueType classes (must have IValueType vtable)
            bool hasValueType = cls.VTables.Any(v => v.Interface.FullName() == "__builtin.IValueType");
            if (!hasValueType)
                return;

            // Check if class already has any ICopy vtable
            var className = cls.FullName();
            foreach (var v in cls.VTables)
            {
                var n = v.Interface.FullName();
                if (n.Contains("ICopy<") && n.Replace("!mut ", "").Replace("mut ", "").Contains(className))
                    return; // Already has ICopy
            }

            // Find the generic ICopy interface
            var icopyGeneric = Model.ResolveTypeNode("__builtin.ICopy<?>") as IInterfaceNode;
            if (icopyGeneric == null)
                return;

            // Try to find existing specialization by name match
            IInterfaceNode? specializedInterface = null;
            foreach (var existing in icopyGeneric.GenericInstances)
            {
                var n = existing.FullName();
                if (n.Contains("ICopy<") && n.Replace("!mut ", "").Replace("mut ", "").Contains(className))
                {
                    specializedInterface = existing as IInterfaceNode;
                    break;
                }
            }

            // Create specialized ICopy<ClassName> interface if needed
            if (specializedInterface == null)
            {
                var classType = cls.ToType(Mutability.Immutable);
                try
                {
                    specializedInterface = icopyGeneric.Specialize([classType]) as IInterfaceNode;
                }
                catch
                {
                    // Specialization already exists - find it after CatchUp
                    foreach (var existing in icopyGeneric.GenericInstances)
                    {
                        var n = existing.FullName();
                        if (n.Contains("ICopy<") && n.Contains(className))
                        {
                            specializedInterface = existing as IInterfaceNode;
                            break;
                        }
                    }
                }
                if (specializedInterface == null)
                    return;
            }

            // Don't add vtable if class already has ICopy
            foreach (var v in cls.VTables)
            {
                if (v.Interface.FullName().Contains("ICopy<") && v.Interface.FullName().Contains(className))
                    return;
            }

            var vtable = new VTable(Model, cls, specializedInterface);
            Model.CatchUp(vtable);
            cls.VTables.Add(vtable);
        }

        /// <summary>
        /// Checks if a class explicitly implements a given interface (not auto-generated).
        /// </summary>
        bool HasExplicitInterface(IClassNode cls, string interfaceFullName)
        {
            foreach (var intf in cls.ImplementedInterfaces)
            {
                if (intf.FullName() == interfaceFullName)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a class implements a given interface (including auto-generated ones via VTables).
        /// </summary>
        bool HasInterfaceViaVTable(IClassNode cls, string interfaceFullName)
        {
            if (HasExplicitInterface(cls, interfaceFullName))
                return true;
            return cls.VTables.Any(v => v.Interface.FullName() == interfaceFullName);
        }

        /// <summary>
        /// Checks if all fields of a class are value types (recursive with visiting set).
        /// </summary>
        static bool AllFieldsAreValueTypes(IClassNode cls, HashSet<string> visiting)
        {
            if (!visiting.Add(cls.FullName()))
                return false; // circular reference → not a value type

            bool hasFields = false;
            foreach (var symbol in cls.Symbols)
            {
                if (symbol.IsClassMember && symbol.IsVariable)
                {
                    hasFields = true;
                    var fieldType = symbol.TypeInfo;
                    if (!IsTypeValueLike(fieldType, visiting))
                    {
                        visiting.Remove(cls.FullName());
                        return false;
                    }
                }
            }

            visiting.Remove(cls.FullName());
            return hasFields; // Empty class returns false (not all-value-type)
        }

        /// <summary>
        /// Checks if a type is "value-like" (can be a field of an auto-IValueType class).
        /// Primitives (except string), enums, and IValueType classes are value-like.
        /// </summary>
        static bool IsTypeValueLike(IType type, HashSet<string> visiting)
        {
            var typeNode = type.TypeNode;
            if (typeNode == null) return false;

            // Primitive types (including string) — but string is ref in IR
            if (typeNode.IsBoolType || typeNode.IsVoidType) return true;
            if (typeNode.Type == TypeEnum.Char) return true;
            if (typeNode.Type == TypeEnum.I8 || typeNode.Type == TypeEnum.I16) return true;
            if (typeNode.Type == TypeEnum.I32 || typeNode.Type == TypeEnum.I64) return true;
            if (typeNode.Type == TypeEnum.U8 || typeNode.Type == TypeEnum.U16) return true;
            if (typeNode.Type == TypeEnum.U32 || typeNode.Type == TypeEnum.U64) return true;
            if (typeNode.Type == TypeEnum.Float || typeNode.Type == TypeEnum.Double) return true;

            // String: reference type, not value-like
            if (typeNode.IsStringType) return false;

            // Enums are value types — but only when their variant payload types
            // are themselves value-like. A payload class that (transitively)
            // contains this enum again is an infinite value layout: the cycle
            // makes the containing class reference-like (mirrors the
            // EmperorPenguin classifier, which walks specialized variant
            // payloads for the same reason).
            if (typeNode.IsEnumType)
            {
                if (typeNode is IEnumNode enumNode && visiting.Add(enumNode.FullName()))
                {
                    try
                    {
                        foreach (var decl in enumNode.EnumDeclarations)
                        {
                            var payloadType = decl.TypeInfo;
                            if (payloadType == null || payloadType.TypeNode == null) continue;
                            // Skip the tag-only sentinel (payload == the enum itself)
                            if (payloadType.TypeNode == typeNode) continue;
                            if (!IsTypeValueLike(payloadType, visiting))
                                return false;
                        }
                    }
                    finally
                    {
                        visiting.Remove(enumNode.FullName());
                    }
                }
                return true;
            }

            // Classes: check if they implement IValueType (explicit or auto)
            if (typeNode.IsClassType && typeNode is IClassNode classNode)
            {
                // Check explicit IValueType
                foreach (var intf in classNode.ImplementedInterfaces)
                {
                    if (intf.FullName() == "__builtin.IValueType")
                        return true;
                }
                // Check explicit IReferenceType
                foreach (var intf in classNode.ImplementedInterfaces)
                {
                    if (intf.FullName() == "__builtin.IReferenceType")
                        return false;
                }
                // Recursive check
                return AllFieldsAreValueTypes(classNode, visiting);
            }

            return false;
        }

        public void MergeVTables(ISemanticNode obj)
        {
            if (obj is IVTableContainer container)
            {
                foreach (var vtable in container.VTables.ToList())
                {
                    if (vtable.IsMerged)
                        continue;

                    MergeVTables(vtable.Interface);

                    foreach (var interfaceVtable in vtable.Interface.VTables)
                    {
                        if (container.VTables.Find(v => v.Interface.FullName() == interfaceVtable.Interface.FullName()) is VTable existingVTable)
                        {
                            // already have directly implemented vtable, ignore from interface
                        }
                        else
                        {
                            var newVtable = new VTable(Model, container, interfaceVtable.Interface);
                            foreach (var slot in interfaceVtable.Slots)
                            {
                                newVtable.Slots.Add(new VTableSlot(slot.InterfaceSymbol, slot.ImplementationSymbol));
                            }
                            Model.CatchUp(newVtable);
                            container.VTables.Add(newVtable);
                        }
                    }

                    vtable.IsMerged = true;
                }
            }
        }

        public void FinishVTable(ISemanticNode obj)
        {
            if (obj is IVTableContainer container)
            {
                foreach (var vtable in container.VTables)
                {
                    foreach (var interfaceFunc in vtable.Interface.Functions)
                    {
                        if (!vtable.Slots.Exists(vs => vs.InterfaceSymbol.FullName() == interfaceFunc.FunctionSymbol!.FullName()))
                        {
                            if (interfaceFunc.IsDeclarationOnly && container is not IInterfaceNode)
                            {
                                throw new BabyPenguinException($"Interface '{vtable.Interface.Name}' requires an implementation for function '{interfaceFunc.Name}' in class '{container.FullName()}'", null, code: ErrorCode.E_INTERFACE_IMPL);
                            }
                            else
                            {
                                vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, interfaceFunc.FunctionSymbol!));
                            }
                        }
                    }
                }
            }
        }

        public void CallInterfaceConstructor(IClassNode cls)
        {
            foreach (var vt in cls.VTables)
            {
                var intf = vt.Interface;
                var funcSymbol = intf.Constructor?.FunctionSymbol ?? throw new BabyPenguinException($"Cant resolve constructor for interface '{intf.Name}'", null, code: ErrorCode.E_NO_CONSTRUCTOR);
                if (cls.Constructor == null) throw new BabyPenguinException($"Cant resolve constructor for class '{cls.Name}'", null, code: ErrorCode.E_NO_CONSTRUCTOR);
                var thisSymbol = Model.ResolveShortSymbol("this", scope: cls.Constructor) ?? throw new BabyPenguinException($"Cant resolve 'this' for '{cls.Constructor.FullName()}'", null, code: ErrorCode.E_RESOLVE_SYMBOL);
                var intfSymbol = cls.Constructor.AllocTempSymbol(intf.ToType(thisSymbol.IsMutable), vt.SourceLocation);
                cls.Constructor.AddCastExpression(new(thisSymbol), intfSymbol, vt.SourceLocation);
                cls.Constructor.Instructions.Add(new FunctionCallInstruction(vt.SourceLocation, funcSymbol, [intfSymbol], null));
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            BuiltVTable(obj);
            MergeVTables(obj);
            FinishVTable(obj);
            if (obj is IClassNode cls)
                CallInterfaceConstructor(cls);

            obj.PassIndex = PassIndex;
        }

        public string Report
        {
            get
            {
                var table = new ConsoleTable("Class", "Interface", "Function", "Implementation");
                foreach (var cls in Model.Classes)
                {
                    foreach (var vtable in cls.VTables)
                    {
                        foreach (var slot in vtable.Slots)
                        {
                            table.AddRow((cls as IClassNode).FullName(), vtable.Interface.FullName(), slot.InterfaceSymbol.FullName(), slot.ImplementationSymbol.FullName());
                        }
                    }
                }
                return table.ToMarkDownString();
            }
        }
    }
}