using System.Collections.Generic;
using System.Linq;
using BabyPenguin.Symbol;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>Info about one reached extern call, captured at the call site.</summary>
    public sealed class ExternInfo
    {
        public string Name = "";           // normalized sanitized extern name
        public FunctionSymbol? Symbol;      // the extern's semantic symbol (for collection dispatch)
        public string[] ArgIrTypes = System.Array.Empty<string>();
        public string RetIrType = "void";
    }

    /// <summary>
    /// Emits C# extern implementations compiled into the same assembly. Scalar externs route I/O/exit
    /// through the shared RuntimeGlobal (byte-identical to the interpreter). Collection externs
    /// (List/Queue/StringBuilder/AtomicI64) operate on a __impl.__backing field. Unknown externs get
    /// arity-correct stubs so the assembly compiles.
    /// </summary>
    public class ExternLowerer
    {
        private readonly CSharpEmitter _emitter;

        private static readonly Dictionary<string, (string[] Params, string Ret, string Body)> Table = new()
        {
            ["__builtin_print"] = (["string"], "void", "GlobalState.Global.Print(a_0);"),
            ["__builtin_println"] = (["string"], "void", "GlobalState.Global.Print(a_0, true);"),
            ["__builtin_eprint"] = (["string"], "void", "System.Console.Error.Write(a_0);"),
            ["__builtin_eprintln"] = (["string"], "void", "System.Console.Error.WriteLine(a_0);"),
            ["__builtin_exit"] = (["int"], "void", "GlobalState.Global.ExitCode = a_0; throw new BabyPenguin.VirtualMachine.ProgramExitException();"),
            ["__builtin_string_length"] = (["string"], "long", "return (long)a_0.Length;"),
            ["__builtin_string_find"] = (["string", "string"], "long", "int i = a_0.IndexOf(a_1); return i < 0 ? -1L : (long)i;"),
            ["__builtin_string_find_from"] = (["string", "string", "long"], "long", "int i = a_0.IndexOf(a_1, (int)a_2); return i < 0 ? -1L : (long)i;"),
            ["__builtin_string_substring"] = (["string", "long", "long"], "string", "{ int s=(int)a_1, l=(int)a_2; if (s<0) s=0; if (s+l>a_0.Length) l=a_0.Length-s; if (s>=a_0.Length || l<0) return \"\"; return a_0.Substring(s, l); }"),
            ["__builtin_string_char_at"] = (["string", "long"], "string", "return a_0[(int)a_1].ToString();"),
            ["__builtin_string_char_code"] = (["string"], "long", "return a_0.Length == 0 ? -1L : (long)a_0[0];"),
            ["__builtin_string_to_int"] = (["string"], "long", "return long.TryParse(a_0, out var v) ? v : 0L;"),
            ["__builtin_lshift"] = (["long", "long"], "long", "return a_0 << (int)a_1;"),
            ["__builtin_rshift"] = (["long", "long"], "long", "return a_0 >> (int)a_1;"),
            ["__builtin___args_count"] = ([], "long", "return (long)GlobalState.Args.Length;"),
            ["__builtin___args_get"] = (["long"], "string", "int i = (int)a_0; return (i >= 0 && i < GlobalState.Args.Length) ? GlobalState.Args[i] : \"\";"),
            ["_utils_file_read_text"] = (["string"], "string", "return System.IO.File.ReadAllText(a_0);"),
            ["_utils_file_write_text"] = (["string", "string"], "void", "System.IO.File.WriteAllText(a_0, a_1);"),
            ["_utils_file_size"] = (["string"], "long", "try { return new System.IO.FileInfo(a_0).Length; } catch { return -1L; }"),
            ["_utils_file_read_range"] = (["string", "long", "long"], "string", "{ long off=a_1, sz=a_2; if (off<0||sz<0) return \"\"; try { using var fs=System.IO.File.OpenRead(a_0); fs.Seek(off, System.IO.SeekOrigin.Begin); var buf=new byte[sz]; int n=fs.Read(buf,0,(int)sz); return System.Text.Encoding.UTF8.GetString(buf,0,n); } catch { return \"\"; } }"),
            ["_utils_file_append"] = (["string", "string"], "void", "try { System.IO.File.AppendAllText(a_0, a_1); } catch { }"),
            ["_utils_exe_path"] = ([], "string", "return System.Environment.ProcessPath ?? \"\";"),
            ["_utils_mkdir"] = (["string"], "bool", "System.IO.Directory.CreateDirectory(a_0); return true;"),
            ["_utils_file_exists"] = (["string"], "bool", "return System.IO.File.Exists(a_0);"),
            ["_utils_dir_exists"] = (["string"], "bool", "return System.IO.Directory.Exists(a_0);"),
            ["_utils_dir_get_entries"] = (["string"], "string", "return string.Join(\"\\n\", System.IO.Directory.GetFileSystemEntries(a_0));"),
            ["_utils_create_temp_dir"] = (["string"], "string", "var d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), a_0 + System.Guid.NewGuid().ToString(\"N\").Substring(0, 8)); System.IO.Directory.CreateDirectory(d); return d;"),
            ["__builtin__exec_cmd"] = (["string"], "long", "return GlobalState.ExecCmd(a_0);"),
        };

        public ExternLowerer(CSharpEmitter emitter) { _emitter = emitter; }

        public IEnumerable<string> LowerExterns(IEnumerable<ExternInfo> externs)
        {
            foreach (var info in externs.GroupBy(e => e.Name).Select(g => g.First()))
            {
                var csName = _emitter.MangleName(info.Name);
                if (Table.TryGetValue(info.Name, out var entry))
                {
                    var parms = string.Join(", ", entry.Params.Select((t, i) => $"{t} a_{i}"));
                    yield return entry.Ret == "void"
                        ? $"public static void {csName}({parms}) {{ {entry.Body} }}"
                        : $"public static {entry.Ret} {csName}({parms}) {{ {entry.Body} }}";
                    continue;
                }

                var body = CollectionBody(info);
                if (body != null) { yield return body; continue; }

                // Unknown extern: arity-correct stub so the assembly compiles.
                var ps = string.Join(", ", info.ArgIrTypes.Select((t, i) => $"{_emitter.CsType(t)} a_{i}"));
                var rt = _emitter.CsType(info.RetIrType);
                if (rt == "void")
                    yield return $"public static void {csName}({ps}) {{ }}";
                else
                    yield return $"public static {rt} {csName}({ps}) {{ return default; }}";
            }
        }

        /// <summary>Returns the C# method text for a collection extern (List/Queue/StringBuilder/AtomicI64/ICopy), or null if not one.</summary>
        private string? CollectionBody(ExternInfo info)
        {
            var sym = info.Symbol;
            var parent = sym?.Parent?.FullName();
            if (parent == null) return null;
            var kind = parent.Contains("__ExternImpl") ? null
                : parent.StartsWith("_utils.List<") ? "list"
                : parent.StartsWith("_utils.Queue<") ? "queue"
                : parent.StartsWith("__builtin.StringBuilder") ? "sb"
                : parent.StartsWith("__builtin.AtomicI64") ? "atomic"
                : parent.StartsWith("__builtin.ICopy<") ? "icopy"
                : null;
            if (kind == null) return null;

            var op = sym!.Name;
            var csName = _emitter.MangleName(info.Name);
            // ICopy<T>.copy is invoked through the ICopy<T> interface, so the call-site receiver is
            // object. Take/return object and clone internally; the call site casts the result.
            string ps;
            string rt;
            if (kind == "icopy")
            {
                ps = "object a_0";
                rt = "object";
            }
            else
            {
                ps = string.Join(", ", info.ArgIrTypes.Select((t, i) => $"{_emitter.CsType(t)} a_{i}"));
                rt = _emitter.CsType(info.RetIrType);
            }
            string body = (kind, op) switch
            {
                ("list", "new") => "a_0.__impl = new __builtin___ExternImpl(); a_0.__impl.__backing = new System.Collections.Generic.List<object>();",
                ("list", "push") => "((System.Collections.Generic.List<object>)a_0.__impl.__backing).Add(a_1);",
                ("list", "at") => OptionReturn(info, "var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; int i=(int)a_1; if (i>=0 && i<L.Count){__r._value=0; __r._containing_value=L[i];} else __r._value=1;"),
                ("list", "pop") => OptionReturn(info, "var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; if (L.Count>0){var v=L[L.Count-1]; L.RemoveAt(L.Count-1); __r._value=0; __r._containing_value=v;} else __r._value=1;"),
                ("list", "remove") => "{ var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; int i=(int)a_1; if (i>=0 && i<L.Count) L.RemoveAt(i); }",
                ("list", "size") => "return (ulong)((System.Collections.Generic.List<object>)a_0.__impl.__backing).Count;",
                ("list", "set") => "{ var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; int i=(int)a_1; if (i>=0 && i<L.Count) L[i]=a_2; }",
                ("queue", "new") => "a_0.__impl = new __builtin___ExternImpl(); a_0.__impl.__backing = new System.Collections.Generic.List<object>();",
                ("queue", "enqueue") => "((System.Collections.Generic.List<object>)a_0.__impl.__backing).Add(a_1);",
                ("queue", "dequeue") => OptionReturn(info, "var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; if (L.Count>0){var v=L[0]; L.RemoveAt(0); __r._value=0; __r._containing_value=v;} else __r._value=1;"),
                ("queue", "peek") => OptionReturn(info, "var L=(System.Collections.Generic.List<object>)a_0.__impl.__backing; if (L.Count>0){__r._value=0; __r._containing_value=L[0];} else __r._value=1;"),
                ("queue", "size") => "return (ulong)((System.Collections.Generic.List<object>)a_0.__impl.__backing).Count;",
                ("sb", "new") => "a_0.__impl = new __builtin___ExternImpl(); a_0.__impl.__backing = new System.Text.StringBuilder();",
                ("sb", "append") => "((System.Text.StringBuilder)a_0.__impl.__backing).Append(a_1);",
                ("sb", "to_string") => "return ((System.Text.StringBuilder)a_0.__impl.__backing).ToString();",
                ("atomic", "swap") => "return System.Threading.Interlocked.Exchange(ref a_0.value, a_1);",
                ("atomic", "compare_exchange") => "return System.Threading.Interlocked.CompareExchange(ref a_0.value, a_2, a_1);",
                ("atomic", "fetch_add") => "return System.Threading.Interlocked.Add(ref a_0.value, a_1);",
                ("icopy", "copy") => ICopyBody(info),
                _ => $"throw new System.NotImplementedException(\"extern not implemented in C# backend: {info.Name}\");"
            };
            return rt == "void"
                ? $"public static void {csName}({ps}) {{ {body}; }}"
                : $"public static {rt} {csName}({ps}) {{ {body}; }}";
        }

        /// <summary>Wraps an option-returning body that fills __r (the Option&lt;T&gt; result).</summary>
        private string OptionReturn(ExternInfo info, string fillBody)
        {
            var rt = _emitter.CsType(info.RetIrType);
            return $"var __r = new {rt}(); {fillBody}; return __r;";
        }

        private static readonly HashSet<string> CsPrimitives = new()
        { "bool", "char", "sbyte", "short", "int", "long", "byte", "ushort", "uint", "ulong", "float", "double", "string" };

        /// <summary>ICopy&lt;T&gt;.copy: memberwise clone via the runtime helper (primitives/strings are
        /// returned as-is by Clone).</summary>
        private string ICopyBody(ExternInfo info) => "return GlobalState.Clone(a_0);";
    }
}
