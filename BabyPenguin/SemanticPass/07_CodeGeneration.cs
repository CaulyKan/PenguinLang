using System.Reflection;

namespace BabyPenguin.SemanticPass
{
    public class CodeGenerationPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is ICodeContainer || o is IType).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj is ICodeContainer container)
            {
                var fullName = container.FullName();
                if (obj is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is IType t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Code generation pass for '{fullName}' is skipped now because it is inside a generic type");
                }
                else
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Generating code for '{fullName}'...");
                    container.CompileSyntaxStatements();
                }
            }

            obj.PassIndex = PassIndex;
        }

        public string Report
        {
            get
            {
                StringBuilder sb = new();
                foreach (var obj in Model.FindAll(o => o is ICodeContainer))
                {
                    if (obj is ICodeContainer codeContainer && codeContainer.Instructions.Count > 0)
                    {
                        sb.AppendLine($"Compile Result For {obj.FullName()}:");
                        sb.AppendLine(codeContainer.PrintInstructionsTable());
                    }
                }
                return sb.ToString();
            }
        }
    }
}