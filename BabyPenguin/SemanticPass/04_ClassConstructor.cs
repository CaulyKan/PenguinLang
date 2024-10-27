namespace BabyPenguin.SemanticPass
{

    public class ClassConstructorPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is IClass).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            if (obj is IClass cls)
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Class constructor pass for class '{cls.Name}' is skipped now because it is generic");
                }
                else
                {
                    ProcessClass(cls);
                }
            }

            obj.PassIndex = PassIndex;
        }

        public void ProcessClass(IClass cls)
        {
            var sourceLocation = cls.SyntaxNode?.SourceLocation ?? SourceLocation.Empty();

            if (cls.Functions.Find(i => i.Name == "new") is Function constructorFunc)
            {
                if (constructorFunc.Parameters.Count > 0 &&
                    constructorFunc.Parameters[0].Type.FullName == cls.FullName)
                {
                    cls.Constructor = constructorFunc;
                }
                else
                {
                    throw new BabyPenguinException($"Constructor function of class '{cls.Name}' should have first parameter of type '{cls.FullName}'", sourceLocation);
                }
            }
            else
            {
                List<FunctionParameter> param = [new FunctionParameter("this", cls, false, 0)];
                cls.Constructor = new Function(Model, "new", param, BasicType.Void, false, false);
                cls.AddFunction((Function)cls.Constructor);
                Model.CatchUp(cls.Constructor);
            }

            if (cls.SyntaxNode is ClassDefinition syntaxNode)
            {
                var constructorBody = (cls.Constructor as ICodeContainer)!;
                foreach (var varDecl in syntaxNode.ClassDeclarations)
                {
                    if (varDecl.Initializer is Expression initializer)
                    {
                        var memberSymbol = Model.ResolveShortSymbol(varDecl.Name, scope: cls)!;
                        var thisSymbol = Model.ResolveShortSymbol("this", scope: cls.Constructor)!;
                        var temp = constructorBody.AddExpression(initializer);
                        constructorBody.AddInstruction(new WriteMemberInstruction(memberSymbol, temp, thisSymbol));
                    }
                }
            }
        }

        public string Report => "";
    }
}