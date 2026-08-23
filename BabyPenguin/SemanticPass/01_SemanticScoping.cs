namespace BabyPenguin.SemanticPass
{
    public record NamespaceImport(string Namespace, PenguinLangParser.SourceLocation SourceLocation);

    public class SemanticScopingPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        /// <summary>Hidden member functions generated for class-level initial
        /// routines (spawned by the class constructor, pass 04).</summary>
        public const string ClassInitialPrefix = "__initial_";

        /// <summary>Hidden member functions generated for class-level construct
        /// blocks (invoked by the constructor before initial spawn, pass 04).
        /// Declarations inside become instance fields (pass 03).</summary>
        public const string ClassConstructPrefix = "__class_construct_";

        /// <summary>Hidden functions generated for top-level construct blocks
        /// (synchronously invoked by _main before any initial job, pass 08).
        /// Declarations inside are hoisted to the enclosing namespace (pass 03).</summary>
        public const string ConstructPrefix = "__construct_";

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
                            var class_ = new ClassNode(Model, classNode);
                            if (ns.Classes.Any(c => c.Name == class_.Name))
                                throw new BabyPenguinException($"Class '{class_.Name}' already exists in namespace '{ns.Name}'.", classNode.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            ns.AddClass(class_);
                            Process(class_);
                        }

                        foreach (var initialRoutineNode in namespaceSyntax.InitialRoutines)
                        {
                            var initialRoutine = new InitialRoutine(Model, initialRoutineNode);
                            if (ns.InitialRoutines.Any(c => c.Name == initialRoutine.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutine.Name}' already exists in namespace '{ns.Name}'.", initialRoutineNode.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            ns.AddInitialRoutine(initialRoutine);
                        }

                        foreach (var constructNode in namespaceSyntax.Constructs.Select((c, i) => (c, i)))
                        {
                            // Elaboration block: a hidden synchronous function whose
                            // lets are hoisted to this namespace; _main invokes all
                            // constructs before spawning any initial routine.
                            var bodyText = constructNode.c.Body?.BuildText() ?? "{}";
                            var funcDef = new FunctionDefinition();
                            funcDef.FromString($"fun {ConstructPrefix}{constructNode.i}() {bodyText}", Model.Reporter);
                            var function = new Function(Model, funcDef);
                            ns.AddFunction(function);
                        }


                        foreach (var func in namespaceSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (ns.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in namespace '{ns.Name}'.", func.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            ns.AddFunction(function);
                        }

                        foreach (var enumNode in namespaceSyntax.Enums)
                        {
                            var enum_ = new SemanticNode.EnumNode(Model, enumNode);
                            if (ns.Enums.Any(c => c.Name == enum_.Name))
                                throw new BabyPenguinException($"Enum '{enum_.Name}' already exists in namespace '{ns.Name}'.", enumNode.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            ns.AddEnum(enum_);
                            Process(enum_);
                        }

                        foreach (var intf in namespaceSyntax.Interfaces)
                        {
                            var interface_ = new InterfaceNode(Model, intf);
                            if (ns.Interfaces.Any(c => c.Name == interface_.Name))
                                throw new BabyPenguinException($"Interface '{interface_.Name}' already exists in namespace '{ns.Name}'.", intf.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            ns.AddInterface(interface_);
                            Process(interface_);
                        }
                    }
                    break;
                case IClassNode cls:
                    if (cls.SyntaxNode is ClassDefinition classSyntax)
                    {
                        foreach (var initialRoutineNode in classSyntax.InitialRoutines)
                        {
                            // Class-level initial routines become hidden member
                            // functions taking `mut this` (their bodies access
                            // module fields/ports); the generated constructor
                            // spawns them after wiring (pass 04). The body is
                            // re-parsed through a synthetic function definition.
                            var bodyText = initialRoutineNode.CodeBlockExpression?.BuildText() ?? "{}";
                            var funcDef = new FunctionDefinition();
                            funcDef.FromString($"fun {ClassInitialPrefix}{initialRoutineNode.Name}(mut this) {bodyText}", Model.Reporter);
                            var function = new Function(Model, funcDef);
                            if (cls.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutineNode.Name}' already exists in class '{cls.Name}'.", initialRoutineNode.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            cls.AddFunction(function);
                        }


                        foreach (var func in classSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (cls.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in class '{cls.Name}'.", func.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            cls.AddFunction(function);
                        }

                        foreach (var constructNode in classSyntax.Constructs.Select((c, i) => (c, i)))
                        {
                            // Class-level elaboration block: a hidden member function
                            // taking `mut this`, invoked by the constructor BEFORE the
                            // initial routines spawn (wiring precedues process start);
                            // its lets become instance fields (pass 03).
                            var bodyText = constructNode.c.Body?.BuildText() ?? "{}";
                            var funcDef = new FunctionDefinition();
                            funcDef.FromString($"fun {ClassConstructPrefix}{constructNode.i}(mut this) {bodyText}", Model.Reporter);
                            var constructFunction = new Function(Model, funcDef);
                            cls.AddFunction(constructFunction);
                        }
                    }
                    break;
                case IInterfaceNode intf:
                    if (intf.SyntaxNode is InterfaceDefinition interfaceSyntax)
                    {
                        foreach (var func in interfaceSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (intf.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in interface '{intf.Name}'.", func.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            if (function.Name == "new")
                                throw new BabyPenguinException($"Function 'new' is not allowed in interface '{intf.Name}'.", func.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            intf.AddFunction(function);
                        }
                    }
                    break;
                case IEnumNode enm:
                    if (enm.SyntaxNode is EnumDefinition enumSyntax)
                    {
                        foreach (var initialRoutineNode in enumSyntax.InitialRoutines)
                        {
                            var initialRoutine = new InitialRoutine(Model, initialRoutineNode);
                            if (enm.InitialRoutines.Any(c => c.Name == initialRoutine.Name))
                                throw new BabyPenguinException($"Initial routine '{initialRoutine.Name}' already exists in enum '{enm.Name}'.", initialRoutineNode.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
                            enm.AddInitialRoutine(initialRoutine);
                        }


                        foreach (var func in enumSyntax.Functions)
                        {
                            var function = new Function(Model, func);
                            if (enm.Functions.Any(c => c.Name == function.Name))
                                throw new BabyPenguinException($"Function '{function.Name}' already exists in enum '{enm.Name}'.", func.SourceLocation, code: ErrorCode.E_DUPLICATE_SYMBOL);
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
                Model.Traverse(t => table.AddRow(t.FullName(), t.GetType().Name));
                return table.ToMarkDownString();
            }
        }
    }
}