namespace BabyPenguin
{
    /// <summary>
    /// Direction metadata for RTL-style module ports. Ports are implemented as
    /// reference fields of type mut __builtin.IChannel&lt;T&gt; (so `wait port`,
    /// `current()`, `write()` all work through the channel interfaces); the
    /// direction recorded here drives the permission matrix (module code may
    /// only write outputs, outsiders may only read outputs, inputs are wired
    /// exclusively through connect) and connect-time checks.
    /// Keyed by "&lt;class full name&gt;.&lt;port name&gt;" — specializations register
    /// under their own full names.
    /// </summary>
    public record PortMeta(string ClassFullName, string PortName, bool IsInput, Type.IType PayloadType, bool HasDefault = false);

    public static class PortRegistry
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PortMeta> _ports = new();

        public static void Register(PortMeta meta) => _ports[$"{meta.ClassFullName}.{meta.PortName}"] = meta;

        public static PortMeta? Find(string classFullName, string portName)
            => _ports.TryGetValue($"{classFullName}.{portName}", out var meta) ? meta : null;

        /// <summary>All ports declared by a class (for the unconnected-input
        /// audit).</summary>
        public static System.Collections.Generic.IEnumerable<PortMeta> PortsOf(string classFullName)
            => System.Linq.Enumerable.Where(_ports.Values, p => p.ClassFullName == classFullName);

        /// <summary>Class-full-name set of classes that declare ports.</summary>
        public static bool ClassHasPorts(string classFullName) => System.Linq.Enumerable.Any(
            System.Linq.Enumerable.Where(_ports.Values, p => p.ClassFullName == classFullName));

