namespace BabyPenguin.SemanticPass
{
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

                        foreach (var onRoutineNode in namespaceSyntax.OnRoutines)
                        {
                            var onRoutine = new OnRoutine(Model, onRoutineNode);
                            if (ns.InitialRoutines.Any(c => c.Name == onRoutine.Name))
                                throw new BabyPenguinException($"On routine '{onRoutine.Name}' already exists in namespace '{ns.Name}'.", onRoutine.SourceLocation);
                            ns.AddOnRoutine(onRoutine);
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

                        foreach (var onRoutineNode in classSyntax.OnRoutines)
                        {
                            var onRoutine = new OnRoutine(Model, onRoutineNode);
                            if (cls.InitialRoutines.Any(c => c.Name == onRoutine.Name))
                                throw new BabyPenguinException($"On routine '{onRoutine.Name}' already exists in namespace '{cls.Name}'.", onRoutine.SourceLocation);
                            cls.AddOnRoutine(onRoutine);
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

                        foreach (var onRoutineNode in enumSyntax.OnRoutines)
                        {
                            var onRoutine = new OnRoutine(Model, onRoutineNode);
                            if (enm.InitialRoutines.Any(c => c.Name == onRoutine.Name))
                                throw new BabyPenguinException($"On routine '{onRoutine.Name}' already exists in namespace '{enm.Name}'.", onRoutine.SourceLocation);
                            enm.AddOnRoutine(onRoutine);
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