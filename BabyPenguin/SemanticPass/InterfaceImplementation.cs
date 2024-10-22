namespace BabyPenguin.SemanticPass
{


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
                                var impl = new SemanticNode.InterfaceImplementation(Model, implSyntax);
                                impl.InterfaceType = Model.ResolveType(implSyntax.InterfaceType.Text, s => s.IsInterfaceType, cls) as Interface;
                                implSyntax.Functions.ForEach(f => (impl as IRoutineContainer).AddFunction(new Function(Model, f)));
                                if (impl.InterfaceType == null)
                                    throw new BabyPenguinException($"Could not resolve interface type {implSyntax.InterfaceType.Text} in class {cls.Name}");

                                foreach (var func in impl.InterfaceType.Functions)
                                {
                                    if (func.IsDeclarationOnly)
                                    {
                                        if (!impl.Functions.Any(f => f.Name == func.Name))
                                            throw new BabyPenguinException($"Interface {impl.InterfaceType.Name} requires an implementation for function {func.Name} in class {cls.FullName}");
                                    }
                                    else
                                    {
                                        if (!impl.Functions.Any(f => f.Name == func.Name))
                                        {
                                            var fs = func.SyntaxNode as FunctionDefinition ?? throw new NotImplementedException();
                                            (impl as IRoutineContainer).AddFunction(new Function(Model, fs));
                                        }
                                    }
                                }

                                Model.CatchUp(impl);
                                cls.ImplementedInterfaces.Add(impl);
                                impl.Parent = cls;
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
                var table = new ConsoleTable("Name", "Namespace", "Type", "Generic Parameters");
                Model.Types.Select(t => table.AddRow(t.Name, t.Namespace, t.Type, string.Join(", ", t.GenericDefinitions))).ToList();
                return table.ToMarkDownString();
            }
        }
    }
}