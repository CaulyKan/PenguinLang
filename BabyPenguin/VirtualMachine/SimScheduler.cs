using BabyPenguin.Symbol;

namespace BabyPenguin.VirtualMachine
{
    /// <summary>
    /// Which kind of park a Blocked return means. The Penguin-level code
    /// cannot express this through the status number alone (the VM's park
    /// instruction is a plain Blocked return), so the library helper that
    /// immediately precedes the `wait` sets a side-channel note on the
    /// scheduler and RunOneDeltaJob consumes it when filing the parked job:
    /// the note extern and the wait are adjacent on the same stack with
    /// nothing in between (single-threaded), so the pairing is unambiguous.
    /// </summary>
    public enum SchedParkNote
    {
        /// <summary>No note: a bare `wait;` / `wait 0;` — owed the next
        /// delta at the CURRENT tick (the clock may not jump past it).</summary>
        None,

        /// <summary>`wait -1;` — pure yield: still runnable in the CURRENT
        /// delta (re-queued at the tail; the magnitude is meaningless).</summary>
        PureYield,

        /// <summary>Condition park (do_wait / Fifo backpressure): owed
        /// nothing; woken only by a kick (delivery, completion, timer).</summary>
        FuturePark,
    }

    /// <summary>
    /// The M0 scheduler semantics (docs/zh/wip/concurrent_scheduler.md §0.2,
    /// §7): fully-converging delta ticks, the seven-step boundary, the
    /// all-finished exit rule, tick-pinning bare waits and pure-yield
    /// negative waits. This is the C# reference implementation of the same
    /// semantics the native mt kernel implements (std/c/scheduler.c +
    /// scheduler_mt.penguin); cross-compiler tests pin both.
    ///
    /// A delta: everything runnable runs until none is (spawns adopted
    /// mid-delta run in their spawning delta; a pure yield re-queues in the
    /// same delta; a kicked condition parker resumes in the kicking delta).
    /// The boundary (§0.2 seven steps): deferred kicks (the channel family
    /// stays immediate, so a transaction's wake lands in the NEXT delta —
    /// classic observable parity) → fire due timers → owed bare-wait parkers
    /// or anyone runnable means the next delta at the SAME tick → (no fd
    /// sources in the C# VM) → advance the tick to the earliest deadline →
    /// all coroutines finished exits; otherwise the program hangs quietly
    /// (decision E: parked-with-no-wake-source is not an exit).
    ///
    /// The two stall leashes mirror the mt kernel: (a) a per-delta resume
    /// budget (EMPEROR_SCHED_STEP_BUDGET, exit 60 — spin/livelock inside one
    /// delta); (b) a pinned-clock streak (EMPEROR_SCHED_PIN_BUDGET, exit 61 —
    /// deltas keep converging while timer waiters sit pending and no timer
    /// makes real, strictly-later-deadline progress, classically a
    /// `while (!cond) wait;` retry loop).
    /// </summary>
    public class SimScheduler
    {
        private long _currentTick = 0;
        private readonly List<TimerEntry> _timers = new();
        /// <summary>Coroutines runnable in the current delta (FIFO).</summary>
        private readonly Queue<IRuntimeValue> _runQueue = new();
        /// <summary>Bare-wait parkers: owed the next delta at the CURRENT
        /// tick — the clock may not advance past them (step 3 before
        /// step 5).</summary>
        private readonly Queue<IRuntimeValue> _owedQueue = new();
        /// <summary>Condition parkers: asleep until a kick. Not runnable,
        /// owed nothing — they never pin the clock and never burn a delta.</summary>
        private readonly List<IRuntimeValue> _condParked = new();
        /// <summary>Jobs that actually started running at least once and
        /// have not finished: the live-coroutine set (the exit rule counts
        /// these; channel objects mis-enqueued by do_wait's generator
        /// re-drive are dropped before counting — they are not coroutines).</summary>
        private readonly HashSet<ReferenceRuntimeValue> _liveJobs = new();
        private BabyPenguinVM? _vm;
        private bool _exited;
        private bool _deferredKick;
        /// <summary>The job currently being driven by RunOneDeltaJob — the
        /// `_after` extern registers its timer against this job (the waiter
        /// that will poll it), mirroring the mt (deadline, co) timer table.</summary>
        private ReferenceRuntimeValue? _currentJob;

