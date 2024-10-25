using System.Linq;
using BabyPenguin.SemanticNode;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticPass
{
    public class TypeElaboratePass(SemanticModel model) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is IType).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            switch (obj)
            {
                case Class class_:
                    {
                        break;
                    }
                case SemanticNode.Enum enum_:
                    {
                        break;
                    }
                default:
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