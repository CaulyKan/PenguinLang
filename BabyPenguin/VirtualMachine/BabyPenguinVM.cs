using System.Collections.Generic;

namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal && !s.IsClassMember))
            {
                Global.GlobalVariables.Add(symbol.FullName(), IRuntimeSymbol.FromSymbol(model, symbol, Global));
            }

            ExternFunctions.Build(this);
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new();

        public RuntimeFrame? StartFrame { get; private set; }

        public string CollectOutput() => Global.Output.ToString();

        public void Initialize()
        {
            // Generate register-based IR from the semantic model
            var generator = new IRGenerator(Model);
            Global.IRModule = generator.Generate();

            // Build CodeContainer index for O(1) lookup (eliminates 38% profile hotspot)
            BuildCodeContainerIndex();

            // Build sanitized extern function index for O(1) lookup
            BuildSanitizedExternFunctionIndex();

            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol
                ?? throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], null);
            StartFrame = frame;
        }

        private static string SanitizeName(string name) => name.Replace(".", "_");

        private void BuildCodeContainerIndex()
        {
            foreach (var node in Model.FindAll(n => n is SemanticInterface.ICodeContainer))
            {
                var cc = (SemanticInterface.ICodeContainer)node;
                var sanitized = SanitizeName(cc.FullName());
                Global.CodeContainerIndex[sanitized] = cc;
            }
        }

        private void BuildSanitizedExternFunctionIndex()
        {
            foreach (var kvp in Global.ExternFunctions)
            {
                var sanitized = SanitizeName(kvp.Key);
                Global.SanitizedExternFunctionIndex[sanitized] = kvp.Value;
            }
        }

        public int Run()
        {
            if (StartFrame == null)
                Initialize();
            try
            {
                // Fast path: direct execution without yield/iterator overhead
                if (Global.Breakpoints.Count == 0 && Global.StepMode == RuntimeGlobal.StepModeEnum.Run)
                {
                    StartFrame!.RunDirect();
                }
                else
                {
                    // Debug path: use yield-based execution for DAP support
                    foreach (var result in StartFrame!.Run())
                    {
                        if (result.IsLeft)
                        {
                            if (result.Left!.Reason == RuntimeBreakReason.Exited)
                            {
                                return Global.ExitCode;
                            }
                        }
                    }
                }
            }
            catch (ProgramExitException)
            {
                return Global.ExitCode;
            }
            return 0;
        }

        public bool InsertBreakPoint(SourceLocation location)
        {
            // Store breakpoint in global for the new RuntimeFrame to check
            Global.Breakpoints.Add(location);
            return true;
        }

        public bool RemoveBreakPoint(SourceLocation location)
        {
            return Global.Breakpoints.Remove(location);
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public enum StepModeEnum { StepIn, StepOver, StepOut, Run }

        public Dictionary<ulong, ReferenceRuntimeValue> AllObjects { get; } = [];

        private ulong _refIdCounter = 0;
        public ulong NextRefId() => ++_refIdCounter;

        public void ClearAllObjects() => AllObjects.Clear();

        // === Object Pool ===
        // Pool of recycled ReferenceRuntimeValue objects to avoid repeated allocation.
        // When BabyPenguin GC sweeps dead objects, they go into the pool.
        // When NEW instruction creates objects, it takes from the pool first.
        private readonly Stack<ReferenceRuntimeValue> _objectPool = new();
        private int _objectPoolHits = 0;
        private int _objectPoolMisses = 0;

        /// <summary>
        /// Take a recycled ReferenceRuntimeValue from the pool, or return null if pool is empty.
        /// The caller must re-initialize the object's fields and type info.
        /// </summary>
        public ReferenceRuntimeValue? TryTakeFromPool()
        {
            if (_objectPool.TryPop(out var obj))
            {
                _objectPoolHits++;
                return obj;
            }
            _objectPoolMisses++;
            return null;
        }

        /// <summary>
        /// Return a dead ReferenceRuntimeValue to the pool for reuse.
        /// Clears its fields to release references to other objects.
        /// </summary>
        public void ReturnToPool(ReferenceRuntimeValue obj)
        {
            if (_objectPool.Count < 100_000) // Cap pool size
            {
                obj.Fields.Clear();
                obj.ExternImplenmentationValue = null;
                _objectPool.Push(obj);
            }
        }

        public void ClearPool()
        {
            _objectPool.Clear();
            _objectPoolHits = 0;
            _objectPoolMisses = 0;
        }

        // === Garbage Collector ===
        /// <summary>
        /// GC runs when AllObjects.Count exceeds this threshold.
        /// After each collection, the threshold is adjusted to 1.5× the live set (min 50k).
        /// </summary>
        public int GCThreshold { get; set; } = 50000;

        /// <summary>
        /// Global instruction counter — shared across all frames.
        /// Ensures GC is checked based on total instructions, not per-frame.
        /// </summary>
        public long GlobalInstructionCount { get; set; } = 0;

        /// <summary>
        /// Number of instructions between GC checks in the execution loop.
        /// Uses the global counter so GC triggers even during deep recursion.
        /// </summary>
        public int GCCheckInterval { get; set; } = 1000;

        /// <summary>
        /// Enable/disable GC. Disabled by default for small programs and debugging.
        /// </summary>
        public bool GCEnabled { get; set; } = false;

        /// <summary>
        /// If set, GC stats are written to this file path after each cycle.
        /// </summary>
        public string? GCStatsFile { get; set; } = null;

        /// <summary>
        /// Total number of objects collected across all GC cycles (for diagnostics).
        /// </summary>
        public long TotalCollected { get; private set; }

        /// <summary>
        /// Number of GC cycles performed (for diagnostics).
        /// </summary>
        public int GCCycles { get; private set; }

        /// <summary>
        /// Mark-sweep garbage collector. Traverses from roots (globals + frame chain),
        /// marks all reachable ReferenceRuntimeValue objects, and sweeps unmarked ones.
        /// </summary>
        public void CollectGarbage(RuntimeFrame currentFrame)
        {
            var before = AllObjects.Count;

            // --- Mark phase ---
            var marked = new HashSet<ulong>();
            var stack = new Stack<IRuntimeValue>();

            // Root: global variables
            foreach (var sym in GlobalVariables.Values)
                if (sym.Value != null)
                    stack.Push(sym.Value);

            // Root: frame chain (current frame → parent → ... → root)
            var frame = currentFrame;
            while (frame != null)
            {
                foreach (var val in frame.GetAllValues())
                    if (val != null)
                        stack.Push(val);
                frame = frame.ParentFrame;
            }

            // Traverse reachable graph
            while (stack.Count > 0)
            {
                var val = stack.Pop();
                switch (val)
                {
                    case ReferenceRuntimeValue refVal:
                        if (marked.Add(refVal.RefId))
                        {
                            // Traverse all fields
                            foreach (var fv in refVal.Fields.Values)
                                if (fv != null)
                                    stack.Push(fv);
                            // Traverse extern containers (List<IRuntimeValue>, etc.)
                            if (refVal.ExternImplenmentationValue is System.Collections.IEnumerable seq
                                && refVal.ExternImplenmentationValue is not string
                                && refVal.ExternImplenmentationValue is not StringBuilder)
                            {
                                foreach (var item in seq)
                                    if (item is IRuntimeValue rv)
                                        stack.Push(rv);
                            }
                        }
                        break;
                    case EnumRuntimeValue enumVal:
                        if (enumVal.FieldsValue != null)
                            stack.Push(enumVal.FieldsValue);
                        if (enumVal.ContainingValue != null)
                            stack.Push(enumVal.ContainingValue);
                        break;
                    case FunctionRuntimeValue funcVal:
                        if (funcVal.Owner != null)
                            stack.Push(funcVal.Owner);
                        break;
                }
            }

            // --- Sweep phase ---
            var toRemove = new List<ulong>(AllObjects.Count - marked.Count);
            foreach (var kvp in AllObjects)
            {
                if (!marked.Contains(kvp.Key))
                {
                    ReturnToPool(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                AllObjects.Remove(id);

            // Adjust threshold: 1.5× live set, minimum 50k
            var liveCount = AllObjects.Count;
            GCThreshold = Math.Max(50000, (int)(liveCount * 1.5));

            var collected = before - liveCount;
            TotalCollected += collected;
            GCCycles++;

            if (EnableGcDebug)
            {
                if (EnableDebugPrint)
                {
                    DebugFunc($"[BabyPenguin GC] Cycle #{GCCycles}: {before} → {liveCount} objects (collected {collected}), new threshold={GCThreshold}\n");
                }

                if (GCStatsFile != null)
                {
                    try
                    {
                        System.IO.File.AppendAllText(GCStatsFile,
                            $"[BabyPenguin GC] #{GCCycles}: {before} → {liveCount} (collected {collected}), threshold={GCThreshold}\n");
                    }
                    catch { }
                }
            }

            // Force .NET GC compaction periodically to prevent heap fragmentation.
            // The BabyPenguin GC frees .NET objects (strings, dicts) but the .NET GC
            // may not compact the heap, causing RSS to grow despite stable live set.
            // We compact LOH explicitly + do full Gen2 compaction.
            if (GCCycles % 10 == 0)
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                System.GC.Collect(2, System.GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        }

        public int ExitCode { get; set; } = 0;

        public bool HasExited { get; set; } = false;

        public string[] CommandLineArgs { get; set; } = Array.Empty<string>();

        public StepModeEnum StepMode { get; set; } = StepModeEnum.Run;

        public Dictionary<string, IRuntimeSymbol> GlobalVariables { get; } = [];

        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> ExternFunctions { get; } = [];

        /// <summary>
        /// Pre-built index: sanitized name → ICodeContainer for O(1) lookup.
        /// Built once during VM initialization instead of traversing the entire semantic tree on every function call.
        /// </summary>
        public Dictionary<string, SemanticInterface.ICodeContainer> CodeContainerIndex { get; } = [];

        /// <summary>
        /// Pre-built index: sanitized name → extern function for O(1) lookup.
        /// Eliminates the fallback linear scan in FindExternFunction.
        /// </summary>
        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> SanitizedExternFunctionIndex { get; } = [];

        public IRModule? IRModule { get; set; }

        public HashSet<SourceLocation> Breakpoints { get; } = [];

        public void RegisterExternFunction(string name, Action<IRuntimeSymbol?, List<IRuntimeValue>> func)
        {
            ExternFunctions.Add(name, (frame, result, args) =>
            {
                func(result, args);
                return [];
            });
        }

        public void RegisterExternFunction(string name, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>> func)
        {
            ExternFunctions.Add(name, func);
        }

        public bool EnableDebugPrint { get; set; } = false;

        public bool EnableGcDebug { get; set; } = false;

        /// <summary>
        /// Enable DAP variable synchronization (LocalVariables construction + per-Store sync).
        /// Only set this during DAP debugging sessions — it is expensive and unnecessary for normal execution.
        /// </summary>
        public bool EnableVariableSync { get; set; } = false;

        public StringBuilder Output { get; } = new();

        public Action<string> PrintFunc { get; set; } = (s) => Console.Write(s);

        public Action<string> DebugFunc { get; set; } = (s) => Console.Write(s);

        public void Print(string s, bool newline = false)
        {
            if (!newline)
            {
                Output.Append(s);
                PrintFunc(s);
            }
            else
            {
                Output.AppendLine(s);
                PrintFunc(s + Environment.NewLine);
            }
        }
    }
}
