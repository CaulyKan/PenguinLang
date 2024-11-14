
namespace BabyPenguin.SemanticPass
{
    public record VTableSlot(ISymbol InterfaceSymbol, ISymbol ImplementationSymbol);

    public class VTable : BaseSemanticNode, ISemanticNode, IRoutineContainer, ISymbolContainer
    {
        public VTable(SemanticModel model, IVTableContainer implementingClass, IInterface interfaceType) : base(model)
        {
            Name = "vtable-" + interfaceType.FullName.Replace(".", "-");
            Parent = implementingClass;
            Interface = interfaceType;
        }

        public VTable(SemanticModel model, IInterfaceImplementation syntaxNode, IVTableContainer implementingClass) : base(model, syntaxNode as SyntaxNode)
        {
            var type = Model.ResolveType(syntaxNode.InterfaceType.Text, s => s.IsInterfaceType, implementingClass);
            if (type is not IInterface interfaceType)
                throw new BabyPenguinException($"Could not resolve interface type {syntaxNode.InterfaceType.Text} in class {implementingClass.Name}");
            Name = "vtable-" + interfaceType.FullName.Replace(".", "-");
            Parent = implementingClass;
            Interface = interfaceType;
        }

        public IInterface Interface { get; }

        public List<VTableSlot> Slots { get; } = [];

        public string Name { get; }

        public ISemanticScope? Parent { get; set; }

        public List<SemanticNode.InitialRoutine> InitialRoutines => throw new NotImplementedException();

        public List<Function> Functions { get; } = [];

        public IEnumerable<ISemanticScope> Children => Functions;

        public List<NamespaceImport> ImportedNamespaces { get; } = [];

        public List<ISymbol> Symbols { get; } = [];

        public string FullName => Parent!.FullName + "." + Name;

        public bool IsMerged { get; set; } = false;
    }

    public interface IVTableContainer : ISemanticScope, IType
    {
        List<VTable> VTables { get; }

        IEnumerable<IInterface> ImplementedInterfaces => VTables.Select(v => v.Interface);
    }

    public class InterfaceImplementationPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            var items = Model.FindAll(o => o is IVTableContainer).ToList();
            foreach (var obj in items)
            {
                BuiltVTable(obj);
            }
            foreach (var obj in items)
            {
                MergeVTables(obj);
            }
            foreach (var obj in items)
            {
                FinishVTable(obj);
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
                        var forType = Model.ResolveType(impl.ForType.Text, scope: ns);
                        if (forType == null)
                            throw new BabyPenguinException($"Could not resolve type {impl.ForType.Text} in namespace {ns.FullName}", impl.SourceLocation);

                        if (forType.FullName == implementingClass.FullName)
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
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Interface implementation for '{container.FullName}' is skipped now because it is generic");
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
                    else return;

                    interfaceImplementations.AddRange(CollectInterfaceForImplementation(container));

                    foreach (var implSyntax in interfaceImplementations)
                    {
                        if (!CheckWhere(implSyntax.WhereDefinition, container))
                            continue;

                        var vtable = new VTable(Model, implSyntax, container);
                        if (container.VTables.Find(v => v.Interface.FullName == vtable.Interface.FullName) is VTable existingVTable)
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
                            if (vtable.Functions.Find(f => f.Name == interfaceFunc.Name) is Function implFunc)
                            {
                                if (implFunc.ReturnTypeInfo.FullName != interfaceFunc.ReturnTypeInfo.FullName
                                        || implFunc.Parameters.Count != interfaceFunc.Parameters.Count
                                        || implFunc.Parameters.Zip(interfaceFunc.Parameters, (p1, p2) => p1.Type.FullName != p2.Type.FullName).Any(b => b))
                                {
                                    throw new BabyPenguinException($"Function {interfaceFunc.Name} in interface {vtable.Interface.Name} does not match the implementation in class {container.Name}");
                                }
                                vtable.Slots.RemoveAll(s => s.InterfaceSymbol.FullName == interfaceFunc.FunctionSymbol!.FullName);
                                vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, implFunc.FunctionSymbol!));
                            }
                        }
                    }
                }
            }
        }

        private bool CheckWhere(WhereDefinition? whereDefinition, IVTableContainer container)
        {
            if (whereDefinition is null)
                return true;

            foreach (var condition in whereDefinition.WhereClauses)
            {
                var leftType = Model.ResolveType(condition.Identifier.Text, scope: container);
                if (leftType == null)
                    throw new BabyPenguinException($"Could not resolve type {condition.Identifier.Text}", condition.SourceLocation);

                var rightType = Model.ResolveType(condition.TypeSpecifier.Text, scope: container);
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
                        if (container.VTables.Find(v => v.Interface.FullName == interfaceVtable.Interface.FullName) is VTable existingVTable)
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
                        if (!vtable.Slots.Exists(vs => vs.InterfaceSymbol.FullName == interfaceFunc.FunctionSymbol!.FullName))
                        {
                            if (interfaceFunc.IsDeclarationOnly)
                            {
                                throw new BabyPenguinException($"Interface {vtable.Interface.Name} requires an implementation for function {interfaceFunc.Name} in class {container.FullName}");
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

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            BuiltVTable(obj);
            MergeVTables(obj);
            FinishVTable(obj);

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
                            table.AddRow((cls as IClass).FullName, vtable.Interface.FullName, slot.InterfaceSymbol.FullName, slot.ImplementationSymbol.FullName);
                        }
                    }
                }
                return table.ToMarkDownString();
            }
        }
    }
}