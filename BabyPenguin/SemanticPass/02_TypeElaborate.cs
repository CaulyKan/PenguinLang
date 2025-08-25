using System.Linq;
using BabyPenguin.SemanticNode;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticPass
{
    public class TypeElaboratePass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is ITypeNode).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            obj.PassIndex = PassIndex;
        }

        public string Report
        {
            get
            {
                var table = new ConsoleTable("Name", "Namespace", "Type", "Generic Parameters");
                Model.Types.Select(t => table.AddRow(t.Name, t.Namespace, t.GetType().Name.Replace("Node", ""), string.Join(", ", t.GenericDefinitions))).ToList();
                return table.ToMarkDownString();
            }
        }
    }
}
