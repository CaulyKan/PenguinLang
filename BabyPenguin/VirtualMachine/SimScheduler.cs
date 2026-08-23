using BabyPenguin.Symbol;

namespace BabyPenguin.VirtualMachine
{
    public class SimScheduler
    {
        public static SimScheduler Instance { get; } = new SimScheduler();

        private long _currentTick = 0;
        private readonly List<TimerEntry> _timers = new();
        private readonly Queue<IRuntimeValue> _readyQueue = new();
        private BabyPenguinVM? _vm;
        private bool _exited;

        public long CurrentTick => _currentTick;

        /// <summary>Monotonic delta-round counter (exposed to Penguin via
        /// __builtin._sim_delta — wires merge same-round writes).</summary>
        public long CurrentRound { get; private set; }

        /// <summary>SimActivityCounter snapshot at the start of the round
        /// currently executing.</summary>
        public long RoundStartActivity { get; private set; }

        /// <summary>True when the most recently COMPLETED round carried no
        /// transaction activity (exposed via __builtin._sim_settled together
        /// with RoundStartActivity — a settle-point port read parks until
        /// propagation at the current time is done).</summary>
        public bool LastRoundQuiet { get; private set; }

        private class TimerEntry : IComparable<TimerEntry>
        {
            public long DeadlineTick { get; set; }
            public ReferenceRuntimeValue Future { get; set; } = null!;

            public int CompareTo(TimerEntry? other)
            {
                if (other == null) return 1;
                int cmp = DeadlineTick.CompareTo(other.DeadlineTick);
                return cmp != 0 ? cmp : Future.RefId.CompareTo(other.Future.RefId);
            }
        }

        public IEnumerable<ReferenceRuntimeValue> GetTimerFutures()
        {
            foreach (var entry in _timers)
                yield return entry.Future;
        }

        /// <summary>
        /// Jobs sitting in the ready queue have been removed from the
        /// Penguin-level pending_jobs queue and are only reachable from C#;
        /// expose them so the GC can treat them as roots.
        /// </summary>
        public IEnumerable<IRuntimeValue> GetReadyJobs()
        {
            foreach (var job in _readyQueue)
                yield return job;
        }

        public void EnqueueTimerFuture(long deadlineTick, ReferenceRuntimeValue future)
        {
            _timers.Add(new TimerEntry { DeadlineTick = deadlineTick, Future = future });
            _timers.Sort();
        }

        public void Run(BabyPenguinVM vm, RuntimeFrame? frame)
        {
            // The singleton persists across runs (e.g. multiple tests in one
            // process); reset all simulation state for a fresh execution.
            _vm = vm;
            _exited = false;
            _currentTick = 0;
            _timers.Clear();
            _readyQueue.Clear();
            CurrentRound = 0;
            RoundStartActivity = 0;
            LastRoundQuiet = false;

            bool dbg = Environment.GetEnvironmentVariable("PENGUIN_SIM_DEBUG") == "1";
            int dbgRounds = 0;
            const long MaxRounds = 20_000_000;
            // State snapshot of the previous signal-free round (null = none).
            // Quiescence = two consecutive signal-free rounds (no progress, no
            // timers, no transaction activity) with IDENTICAL job snapshots:
            // parked frames replay the same instruction pointer + register
            // contents, so an unchanged fingerprint means no forward motion is
            // possible. Any real advancement — bare-wait handoffs, yields,
            // counters, user code — mutates observable state and changes the
            // fingerprint.
            string? lastIdleSnapshot = null;

            while (true)
            {
                // Each round starts by draining pending_jobs (initial routines plus
                // any jobs enqueued at runtime by `async` expressions / do_wait).
                DrainPendingJobs(vm);

                // Nothing to do -> program finished
                if (_readyQueue.Count == 0 && _timers.Count == 0 && GetPendingJobCount(vm) == 0)
                    break;

                dbgRounds++;
                if (dbgRounds > MaxRounds)
                    throw new BabyPenguinRuntimeException("SimScheduler exceeded max rounds (possible live-lock)", code: ErrorCode.E_RUNTIME_INVALID_OP);

                if (dbg)
                {
                    if (dbgRounds <= 200)
                        Console.Error.WriteLine($"[SIM] round={dbgRounds} tick={_currentTick} ready={_readyQueue.Count} pending={GetPendingJobCount(vm)} timers={_timers.Count} activity={vm.Global.SimActivityCounter} snap={BuildPendingSnapshot(vm)}");
                }

                long activityAtRoundStart = vm.Global.SimActivityCounter;
                CurrentRound = dbgRounds;
                RoundStartActivity = activityAtRoundStart;

                bool progress = false;

                // --- Delta round: run every job currently ready ---
                while (_readyQueue.Count > 0)
                {
                    var job = _readyQueue.Dequeue();
                    if (job is not ReferenceRuntimeValue jobObj) continue;

                    if (RunJob(vm, frame, jobObj)) progress = true;
                    if (_exited) return;
                }

                // Fire timers whose deadline has been reached
                progress |= FireTimers();

                // No transaction flowed in this round → propagation at the
                // current time is settling (settle-point readers check this).
                LastRoundQuiet = vm.Global.SimActivityCounter == activityAtRoundStart;

                if (progress)
                {
                    lastIdleSnapshot = null;
                    continue;
                }

                // --- No progress this round ---
                if (_timers.Count > 0)
                {
                    // Advance tick to the earliest pending timer and fire it
                    lastIdleSnapshot = null;
                    _currentTick = Math.Max(_currentTick, _timers[0].DeadlineTick);
                    FireTimers();
                    continue;
                }

                // No timers, no progress, pending jobs remain. Fingerprint
                // every parked job (suspension point + register contents); two
                // consecutive signal-free rounds with the identical fingerprint
                // is quiescence — the next round would replay this one exactly
                // (deterministic cooperative scheduling). The program ends
                // normally: v1 has no external event sources, so "waiting for
                // external input" and "deadlocked on a full/empty channel" are
                // indistinguishable from quiescence. (The epoll integration
                // changes this: external deltas reset activity.)
                if (GetPendingJobCount(vm) > 0 || _readyQueue.Count > 0)
                {
                    bool transactionFlowed = vm.Global.SimActivityCounter != activityAtRoundStart;
                    string snapshot = BuildPendingSnapshot(vm);

                    if (transactionFlowed || snapshot != lastIdleSnapshot)
                    {
                        lastIdleSnapshot = snapshot;
                        continue;
                    }

                    if (dbg)
                        Console.Error.WriteLine($"[SIM] quiescent at round={dbgRounds} tick={_currentTick} pending={GetPendingJobCount(vm)}");
                }

                break;
            }
        }