        private long _stepBudget = -1;   /* lazy env parse; -1 = unparsed */
        private long _pinBudget = -1;
        private long _stepCount;         /* resumes in the current delta (a) */
        private long _pinStreak;         /* converged deltas with pending timers (b) */
        private long _pinMaxDeadline = long.MinValue; /* latest deadline ever fired */
        private bool _stallReported;     /* one diagnostic per run */
        private bool _trace;

        public long CurrentTick => _currentTick;

        /// <summary>Monotonic delta counter (exposed to Penguin via
        /// __builtin._sim_delta — wires merge same-round writes).</summary>
        public long CurrentRound { get; private set; }

        /// <summary>SimActivityCounter snapshot at the start of the delta
        /// currently executing.</summary>
        public long RoundStartActivity { get; private set; }

        /// <summary>True when the most recently COMPLETED delta carried no
        /// transaction activity (exposed via __builtin._sim_settled together
        /// with RoundStartActivity — a settle-point port read parks until
        /// propagation at the current time is done).</summary>
        public bool LastRoundQuiet { get; private set; }

        /// <summary>The park-kind side channel: set by the note externs
        /// (__builtin._sched_yield / _sched_future_park), consumed when the
        /// following Blocked return files the job.</summary>
        public SchedParkNote PendingParkNote { get; set; } = SchedParkNote.None;

        /// <summary>True when a job called __builtin.exit (or a stall leash
        /// tripped) and the scheduler stopped (vm.Global.ExitCode holds the
        /// code). The driver (__builtin._run) turns this into
        /// ProgramExitException so BabyPenguinVM.Run returns the program's
        /// exit code.</summary>
        public bool Exited => _exited;

        private class TimerEntry : IComparable<TimerEntry>
        {
            public long DeadlineTick { get; set; }
            public ReferenceRuntimeValue Future { get; set; } = null!;
            /// <summary>The job that registered the timer (it polls the
            /// future); null when registered outside any job — firing then
            /// falls back to a generic kick.</summary>
            public ReferenceRuntimeValue? Waiter { get; set; }

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
        /// Jobs sitting in the C# queues (runnable, owed, condition-parked)
        /// have been removed from the Penguin-level pending_jobs queue and
        /// are only reachable from C#; expose them so the GC treats them as
        /// roots.
        /// </summary>
        public IEnumerable<IRuntimeValue> GetReadyJobs()
        {
            foreach (var job in _runQueue)
                yield return job;
            foreach (var job in _owedQueue)
                yield return job;
            foreach (var job in _condParked)
                yield return job;
        }

        public void EnqueueTimerFuture(long deadlineTick, ReferenceRuntimeValue future)
        {
            _timers.Add(new TimerEntry { DeadlineTick = deadlineTick, Future = future, Waiter = _currentJob });
            _timers.Sort();
        }

        /// <summary>A transaction flowed that waiters must observe in the
        /// NEXT delta (channel deliver/emit/consume/close): the boundary's
        /// step-1 kick. Deferring (rather than kicking at the delivery site)
        /// keeps the observable one-delta latency of the classic polling
        /// scheduler.</summary>
        public void RequestDeferredKick() => _deferredKick = true;

        /// <summary>Immediate wake of every condition parker: they re-queue
        /// into the current delta (a spawn completing wakes its waiting
        /// parent mid-delta, mt parity).</summary>
        public void KickFutures()
        {
            if (_trace && _condParked.Count > 0)
            {
                var ids = new System.Text.StringBuilder();
                foreach (var job in _condParked)
                    if (job is ReferenceRuntimeValue rj)
                        ids.Append(rj.RefId).Append(',');
                Console.Error.WriteLine($"[SIM] kick futures: {ids}");
            }
            foreach (var job in _condParked)
                _runQueue.Enqueue(job);
            _condParked.Clear();
        }

