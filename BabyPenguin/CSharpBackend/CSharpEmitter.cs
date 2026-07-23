using System.Collections.Generic;
using System.Text;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>Text/identifier helpers shared by the lowerers.</summary>
    public class CSharpEmitter
    {
        public NameMangler Mangler { get; }
        private readonly HashSet<string> _interfaceNames;
        public bool IsInterfaceType(string irType) =>
            _interfaceNames.Contains(Normalize(InnerTypeName(irType) ?? ""));
        public CSharpEmitter(NameMangler mangler, HashSet<string> interfaceNames)
        {
            Mangler = mangler;
            _interfaceNames = interfaceNames;
        }

        private static readonly Dictionary<string, string> PrimitiveCs = new()
        {
            ["bool"] = "bool", ["char"] = "char", ["void"] = "void",
            ["i8"] = "sbyte", ["i16"] = "short", ["i32"] = "int", ["i64"] = "long",
            ["u8"] = "byte", ["u16"] = "ushort", ["u32"] = "uint", ["u64"] = "ulong",
            ["f32"] = "float", ["f64"] = "double",
        };

        /// <summary>Strip Penguin mutability markers (!mut) so IR type names match semantic-model FullNames.</summary>
        public static string Normalize(string irType) => (irType ?? "").Replace("!mut ", "").Trim();

        /// <summary>Map an IR type string to a C# type name.</summary>
        public string CsType(string irType)
        {
            irType = Normalize(irType);
            if (PrimitiveCs.TryGetValue(irType, out var prim)) return prim;
            if (irType == "ref<string>" || irType == "string") return "string";
            if (irType == "funptr") return "System.Delegate";
            foreach (var prefix in new[] { "ref<", "struct<", "enum<" })
                if (irType.StartsWith(prefix) && irType.EndsWith(">"))
                {
                    var inner = irType.Substring(prefix.Length, irType.Length - prefix.Length - 1);
                    // Interface-typed values are the bare object reference (meta carries the vtable).
                    if (_interfaceNames.Contains(Normalize(inner))) return "object";
                    return Mangler.Mangle(inner);
                }
            return Mangler.Mangle(irType); // fallback: mangle the raw type token
        }

        /// <summary>The inner type FullName of a ref&lt;X&gt;/struct&lt;X&gt;/enum&lt;X&gt;, normalized; else null.</summary>
        public static string? InnerTypeName(string irType)
        {
            irType = Normalize(irType);
            foreach (var prefix in new[] { "ref<", "struct<", "enum<" })
                if (irType.StartsWith(prefix) && irType.EndsWith(">"))
                    return irType.Substring(prefix.Length, irType.Length - prefix.Length - 1);
            return null;
        }

        /// <summary>Mangle a (sanitized) IR name into a C# identifier, normalizing !mut first.
        /// Use everywhere a C# identifier is derived from an IR name so function defs, call sites,
        /// method refs, and externs all agree regardless of !mut markers in the source.</summary>
        public string MangleName(string name) => Mangler.Mangle(Normalize(name));

        /// <summary>Mangled C# name of an instance method, given the receiver's IR type and the method name.</summary>
        public string MethodCsName(string objIrType, string methodName)
        {
            var inner = InnerTypeName(objIrType) ?? Normalize(objIrType);
            var irName = (inner + "." + methodName).Replace(".", "_");
            return MangleName(irName);
        }

        /// <summary>Render an IR operand (register / constant / global / label) as a C# expression.</summary>
        public string Operand(IRValue v)
        {
            switch (v)
            {
                case IRNamedRegister nr: return $"r_{nr.Index}";
                case IRTempRegister tr: return $"r_{tr.Index}";
                case IRConstant c: return Literal(c.Value, c.IrType);
                case IRGlobalRef g: return $"G.{Mangler.Mangle(g.Name)}";
                case IRLabelValue l: return $"L_{Mangler.Mangle(l.Name)}";
                default: return $"/*unknown operand {v?.Display()}*/";
            }
        }

        public string Literal(string value, string irType)
        {
            switch (irType)
            {
                case "bool": return value == "true" ? "true" : "false";
                case "string":
                case "ref<string>":
                    return RenderStringLiteral(value);
                case "char":
                    return RenderCharLiteral(value);
                case "i8": return $"(sbyte)({value})";
                case "u8": return $"(byte)({value})";
                case "i16": return $"(short)({value})";
                case "u16": return $"(ushort)({value})";
                case "i32": return value;
                case "u32": return value + "U";
                case "i64": return value + "L";
                case "u64": return value + "UL";
                case "f32": return value + "f";
                case "f64": return EnsureDoubleLiteral(value);
                case "funptr": return $"/*funptr {value}*/null";
                default:
                    if (int.TryParse(value, out _)) return value;
                    return value;
            }
        }

        private static string RenderStringLiteral(string value)
        {
            // Penguin string constants are stored including surrounding quotes ("...") with
            // C#-compatible escapes; emit verbatim. If unquoted for some reason, wrap it.
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                return value;
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string RenderCharLiteral(string value)
        {
            if (value.Length >= 3 && value[0] == '\'' && value[^1] == '\'') return value;
            var ch = value.Length > 0 ? value[0] : '\0';
            return "'" + (ch == '\'' || ch == '\\' ? "\\" + ch : ch.ToString()) + "'";
        }

        private static string EnsureDoubleLiteral(string value)
        {
            // C# requires a digit before '.'/'e' for double literals.
            if (string.IsNullOrEmpty(value)) return "0.0";
            if (value[0] == '.') return "0" + value;
            return value;
        }

        public static string BinOp(string op) => op switch
        {
            "add" => "+", "sub" => "-", "mul" => "*", "div" => "/", "mod" => "%",
            "band" => "&", "bor" => "|", "bxor" => "^",
            "land" => "&&", "lor" => "||",
            "eq" => "==", "ne" => "!=", "lt" => "<", "gt" => ">", "le" => "<=", "ge" => ">=",
            _ => $"/*unknown binop {op}*/+"
        };

        public static string UnaryOp(string op) => op switch
        {
            "neg" => "-", "plus" => "+", "bnot" => "~", "lnot" => "!",
            _ => $"/*unknown unop {op}*/+"
        };
    }
}