        /// <summary>
        /// Fingerprint every parked job: object identity, routine state and the
        /// suspended frame's execution snapshot. Order-sensitive (queue
        /// position is part of the scheduler state).
        /// </summary>
        private string BuildPendingSnapshot(BabyPenguinVM vm)
        {
            var sb = new System.Text.StringBuilder(256);
            var items = GetPendingJobItems(vm);
            if (items != null)
                foreach (var job in items)
                    AppendJobSnapshot(sb, job);
            foreach (var job in _readyQueue)
                AppendJobSnapshot(sb, job);
            return sb.ToString();
        }

        private static void AppendJobSnapshot(System.Text.StringBuilder sb, IRuntimeValue job)
        {
            sb.Append('#');
            if (job is not ReferenceRuntimeValue jobObj)
            {
                sb.Append(job.GetType().Name);
                return;
            }
            sb.Append(jobObj.RefId).Append(':');

            if (jobObj.Fields.TryGetValue("context", out var ctxVal) && ctxVal is ReferenceRuntimeValue ctx
                && ctx.Fields.TryGetValue("__impl", out var implVal) && implVal is ReferenceRuntimeValue impl
                && impl.ExternImplenmentationValue is RuntimeFrame f)
            {
                sb.Append(f.GetSnapshot());
            }
            else
            {
                sb.Append("no-frame");
            }
        }

        /// <summary>
        /// Drain all pending jobs from the PenguinLang Scheduler.pending_jobs queue
        /// into our readyQueue (FIFO preserved).
        /// </summary>
        private void DrainPendingJobs(BabyPenguinVM vm)
        {
            var items = GetPendingJobItems(vm);
            if (items == null) return;

            while (items.Count > 0)
            {
                var job = items[0];
                items.RemoveAt(0);
                _readyQueue.Enqueue(job);
            }
        }

        /// <summary>
        /// Re-enqueue a blocked job into the PenguinLang Scheduler.pending_jobs
        /// queue so FIFO order relative to other jobs is preserved.
        /// </summary>
        private static void EnqueuePendingJob(BabyPenguinVM vm, IRuntimeValue job)
        {
            var items = GetPendingJobItems(vm);
            if (items == null) return;
            items.Add(job);
        }

        private static int GetPendingJobCount(BabyPenguinVM vm)
        {
            return GetPendingJobItems(vm)?.Count ?? 0;
        }