        public void Run(BabyPenguinVM vm, RuntimeFrame? frame)
        {
            // The scheduler is owned by the VM's RuntimeGlobal (fresh per
            // execution); reset all simulation state defensively for a fresh
            // run in case Run is invoked twice on the same VM.
            _vm = vm;
            _exited = false;
            _currentTick = 0;
            _timers.Clear();
            _runQueue.Clear();
            _owedQueue.Clear();
            _condParked.Clear();
            _liveJobs.Clear();
            _deferredKick = false;
            _currentJob = null;
            PendingParkNote = SchedParkNote.None;
            CurrentRound = 0;
            RoundStartActivity = 0;
            LastRoundQuiet = false;
            _stepCount = 0;
            _pinStreak = 0;
            _pinMaxDeadline = long.MinValue;
            _stallReported = false;
            ParseBudgets();

            bool dbg = Environment.GetEnvironmentVariable("PENGUIN_SIM_DEBUG") == "1";
            _trace = dbg;
            long dbgRounds = 0;

            while (true)
            {
                // A new delta begins (numbered from 1, matching the mt
                // scheduler's round counter).
                CurrentRound++;
                RoundStartActivity = vm.Global.SimActivityCounter;
                _stepCount = 0;
                dbgRounds++;

                // ---- DELTA: run everything runnable to convergence. ----
                // Spawns adopted mid-delta run in their spawning delta; a
                // pure yield re-queues at the tail; a kicked parker resumes
                // here too. Nothing runs twice before everything runnable
                // ran once (FIFO).
                while (true)
                {
                    AdoptSpawns(vm);
                    if (_runQueue.Count == 0)
                        break;
                    var job = _runQueue.Dequeue();
                    if (job is not ReferenceRuntimeValue jobObj) continue;
                    RunOneDeltaJob(vm, frame, jobObj);
                    if (_exited) return;
                }

                if (dbg && dbgRounds <= 200)
                    Console.Error.WriteLine($"[SIM] delta={CurrentRound} tick={_currentTick} run={_runQueue.Count} owed={_owedQueue.Count} parked={_condParked.Count} live={_liveJobs.Count} timers={_timers.Count} activity={vm.Global.SimActivityCounter}");

                // The delta converged with no transaction activity →
                // propagation at the current time is settling (settle-point
                // readers check this via _sim_settled).
                LastRoundQuiet = vm.Global.SimActivityCounter == RoundStartActivity;

                // ---- Stall leash (b), evaluated at the boundary: the
                // streak counts consecutive converged deltas while
                // time-waiters sit pending and no timer makes real
                // (strictly-later-deadline) progress — the clock is pinned
                // by zero-time churn, classically `while (!cond) wait;`.
                // An empty timer table is not a pin: reset and forget the
                // deadline high-water mark so a fresh wait cycle starts
                // clean. ----
                if (_timers.Count > 0)
                {
                    _pinStreak++;
                    if (_pinBudget > 0 && _pinStreak > _pinBudget)
                    {
                        StallAbort(61,
                            $"the clock is pinned: {_pinStreak} deltas converged with timer wait(s) pending but no timer firing",
                            "suspected tick-pinning retry loop, e.g. `while (!cond) wait;`");
                        return;
                    }
                }
                else
                {
                    _pinStreak = 0;
                    _pinMaxDeadline = long.MinValue;
                }

                // ---- BOUNDARY: the seven steps. ----
                // Step 1: deferred commits — BabyPenguin's channel family
                // stays immediate (M0 scope; deferral is the native mt
                // scheduler's M6 layer), so there is nothing to flush; the
                // deferred KICK stands in: transactions that flowed this
                // delta wake their waiters in the next delta.
                if (_deferredKick)
                {
                    _deferredKick = false;
                    KickFutures();
                }

                // Step 2: fire due timers (deadline <= now), waking their
                // waiters.
                FireDueTimers();

                // Step 3: owed bare-wait parkers, or anyone a kick/timer
                // wake made runnable -> next delta, SAME tick.
                if (_owedQueue.Count > 0)
                {
                    while (_owedQueue.Count > 0)
                        _runQueue.Enqueue(_owedQueue.Dequeue());
                    continue;
                }
                if (_runQueue.Count > 0)
                    continue;

                // Step 4: non-blocking fd probe — the C# VM has no external
                // event sources; nothing to do.

                // Step 5: advance the tick to the earliest future deadline
                // (nothing pins the clock anymore) — the next pass fires it
                // (which is why a timer wake lands two delta numbers after
                // the advance, matching the mt scheduler).
                if (_timers.Count > 0)
                {
                    var d = _timers[0].DeadlineTick;
                    if (d > _currentTick)
                        _currentTick = d;
                    continue;
                }

                // Step 6: only fd waiters remain — none in the C# VM.

                // Step 7: every coroutine finished -> exit. Coroutines still
                // parked with no wake source: HANG (decision E) — a quiet
                // sleep keeps the hang free of CPU burn.
                if (_liveJobs.Count == 0 && GetPendingJobCount(vm) == 0)
                    break;
                Thread.Sleep(10);
            }
        }