        /// <summary>Test hook: reset all registries (port metadata, passthrough
        /// lines, output drivers) between in-process compilations.</summary>
        public static void Reset()
        {
            _ports.Clear();
            PassthroughRegistry.Reset();
            DriverRegistry.Reset();
        }
    }

    /// <summary>
    /// Input ports that a class-level construct passes through to an inner
    /// module (connect(this.x, inner.x)). The field holds a _LateSource relay
    /// created at construct time; an OUTER connect targeting such an input
    /// dispatches to relay.bind(source) instead of overwriting the field
    /// (topology-aware binding — the outer wire arrives after the class
    /// construct has already run). Keyed like PortRegistry; specializations
    /// register under their own full names.
    /// </summary>
    public static class PassthroughRegistry
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _passthrough = new();

        public static void Register(string classFullName, string portName)
            => _passthrough[$"{classFullName}.{portName}"] = true;

        public static bool Find(string classFullName, string portName)
            => _passthrough.TryGetValue($"{classFullName}.{portName}", out var v) && v;

        public static void Reset() => _passthrough.Clear();
    }

    /// <summary>
    /// Output-port driver bookkeeping for the static driver-uniqueness check
    /// (design Q9 / round-3 gap 3): an output has exactly one driver — module
    /// body code (assignments, write()/try_write() calls) XOR a construct
    /// passthrough wire (connect(inner.y, this.y)); and at most ONE driving
    /// routine. Keyed "&lt;class full name&gt;.&lt;port name&gt;".
    /// </summary>
    public static class DriverRegistry
    {
        public sealed class DriverRecord
        {
            public string? WireLocation { get; set; }
            public System.Collections.Generic.HashSet<string> BodyFunctions { get; } = [];
            public System.Collections.Generic.List<string> BodyLocations { get; } = [];
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DriverRecord> _drivers = new();

        public static void AddBodyDriver(string classFullName, string portName, string functionName, PenguinLangParser.SourceLocation location)
        {
            var record = _drivers.GetOrAdd($"{classFullName}.{portName}", _ => new DriverRecord());
            lock (record) { record.BodyFunctions.Add(functionName); record.BodyLocations.Add($"{functionName} ({location})"); }
        }

        public static void AddWireDriver(string classFullName, string portName, PenguinLangParser.SourceLocation location)
        {
            var record = _drivers.GetOrAdd($"{classFullName}.{portName}", _ => new DriverRecord());
            lock (record) { record.WireLocation ??= location.ToString(); }
        }

        public static System.Collections.Generic.IEnumerable<(string Key, DriverRecord Record)> All
            => System.Linq.Enumerable.Select(_drivers, kvp => (kvp.Key, kvp.Value));

        public static void Reset() => _drivers.Clear();
    }

    /// <summary>
    /// Post-pass validation of the static connect topology (design Q2/Q3/Q9):
    /// 1. an input port is connected by exactly one connect line per instance
    ///    (multi-driver inputs are a compile error);
    /// 2. every module instantiated in a construct has each of its inputs
    ///    connected in the same wiring pool or declared with a default;
    /// 3. an output port has exactly one driver — a construct passthrough wire
    ///    XOR module body code, and at most one driving routine.
    /// Construct functions are pooled per scope: all top-level constructs of
    /// one namespace share a pool (their lets hoist to that namespace), and
    /// all class constructs of one class share the class's pool.
    /// </summary>
    public static class PortTopologyValidator
    {
        public static void Validate(SemanticModel model)
        {
            // ---- 3. output driver uniqueness (global registry) ----
            foreach (var (key, record) in DriverRegistry.All)
            {
                var hasBody = record.BodyFunctions.Count > 0;
                if (record.WireLocation != null && hasBody)
                    throw new BabyPenguinException(
                        $"Output port '{key}' has two drivers: module body code ({string.Join(", ", record.BodyLocations)}) and a construct wire ({record.WireLocation}) — drive it from exactly one (body XOR connect line); merge multiple streams through a channel",
                        null, code: PenguinLangParser.ErrorCode.E_WIRING);
                if (record.BodyFunctions.Count > 1)
                    throw new BabyPenguinException(
                        $"Output port '{key}' is driven by {record.BodyFunctions.Count} different routines ({string.Join(", ", record.BodyFunctions)}) — an output has exactly one driving routine; merge multiple streams through a channel",
                        null, code: PenguinLangParser.ErrorCode.E_WIRING);
            }

            // ---- 1+2. pool construct functions, audit sinks & instantiations ----
            var pools = new System.Collections.Generic.Dictionary<string, List<SemanticNode.IFunction>>();
            foreach (var node in model.FindAll(n => n is SemanticNode.IFunction))
            {
                var func = (SemanticNode.IFunction)node;
                var isTopConstruct = func.Name.StartsWith(SemanticPass.SemanticScopingPass.ConstructPrefix) && func.Parent is SemanticNode.INamespace;
                var isClassConstruct = func.Name.StartsWith(SemanticPass.SemanticScopingPass.ClassConstructPrefix) && func.Parent is SemanticInterface.ITypeNode;
                if (!isTopConstruct && !isClassConstruct) continue;

                var poolId = isClassConstruct ? "class:" + func.Parent!.FullName() : "top:" + func.Parent!.FullName();
                if (!pools.TryGetValue(poolId, out var list))
                    pools[poolId] = list = [];
                list.Add(func);
            }

            foreach (var (_, functions) in pools)
            {
                var sinkCounts = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var func in functions)
                {
                    foreach (var (sinkKey, location) in func.CodeContainerData.ConnectedInputSinks)
                    {
                        if (System.Environment.GetEnvironmentVariable("PENGUIN_TOPO_DEBUG") == "1")
                            System.Console.Error.WriteLine($"[TOPO] pool sink '{sinkKey}' at {location}");
                        if (sinkCounts.TryGetValue(sinkKey, out var firstAt))
                            throw new BabyPenguinException(
                                $"Input port '{sinkKey}' is connected twice (first at {firstAt}, again at {location}) — an input has exactly one driver",
                                location, code: PenguinLangParser.ErrorCode.E_WIRING);
                        sinkCounts[sinkKey] = location.ToString();
                    }
                }

                foreach (var func in functions)
                {
                    foreach (var (symbol, classFullName, location) in func.CodeContainerData.NewedModules)
                    {
                        if (System.Environment.GetEnvironmentVariable("PENGUIN_TOPO_DEBUG") == "1")
                            System.Console.Error.WriteLine($"[TOPO] newed '{symbol.FullName()}' class={classFullName}");
                        foreach (var port in PortRegistry.PortsOf(classFullName))
                        {
                            if (!port.IsInput || port.HasDefault) continue;
                            var sinkKey = symbol.FullName() + "." + port.PortName;
                            if (!sinkCounts.ContainsKey(sinkKey))
                                throw new BabyPenguinException(
                                    $"Input port '{port.PortName}' of module '{symbol.Name}' ({classFullName}) is never connected — connect it in a construct block or declare a default ('input {port.PortName} : {port.PayloadType.FullName()} = <default>')",
                                    location, code: PenguinLangParser.ErrorCode.E_WIRING);
                        }
                    }
                }
            }
        }
    }
}
