using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend.Runtime
{
    /// <summary>
    /// Shared mutable state handed from the BabyPenguin host to the compiled program.
    /// The compiled code reads/writes this so I/O, args, and exit are byte-identical
    /// to the interpreter (which uses the same RuntimeGlobal instance).
    /// </summary>
    public static class GlobalState
    {
        public static RuntimeGlobal? Global;
        public static string[] Args = System.Array.Empty<string>();

        /// <summary>
        /// Shallow memberwise clone (new instance, all fields copied). Used by lowered ICopy&lt;T&gt;.copy
        /// for value types so a copy is independent of its source. Reference-typed fields are shared
        /// (shallow), matching Penguin value semantics.
        /// </summary>
        public static object? Clone(object? o)
        {
            if (o == null) return null;
            var t = o.GetType();
            if (t == typeof(string) || t.IsValueType) return o; // immutable / value — no clone needed
            var clone = System.Activator.CreateInstance(t);
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var f in t.GetFields(flags))
                f.SetValue(clone, f.GetValue(o));
            return clone;
        }

        /// <summary>
        /// Value-semantics copy for enum payloads / container slots (mirrors the VM's
        /// RuntimeValueCopier and EmperorPenguin's inline struct layout): instances of
        /// value classes (marked IValueSemantics at lowering time) are copied memberwise,
        /// recursing into value-class fields; everything else — reference-class instances,
        /// strings, boxed primitives — is shared, like a copied pointer.
        /// </summary>
        public static object? CopyValueSemantics(object? o)
            => CopyValueSemantics(o, new System.Collections.Generic.Dictionary<object, object>());

        private static object? CopyValueSemantics(object? o, System.Collections.Generic.Dictionary<object, object> visited)
        {
            if (o == null) return null;
            if (o is not IValueSemantics) return o;
            if (visited.TryGetValue(o, out var existing)) return existing;
            var t = o.GetType();
            var clone = System.Activator.CreateInstance(t);
            visited[o] = clone!;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var f in t.GetFields(flags))
                f.SetValue(clone, CopyValueSemantics(f.GetValue(o), visited));
            return clone;
        }

        // Interface virtual dispatch: (concrete runtime type, interface-method mangled name) -> impl MethodInfo.
        // Populated by the generated __InitVtables() from each class's VTables.
        private static System.Collections.Generic.Dictionary<(System.Type, string), System.Reflection.MethodInfo>? _vtable;

        public static void RegisterVtable(System.Type type, string ifaceMethodMangled, System.Reflection.MethodInfo impl)
        {
            _vtable ??= new System.Collections.Generic.Dictionary<(System.Type, string), System.Reflection.MethodInfo>();
            _vtable[(type, ifaceMethodMangled)] = impl;
        }

        /// <summary>Dispatch an interface method call to the concrete impl for obj's runtime type.</summary>
        public static object? InvokeVirtual(object? obj, string ifaceMethodMangled, params object?[] args)
        {
            if (obj == null) throw new System.NullReferenceException();
            _vtable ??= new System.Collections.Generic.Dictionary<(System.Type, string), System.Reflection.MethodInfo>();
            if (!_vtable.TryGetValue((obj.GetType(), ifaceMethodMangled), out var mi))
                throw new System.Exception($"cs backend: no vtable impl for {obj.GetType().Name}.{ifaceMethodMangled}");
            return mi.Invoke(null, args);
        }

        /// <summary>Run a shell command (matches the interpreter's __builtin._exec_cmd: sh -c "&lt;cmd&gt;", exit code).</summary>
        public static long ExecCmd(string cmd)
        {
            try
            {
                var si = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"" + cmd.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                };
                var p = System.Diagnostics.Process.Start(si);
                if (p == null) return -1;
                p.WaitForExit();
                return (long)p.ExitCode;
            }
            catch { return -1; }
        }
    }

    /// <summary>Implemented by every lowered class/struct so its metadata/vtable is reachable.</summary>
    public interface IHasMeta
    {
        Meta __meta { get; }
    }

    /// <summary>
    /// Marker implemented by lowered VALUE classes (explicit or auto IValueType). The runtime
    /// value-semantics copier (GlobalState.CopyValueSemantics) clones these memberwise (recursing
    /// into value-class fields) while sharing everything else, matching EmperorPenguin's inline
    /// struct layout and the VM's RuntimeValueCopier.
    /// </summary>
    public interface IValueSemantics
    {
    }

    /// <summary>
    /// Slim C# equivalent of EmperorPenguin's metaptr. Holds the type name and a
    /// per-interface method-pointer map for virtual dispatch / isinstance. (CLR GC
    /// manages object lifetime, so EmperorPenguin's GC field-offset arrays are absent.)
    /// </summary>
    public sealed class Meta
    {
        public string Name { get; }
        public InterfaceMapEntry[] Interfaces { get; }

        public Meta(string name, InterfaceMapEntry[]? ifaces)
        {
            Name = name;
            Interfaces = ifaces ?? System.Array.Empty<InterfaceMapEntry>();
        }

        public bool Is(string typeId)
        {
            if (typeId == Name) return true;
            foreach (var e in Interfaces)
                if (e.InterfaceId == typeId) return true;
            return false;
        }

        public Delegate? VTableLookup(string interfaceId, int slot)
        {
            foreach (var e in Interfaces)
                if (e.InterfaceId == interfaceId) return e.MethodTable[slot];
            return null;
        }
    }

    public sealed class InterfaceMapEntry
    {
        public string InterfaceId { get; }
        public Delegate[] MethodTable { get; }
        public InterfaceMapEntry(string id, Delegate[] mt) { InterfaceId = id; MethodTable = mt; }
    }

    /// <summary>
    /// One value yielded by a lowered coroutine iterator per scheduler tick.
    /// Status mirrors BabyPenguin's ReturnStatus: Blocked=0, YieldNotFinished=2, Finished=3, YieldFinished=4.
    /// </summary>
    public readonly record struct RoutineYield(int Status, object? Value)
    {
        public static readonly RoutineYield Blocked = new(0, null);
        public static RoutineYield Yielded(object? v) => new(2, v);
        public static RoutineYield Final(object? v) => new(4, v);
        public bool IsBlocked => Status == 0;
    }
}