        private void ParseBudgets()
        {
            if (_stepBudget < 0)
                _stepBudget = EnvBudget("EMPEROR_SCHED_STEP_BUDGET", 20_000_000);
            if (_pinBudget < 0)
                _pinBudget = EnvBudget("EMPEROR_SCHED_PIN_BUDGET", 20_000_000);
        }

        private static long EnvBudget(string name, long dflt)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(raw)) return dflt;
            if (!long.TryParse(raw, out var v)) return dflt;
            return v < 0 ? dflt : v;
        }

        private void StallAbort(int code, string what, string hint)
        {
            if (_stallReported) return;
            _stallReported = true;
            Console.Error.WriteLine($"penguin sim sched: stall (exit {code}): {what}");
            Console.Error.WriteLine($"  {hint}; {_liveJobs.Count} coroutine(s) live. Raise the matching EMPEROR_SCHED_*_BUDGET knob to lengthen the leash, or set it to 0 to disable.");
            _vm!.Global.ExitCode = code;
            _exited = true;
        }

        /// <summary>
        /// Move every job from the Penguin-level Scheduler.pending_jobs
        /// queue into the current delta's run queue (FIFO preserved):
        /// initial routines plus anything enqueued at runtime by `async`
        /// expressions / do_wait's generator re-drive. A job that already
        /// ran is re-runnable; one that never ran runs for the first time
        /// in its spawning delta (mid-delta spawns are not deferred).
        /// </summary>
        private void AdoptSpawns(BabyPenguinVM vm)
        {
            var items = GetPendingJobItems(vm);
            if (items == null) return;

            while (items.Count > 0)
            {
                var job = items[0];
                items.RemoveAt(0);
                _runQueue.Enqueue(job);
            }
        }

        private static int GetPendingJobCount(BabyPenguinVM vm)
        {
            return GetPendingJobItems(vm)?.Count ?? 0;
        }