        /// <summary>
        /// Run a single job by directly invoking its RoutineContext.call() extern,
        /// replicating the state machine in _DefaultRoutine.start():
        ///   state: pending(0) -> running(1)
        ///   status = context.call()
        ///   state: running(1) -> status
        /// A job whose status is Blocked (0) stays pending and is re-enqueued.
        /// Returns true if the job completed (finished) this round.
        /// </summary>
        private bool RunJob(BabyPenguinVM vm, RuntimeFrame? frame, ReferenceRuntimeValue jobObj)
        {
            var stateField = ReadField(jobObj, "state") as ReferenceRuntimeValue;
            if (stateField == null) return false;
            var stateBasic = ReadField(stateField, "value") as BasicRuntimeValue;
            if (stateBasic == null) return false;

            // pending(0) -> running(1); if not pending, skip
            if (stateBasic.I64Value != 0) return false;
            stateBasic.I64Value = 1;

            var contextField = ReadField(jobObj, "context") as ReferenceRuntimeValue;
            if (contextField == null) { stateBasic.I64Value = 3; return true; }

            var callFuncName = FindExternFuncName(vm, "RoutineContext", "call");
            var func = FindExternFunc(vm, callFuncName);
            if (func == null) { stateBasic.I64Value = 3; return true; }

            var resultSym = CreateI64Result(vm);
            var args = new List<IRuntimeValue> { contextField };
            foreach (var brk in func(frame, resultSym, args))
            {
                if (brk.Reason == RuntimeBreakReason.Exited)
                {
                    _exited = true;
                    return true;
                }
            }

            var status = resultSym.Value.As<BasicRuntimeValue>().I64Value;
            stateBasic.I64Value = status;

            // Blocked (0) maps back to pending -> run again next round
            if (status == 0)
                EnqueuePendingJob(vm, jobObj);

            return status == 3 || status == 4;
        }

        /// <summary>
        /// Remove all timers whose deadline has been reached.
        /// Returns true if any timer was fired.
        /// </summary>
        private bool FireTimers()
        {
            if (_timers.Count == 0) return false;

            bool fired = false;
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                if (_currentTick >= _timers[i].DeadlineTick)
                {
                    _timers.RemoveAt(i);
                    fired = true;
                }
            }
            return fired;
        }

        /// <summary>
        /// Get the backing List from the Scheduler.pending_jobs Queue.
        /// </summary>
        private static List<IRuntimeValue>? GetPendingJobItems(BabyPenguinVM vm)
        {
            if (!vm.Global.GlobalVariables.TryGetValue("__builtin._main_scheduler", out var sym))
                return null;
            if (sym.Value is not ReferenceRuntimeValue schedulerObj)
                return null;
            if (!schedulerObj.Fields.TryGetValue("pending_jobs", out var pjVal))
                return null;
            if (pjVal is not ReferenceRuntimeValue pendingJobs)
                return null;
            if (!pendingJobs.Fields.TryGetValue("__impl", out var implVal))
                return null;
            if (implVal is not ReferenceRuntimeValue implObj)
                return null;
            return implObj.ExternImplenmentationValue as List<IRuntimeValue>;
        }

        private static IRuntimeValue ReadField(ReferenceRuntimeValue obj, string fieldName)
        {
            return obj.Fields.TryGetValue(fieldName, out var val) ? val : null!;
        }

        private static Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>? FindExternFunc(BabyPenguinVM vm, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (vm.Global.ExternFunctions.TryGetValue(name, out var func)) return func;
            if (vm.Global.SanitizedExternFunctionIndex.TryGetValue(name, out func)) return func;
            return null;
        }

        private static string FindExternFuncName(BabyPenguinVM vm, string typeName, string methodName)
        {
            var suffix = "." + methodName;
            foreach (var key in vm.Global.ExternFunctions.Keys)
            {
                if (key.Contains(typeName) && key.EndsWith(suffix))
                    return key;
            }
            foreach (var key in vm.Global.SanitizedExternFunctionIndex.Keys)
            {
                if (key.Contains(typeName) && key.EndsWith(methodName))
                    return key;
            }
            return "";
        }

        private static IRuntimeSymbol CreateI64Result(BabyPenguinVM vm)
        {
            var i64Type = vm.Model.BasicTypeNodes.GetCachedImmutableType("i64")!;
            var fakeSymbol = new ExternResultSymbol(i64Type);
            return IRuntimeSymbol.FromSymbol(vm.Model, fakeSymbol, vm.Global);
        }

        private class ExternResultSymbol : ISymbol
        {
            private readonly IType _typeInfo;
            public ExternResultSymbol(IType typeInfo) { _typeInfo = typeInfo; }
            public string Name => "__extern_result__";
            public string OriginName => "__extern_result__";
            public ISymbolContainer Parent => null!;
            public IType TypeInfo => _typeInfo;
            public SourceLocation SourceLocation => SourceLocation.Empty();
            public bool IsLocal => false;
            public bool IsTemp => false;
            public bool IsParameter => false;
            public int ParameterIndex => 0;
            public bool IsClassMember => false;
            public bool IsStatic => false;
            public bool IsEnum => _typeInfo.IsEnumType;
            public bool IsFunction => false;
            public bool IsVariable => true;
            public Mutability IsMutable { get; set; } = Mutability.Mutable;
            public TypeInferStatus TypeInferStatus => TypeInferStatus.ExplicitTyped;
            public string FullName() => "__extern_result__";
        }
    }
}