using System.Collections.Generic;
using System.Linq;
using System.Text;
using BabyPenguin.SemanticInterface;
using BabyPenguin.SemanticNode;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>
    /// Lowers Penguin classes/enums to C# types. Classes → `sealed class` with data fields
    /// (reference semantics; value-type copy semantics deferred). Enums → struct { Meta, _tag, _payload }.
    /// Emits the transitive closure of types reachable from the given roots via field types.
    /// </summary>
    public class TypeLowerer
    {
        private readonly SemanticModel _model;
        private readonly CSharpEmitter _emitter;
        private readonly HashSet<string> _done = new(); // mangled type names already emitted
        private readonly Queue<string> _work = new();

        public TypeLowerer(SemanticModel model, CSharpEmitter emitter) { _model = model; _emitter = emitter; }

        /// <summary>Emit ALL specialized classes and enums (including generic specializations,
        /// which live in GenericInstances and aren't reached by FindAll). Robust against generic
        /// specializations whose names ResolveTypeNode may not resolve from raw IR strings.</summary>
        public string Lower(IEnumerable<string> rootTypeNames)
        {
            var toEmit = new List<ITypeNode>();
            foreach (var c in _model.Classes)
            {
                var t = (ITypeNode)c;
                if (t.IsSpecialized) toEmit.Add(t);
                toEmit.AddRange(t.GenericInstances);
            }
            foreach (var e in _model.Enums)
            {
                var t = (ITypeNode)e;
                if (t.IsSpecialized) toEmit.Add(t);
                toEmit.AddRange(t.GenericInstances);
            }

            var sb = new StringBuilder();
            foreach (var t in toEmit)
            {
                var csName = CsName(t.FullName());
                if (!_done.Add(csName)) continue;
                switch (t)
                {
                    case IClassNode cls: sb.Append(LowerClass(cls)); break;
                    case EnumNode enm: sb.Append(LowerEnum(enm)); break;
                }
            }
            return sb.ToString();
        }

        private string CsName(string fullName) => _emitter.Mangler.Mangle(CSharpEmitter.Normalize(fullName));

        private string LowerClass(IClassNode cls)
        {
            var name = CsName(cls.FullName());
            var sb = new StringBuilder();
            sb.AppendLine($"public sealed class {name} : BabyPenguin.CSharpBackend.Runtime.IHasMeta");
            sb.AppendLine("{");
            sb.AppendLine($"    public static readonly BabyPenguin.CSharpBackend.Runtime.Meta META = new(\"{cls.FullName()}\", System.Array.Empty<BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry>());");
            sb.AppendLine("    public BabyPenguin.CSharpBackend.Runtime.Meta __meta => META;");
            foreach (var field in cls.Symbols.Where(s => s.IsVariable && !s.IsFunction))
            {
                var ft = IRTypeClassifier.ToIrType(field.TypeInfo);
                // Queue referenced class/enum field types for lowering.
                var inner = CSharpEmitter.InnerTypeName(ft);
                if (inner != null) _work.Enqueue(inner);
                sb.AppendLine($"    public {_emitter.CsType(ft)} {_emitter.Mangler.Mangle(field.Name)};");
            }
            // __ExternImpl holds the C# backing object for collection externs (List/Queue/StringBuilder).
            if (cls.FullName().Contains("__ExternImpl"))
                sb.AppendLine("    public object? __backing;");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string LowerEnum(EnumNode enm)
        {
            var name = CsName(enm.FullName());
            var sb = new StringBuilder();
            sb.AppendLine($"public struct {name} : BabyPenguin.CSharpBackend.Runtime.IHasMeta");
            sb.AppendLine("{");
            sb.AppendLine($"    public static readonly BabyPenguin.CSharpBackend.Runtime.Meta META = new(\"{enm.FullName()}\", System.Array.Empty<BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry>());");
            sb.AppendLine("    public BabyPenguin.CSharpBackend.Runtime.Meta __meta => META;");
            sb.AppendLine("    public int _value;             // variant tag (matches IR member name)");
            sb.AppendLine("    public object _containing_value; // variant payload (matches IR member name)");
            // enum -> string yields the variant NAME (matches interpreter StaticToString), not the C# type name.
            // TokenStream.match/expect compare TokenType cast to string, so this must distinguish variants.
            sb.Append($"    public static string __ToName({name} v) {{ switch (v._value) {{");
            foreach (var d in enm.EnumDeclarations)
                sb.Append($" case {d.Value}: return \"{d.Name}\";");
            sb.Append(" default: return v._value.ToString(); } }");
            sb.AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