        /// <summary>
        /// Run one job in the current delta by directly invoking its
        /// RoutineContext.call() extern, replicating the state machine in
        /// _DefaultRoutine.start():
        ///   state: pending(0) -> running(1)
        ///   status = context.call()
        ///   state: running(1) -> status
        /// Then file the job by its park kind:
        ///   3/4 finished (live--, timers purged, futures kicked — a
        ///       waiting parent's spawn future just completed, same delta);
        ///   2     generator yielded (parked consumers re-poll next delta);
        ///   0     park, filed by the side-channel note (pure yield -> the
        ///       current delta's tail; condition -> the kick-owned set;
        ///       bare -> owed the next delta at the current tick).
        /// </summary>
        private void RunOneDeltaJob(BabyPenguinVM vm, RuntimeFrame? frame, ReferenceRuntimeValue jobObj)
        {
            // Stall leash (a): one resume = one step of the current delta.
            // A spin (`while (!cond) wait -1;`) or a wake-livelock keeps
            // resuming without the delta ever converging; past the leash,
            // report and stop instead of hanging forever.
            if (_stepBudget > 0 && ++_stepCount >= _stepBudget)
            {
                StallAbort(60,
                    "the current delta did not converge within its step budget",
                    "suspected zero-time loop or livelock, e.g. `while (!cond) wait -1;`");
                return;
            }

            var stateField = ReadField(jobObj, "state") as ReferenceRuntimeValue;
            if (stateField == null) return; // not a coroutine (e.g. a channel object mis-enqueued by do_wait's generator re-drive) — drop it
            var stateBasic = ReadField(stateField, "value") as BasicRuntimeValue;
            if (stateBasic == null) return;

            // pending(0) -> running(1); if not pending, skip
            if (stateBasic.I64Value != 0) return;
            stateBasic.I64Value = 1;
            _liveJobs.Add(jobObj);
            PendingParkNote = SchedParkNote.None;

            var contextField = ReadField(jobObj, "context") as ReferenceRuntimeValue;
            if (contextField == null) { stateBasic.I64Value = 3; JobFinished(jobObj); return; }

            var callFuncName = FindExternFuncName(vm, "RoutineContext", "call");
            var func = FindExternFunc(vm, callFuncName);
            if (func == null) { stateBasic.I64Value = 3; JobFinished(jobObj); return; }

            var resultSym = CreateI64Result(vm);
            var args = new List<IRuntimeValue> { contextField };
            _currentJob = jobObj;
            foreach (var brk in func(frame, resultSym, args))
            {
                if (brk.Reason == RuntimeBreakReason.Exited)
                {
                    _exited = true;
                    return;
                }
            }
            _currentJob = null;

            var status = resultSym.Value.As<BasicRuntimeValue>().I64Value;
            stateBasic.I64Value = status;

            if (status == 3 || status == 4)
            {
                JobFinished(jobObj);
                return;
            }
            if (status == 2)
            {
                // Generator yielded a value: consumers parked in do_wait
                // re-poll in the next delta (deferred kick — classic
                // pacing; the consumer's poll re-enqueues this generator).
                _deferredKick = true;
                return;
            }

            // status == 0: a park — filed by the consumed side-channel note.
            var note = ConsumeParkNote();
            if (_trace)
                Console.Error.WriteLine($"[SIM] park job={jobObj.RefId} note={note} delta={CurrentRound}");
            switch (note)
            {
                case SchedParkNote.PureYield:
                    _runQueue.Enqueue(jobObj);
                    break;
                case SchedParkNote.FuturePark:
                    _condParked.Add(jobObj);
                    break;
                default:
                    _owedQueue.Enqueue(jobObj);
                    break;
            }
        }

        private SchedParkNote ConsumeParkNote()
        {
            var note = PendingParkNote;
            PendingParkNote = SchedParkNote.None;
            return note;
        }

        private void JobFinished(ReferenceRuntimeValue jobObj)
        {
            _liveJobs.Remove(jobObj);
            // The job's timer entries are stale now (nobody will poll them).
            for (int i = _timers.Count - 1; i >= 0; i--)
                if (_timers[i].Waiter == jobObj)
                    _timers.RemoveAt(i);
            // A waiting parent's spawn future just completed — wake it in
            // THIS delta (mt parity: completion is a same-delta kick).
            KickFutures();
        }

        /// <summary>
        /// Remove the timers whose deadline has been reached, waking their
        /// waiter for the next delta. A firing with a strictly later
        /// deadline than any before it is real clock progress — it resets
        /// the pin streak (a same-deadline re-registration spin does not).
        /// </summary>
        private void FireDueTimers()
        {
            if (_timers.Count == 0) return;

            for (int i = 0; i < _timers.Count;)
            {
                if (_currentTick >= _timers[i].DeadlineTick)
                {
                    var entry = _timers[i];
                    _timers.RemoveAt(i);
                    if (entry.DeadlineTick > _pinMaxDeadline)
                    {
                        _pinStreak = 0;
                        _pinMaxDeadline = entry.DeadlineTick;
                    }
                    if (entry.Waiter != null)
                    {
                        // Only a condition parker can be woken; one that is
                        // owed/runnable already will re-poll on its own.
                        if (_condParked.Remove(entry.Waiter))
                            _runQueue.Enqueue(entry.Waiter);
                    }
                    else
                    {
                        KickFutures();
                    }
                }
                else
                {
                    i++;
                }
            }
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
