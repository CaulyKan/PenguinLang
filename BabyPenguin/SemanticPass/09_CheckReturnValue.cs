
namespace BabyPenguin.SemanticPass
{

    public class CheckReturnValuePass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process(ISemanticNode node)
        {
            // do nothing
        }

        public void Process()
        {
            foreach (ICodeContainer codeContainer in Model.FindAll(i => i is ICodeContainer).Cast<ICodeContainer>())
            {
                bool returnVoid = false;
                if (codeContainer is IFunction function)
                {
                    if (function.ReturnTypeInfo.IsVoidType)
                    {
                        returnVoid = true;
                    }
                    else
                    {
                        // TODO: check if all path return a value
                    }
                }

                returnVoid |= codeContainer is IInitialRoutine;

                // if (returnVoid)
                {
                    if (codeContainer.Instructions.Count() == 0 || codeContainer.Instructions.Last() is not ReturnInstruction
                        || (codeContainer.Instructions.Last() is ReturnInstruction returnInstruction && (returnInstruction.ReturnStatus == ReturnStatus.Blocked || returnInstruction.ReturnStatus == ReturnStatus.YieldNotFinished)))
                    {
                        Model.Reporter.Write(DiagnosticLevel.Debug, $"Adding return for '{codeContainer.FullName()}'");
                        codeContainer.Instructions.Add(new ReturnInstruction(codeContainer.SourceLocation.EndLocation, null, ReturnStatus.Finished));
                    }
                }
            }
        }

        private StringBuilder sb = new StringBuilder();
        public string Report
        {
            get
            {
                return sb.ToString();
            }
        }
    }
}