
namespace BabyPenguin.SemanticPass
{
    public record VTableSlot(ISymbol InterfaceSymbol, ISymbol ImplementationSymbol);

    public class VTable : BaseSemanticNode, ISemanticNode, IRoutineContainer, ISymbolContainer
    {
        public VTable(SemanticModel model, IClass implementingClass, IInterface interfaceType) : base(model)
        {
            Name = "vtable-" + interfaceType.FullName.Replace(".", "-");
            Parent = implementingClass;
            Interface = interfaceType;
        }

        public VTable(SemanticModel model, InterfaceImplementation syntaxNode, IClass implementingClass) : base(model, syntaxNode)
        {
            if (Model.ResolveType(syntaxNode.InterfaceType.Text, s => s.IsInterfaceType, implementingClass) is not IInterface interfaceType)
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
    }

    public class InterfaceImplementationPass(SemanticModel model) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is IClass).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            switch (obj)
            {
                case IClass cls:
                    {
                        if (cls.SyntaxNode is ClassDefinition classSyntax)
                        {
                            foreach (var implSyntax in classSyntax.InterfaceImplementations)
                            {
                                var vtable = new VTable(Model, implSyntax, cls);
                                foreach (var funcSyntax in implSyntax.Functions)
                                {
                                    if (!vtable.Interface.Functions.Any(f => f.Name == funcSyntax.Name))
                                        throw new BabyPenguinException($"Interface {vtable.Interface.Name} does not have a function {funcSyntax.Name} to implement in class {cls.Name}");

                                    var func = new Function(Model, funcSyntax);
                                    (vtable as IRoutineContainer).AddFunction(func);
                                }

                                Model.CatchUp(vtable);

                                void checkFunction(Function interfaceFunc, Function implFunc)
                                {
                                    if (implFunc.ReturnTypeInfo.FullName != interfaceFunc.ReturnTypeInfo.FullName
                                                || implFunc.Parameters.Count != interfaceFunc.Parameters.Count
                                                || implFunc.Parameters.Zip(interfaceFunc.Parameters, (p1, p2) => p1.Type.FullName != p2.Type.FullName).Any(b => b))
                                    {
                                        throw new BabyPenguinException($"Function {interfaceFunc.Name} in interface {vtable.Interface.Name} does not match the implementation in class {cls.Name}");
                                    }
                                }

                                foreach (var interfaceFunc in vtable.Interface.Functions)
                                {
                                    if (interfaceFunc.IsDeclarationOnly)
                                    {
                                        if (vtable.Functions.Find(f => f.Name == interfaceFunc.Name) is Function implFunc)
                                        {
                                            checkFunction(interfaceFunc, implFunc);
                                            vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, implFunc.FunctionSymbol!));
                                        }
                                        else
                                        {
                                            throw new BabyPenguinException($"Interface {vtable.Interface.Name} requires an implementation for function {interfaceFunc.Name} in class {cls.FullName}");
                                        }
                                    }
                                    else
                                    {
                                        if (vtable.Functions.Find(f => f.Name == interfaceFunc.Name) is Function implFunc)
                                        {
                                            checkFunction(interfaceFunc, implFunc);
                                            vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, implFunc.FunctionSymbol!));
                                        }
                                        else
                                        {
                                            vtable.Slots.Add(new VTableSlot(interfaceFunc.FunctionSymbol!, interfaceFunc.FunctionSymbol!));
                                        }
                                    }
                                }

                                cls.VTables.Add(vtable);
                            }
                        }
                    }
                    break;
            }
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