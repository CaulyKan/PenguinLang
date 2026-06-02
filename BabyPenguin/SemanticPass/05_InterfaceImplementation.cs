
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
            foreach (var cls in items.OfType<IClassNode>())
            {
                CallInterfaceConstructor(cls);
            }
            foreach (var obj in items)
            {
                obj.PassIndex = PassIndex;
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
                            throw new BabyPenguinException($"Could not resolve type {impl.InterfaceType.Text} in namespace {ns.FullName()}", impl.SourceLocation);
                        if (interfaceTypeNode is IInterfaceNode intf && intf.HasDeclartion)
                            throw new BabyPenguinException($"Interface {intf.FullName()} has declarations, so it must be implemented in the scope of a class.");

                        var forType = Model.ResolveType(impl.ForType!.Text, scope: ns, useImmutableAsDefault: false);
                        if (forType == null)
                            throw new BabyPenguinException($"Could not resolve type {impl.ForType.Text} in namespace {ns.FullName()}", impl.SourceLocation);

                        if (forType.TypeNode.FullName() == implementingClass.FullName())
                            yield return impl;
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
                    else interfaceImplementations = [];

                    interfaceImplementations.AddRange(CollectInterfaceForImplementation(container));

                    foreach (var implSyntax in interfaceImplementations)
                    {
                        if (!CheckWhere(implSyntax.WhereDefinition, container))
                            continue;

                        var vtable = new VTable(Model, implSyntax, container);
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
                                throw new BabyPenguinException($"Interface {vtable.Interface.Name} does not have a function {funcSyntax.Name} to implement in class {container.Name}");

                            var func = new Function(Model, funcSyntax);
                            (vtable as IRoutineContainer).AddFunction(func);
                        }

                        Model.CatchUp(vtable);

                        foreach (var interfaceFunc in vtable.Interface.Functions)
                        {
                            if (vtable.Functions.Find(f => f.Name == interfaceFunc.Name) is IFunction implFunc)
                            {
                                if (implFunc.ReturnTypeInfo.FullName() != interfaceFunc.ReturnTypeInfo.FullName()
                                        || implFunc.Parameters.Count != interfaceFunc.Parameters.Count
                                        || implFunc.Parameters.Zip(interfaceFunc.Parameters, (p1, p2) => p1.Type.FullName() != p2.Type.FullName()).Any(b => b))
                                {
                                    throw new BabyPenguinException($"Function {interfaceFunc.Name} in interface {vtable.Interface.Name} does not match the implementation in class {container.Name}");
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

            // Enums are value types
            if (typeNode.IsEnumType) return true;

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

        private bool CheckWhere(WhereDefinition? whereDefinition, IVTableContainer container)
        {
            if (whereDefinition is null)
                return true;

            foreach (var condition in whereDefinition.WhereClauses)
            {
                var leftType = Model.ResolveType(condition.Identifier!.Text, scope: container);
                if (leftType == null)
                    throw new BabyPenguinException($"Could not resolve type {condition.Identifier.Text}", condition.SourceLocation);

                var rightType = Model.ResolveType(condition.TypeSpecifier!.Text, scope: container);
                if (rightType == null)
                    throw new BabyPenguinException($"Could not resolve type {condition.TypeSpecifier.Text}", condition.SourceLocation);

                if (!leftType.CanImplicitlyCastTo(rightType))
                    return false;
            }
            return true;
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
                                throw new BabyPenguinException($"Interface '{vtable.Interface.Name}' requires an implementation for function '{interfaceFunc.Name}' in class '{container.FullName()}'");
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
                var funcSymbol = intf.Constructor?.FunctionSymbol ?? throw new BabyPenguinException($"Cant resolve constructor for interface '{intf.Name}'");
                if (cls.Constructor == null) throw new BabyPenguinException($"Cant resolve constructor for class '{cls.Name}'");
                var thisSymbol = Model.ResolveShortSymbol("this", scope: cls.Constructor) ?? throw new BabyPenguinException($"Cant resolve 'this' for '{cls.Constructor.FullName()}'");
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