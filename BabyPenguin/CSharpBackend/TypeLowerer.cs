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
            // Value classes (explicit or auto IValueType) carry the marker so the runtime
            // value-semantics copier can clone them at enum-payload/container insertions.
            var bases = "BabyPenguin.CSharpBackend.Runtime.IHasMeta"
                + (IRTypeClassifier.IsValueClassIncludingAuto(cls) ? ", BabyPenguin.CSharpBackend.Runtime.IValueSemantics" : "");
            sb.AppendLine($"public sealed class {name} : {bases}");
            sb.AppendLine("{");
            // Build interface map entries so Meta.Is() can check interface implementation
            var ifaceEntries = new List<string>();
            foreach (var vt in cls.VTables)
            {
                var ifaceId = vt.Interface.FullName();
                if (ifaceId.StartsWith("!mut ")) ifaceId = ifaceId[5..];
                ifaceEntries.Add($"new BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry(\"{ifaceId}\", System.Array.Empty<System.Delegate>())");
            }
            var ifaceArray = ifaceEntries.Count > 0
                ? $"new BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry[] {{ {string.Join(", ", ifaceEntries)} }}"
                : "System.Array.Empty<BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry>()";
            var typeName = cls.FullName();
            if (typeName.StartsWith("!mut ")) typeName = typeName[5..];
            else if (typeName.StartsWith("mut ")) typeName = typeName[4..];
            sb.AppendLine($"    public static readonly BabyPenguin.CSharpBackend.Runtime.Meta META = new(\"{typeName}\", {ifaceArray});");
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
            // Build interface map entries so Meta.Is() can check interface implementation
            var ifaceEntries = new List<string>();
            foreach (var vt in enm.VTables)
            {
                var ifaceId = vt.Interface.FullName();
                if (ifaceId.StartsWith("!mut ")) ifaceId = ifaceId[5..];
                ifaceEntries.Add($"new BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry(\"{ifaceId}\", System.Array.Empty<System.Delegate>())");
            }
            var ifaceArray = ifaceEntries.Count > 0
                ? $"new BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry[] {{ {string.Join(", ", ifaceEntries)} }}"
                : "System.Array.Empty<BabyPenguin.CSharpBackend.Runtime.InterfaceMapEntry>()";
            var typeName = enm.FullName();
            if (typeName.StartsWith("!mut ")) typeName = typeName[5..];
            else if (typeName.StartsWith("mut ")) typeName = typeName[4..];
            sb.AppendLine($"    public static readonly BabyPenguin.CSharpBackend.Runtime.Meta META = new(\"{typeName}\", {ifaceArray});");
            sb.AppendLine("    public BabyPenguin.CSharpBackend.Runtime.Meta __meta => META;");
            sb.AppendLine("    public int _value;             // variant tag (matches IR member name)");
            sb.AppendLine("    public object _containing_value; // variant payload (matches IR member name)");
            // enum -> string yields the variant INDEX (matches the EmperorPenguin
            // LLVM backend and the cross-compiler tests, e.g. cast<string>(Color.red)
            // -> "0"). The interpreter's StaticToString yields the name, but the
            // test suite (and the native compilers) expect the index. TokenStream
            // match/expect compare TokenType via cast-to-string; index comparison
            // distinguishes variants just as well (distinct variants have distinct
            // declaration indexes), so this is safe for the pass1 bootstrap.
            sb.AppendLine($"    public static string __ToName({name} v) => v._value.ToString();");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
