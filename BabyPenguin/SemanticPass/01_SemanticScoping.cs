using InitialRoutine = BabyPenguin.SemanticNode.InitialRoutine;
using Namespace = BabyPenguin.SemanticNode.Namespace;

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