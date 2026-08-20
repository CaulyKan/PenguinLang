using BabyPenguin.SemanticInterface;
using BabyPenguin.Symbol;

namespace BabyPenguin.VirtualMachine
{
    public record RuntimeFrameResult(IRuntimeSymbol? ReturnValue, ReturnStatus ReturnStatus);
    public enum RuntimeBreakReason { Step, Breakpoint, Exception, Exited }
    public record RuntimeBreak(RuntimeBreakReason Reason, RuntimeFrame CurrentFrame);

    public class RuntimeFrame
    {
        // === IR execution state ===
        private readonly IRFunction _function;
        private readonly IRuntimeValue[] _registers;
        private readonly Dictionary<string, int> _labelMap;
        private readonly List<IRuntimeValue> _arguments;
        private int _ip;
        private bool _hasReturned;
        private IRuntimeValue? _returnValue;
        private IRuntimeValue? _pendingCallResult;

        // === Old API compatibility ===
        public SemanticModel Model { get; }
        public RuntimeGlobal Global { get; }
        public ICodeContainer CodeContainer { get; }
        public int FrameLevel { get; set; }
        public int InstructionPointer { get => _ip; set => _ip = value; }
        public RuntimeFrame? ParentFrame { get; set; }
        public RuntimeFrame? ChildFrame { get; set; }
        public Dictionary<string, IRuntimeSymbol> LocalVariables { get; } = [];
        public IRuntimeSymbol? LastReturnVar { get; private set; }

        // Cache whether debug sync is needed (checked once per frame instead of per Store)
        private readonly bool _needsDebugSync;

        public SourceLocation CurrentSourceLocation
        {
            get
            {
                if (_ip < _function.Instructions.Count)
                {
                    var loc = GetLocation(_function.Instructions[_ip]);
                    if (!string.IsNullOrEmpty(loc.FilePath) && loc.Line > 0)
                        return new SourceLocation(loc.FilePath, "", loc.Line, loc.Line, loc.Column, loc.Column);
                }
                return CodeContainer.SourceLocation.EndLocation;
            }
        }

        public override string ToString() => $"[RuntimeFrame: {CodeContainer.FullName()}]";

        public RuntimeFrame(ICodeContainer container, RuntimeGlobal global, List<IRuntimeValue> parameters, RuntimeFrame? parentFrame)
        {
            CodeContainer = container;
            Global = global;
            Model = container.Model;
            FrameLevel = parentFrame?.FrameLevel + 1 ?? 0;
            ParentFrame = parentFrame;
            if (ParentFrame != null) ParentFrame.ChildFrame = this;
            _arguments = parameters;

            var sanitizedName = SanitizeName(container.FullName());
            _function = Global.IRModule?.FindFunction(sanitizedName)
                ?? throw new BabyPenguinRuntimeException($"No IR function found for {container.FullName()} (tried {sanitizedName})", code: ErrorCode.E_RUNTIME_LOOKUP);

            // Pre-allocate register array sized to total register count (named + temp)
            var regCount = _function.RegisterCount;
            _registers = new IRuntimeValue[regCount];
            // Use cached label map from IRFunction (avoids rebuilding per call)
            _labelMap = _function.LabelMap;

            // Cache whether debug sync is needed (avoids checking per-Store)
            // Only enable for DAP debugging sessions, not for CLI debug prints
            _needsDebugSync = global.EnableVariableSync || global.Breakpoints.Count > 0;

            // Only build LocalVariables for DAP debugging (expensive — creates IRuntimeSymbol per variable)
            if (_needsDebugSync)
            {
                foreach (var symbol in container.Symbols)
                {
                    if (symbol.IsParameter)
                    {
                        var sym = IRuntimeSymbol.FromSymbol(container.Model, symbol, Global);
                        try
                        {
                            if (symbol.ParameterIndex < parameters.Count)
                                sym.AssignFrom(parameters[symbol.ParameterIndex]);
                        }
                        catch { /* skip parameter assignment on type mismatch */ }
                        LocalVariables[symbol.FullName()] = sym;
                    }
                    else
                    {
                        LocalVariables[symbol.FullName()] = IRuntimeSymbol.FromSymbol(container.Model, symbol, Global);
                    }
                }
            }
        }

        /// <summary>
        /// Returns all IRuntimeValue references held by this frame (registers, arguments, pending results).
        /// Used by the mark-sweep GC to find root references.
        /// </summary>
        public IEnumerable<IRuntimeValue> GetAllValues()
        {
            foreach (var reg in _registers)
                if (reg != null)
                    yield return reg;
            foreach (var arg in _arguments)
                yield return arg;
            if (_pendingCallResult != null)
                yield return _pendingCallResult;
            if (_returnValue != null)
                yield return _returnValue;
        }

        public IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>> Run()
        {
            // Resume child frame if present (for async/coroutine support)
            if (ChildFrame != null)
            {
                foreach (var resTemp in ChildFrame.Run())
                {
                    if (resTemp.IsLeft)
                    {
                        yield return resTemp;
                    }
                    else
                    {
                        if (resTemp.Right!.ReturnStatus == ReturnStatus.Blocked)
                        {
                            yield return new RuntimeFrameResult(null, ReturnStatus.Blocked);
                            yield break;
                        }

                        if (resTemp.Right!.ReturnValue != null)
                        {
                            _pendingCallResult = resTemp.Right.ReturnValue.Value;
                            LastReturnVar = resTemp.Right.ReturnValue;
                        }

                        if (resTemp.Right!.ReturnStatus == ReturnStatus.Finished || resTemp.Right!.ReturnStatus == ReturnStatus.YieldFinished)
                        {
                            ChildFrame = null;
                            // For void calls, set a sentinel so CALL_VOID handler knows the call completed
                            _pendingCallResult ??= new BasicRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));
                        }
                    }
                }
            }

            RuntimeFrameResult? result = null;
            int gcCounter = 0;
            while (_ip < _function.Instructions.Count && !_hasReturned)
            {
                // Periodic GC check — runs in every frame so GC actually triggers
                // during deep call stacks. CollectGarbage walks ParentFrame chain
                // to find all roots, so it's safe from any frame.
                if (Global.GCEnabled)
                {
                    gcCounter++;
                    if (gcCounter >= Global.GCCheckInterval)
                    {
                        gcCounter = 0;
                        if (Global.AllObjects.Count > Global.GCThreshold)
                            Global.CollectGarbage(this);
                    }
                }

                // DAP step mode support
                if (_ip > 0 && (Global.StepMode == RuntimeGlobal.StepModeEnum.StepIn || Global.StepMode == RuntimeGlobal.StepModeEnum.StepOver))
                {
                    Global.StepMode = RuntimeGlobal.StepModeEnum.Run;
                    yield return new RuntimeBreak(RuntimeBreakReason.Step, this);
                }

                var inst = _function.Instructions[_ip];

                switch (inst)
                {
                    case IRConstInst ci:
                        {
                            var val = MakeValue(ci.Value, ci.Result.GetIrType());
                            Store(ci.Result, val);
                        }
                        break;

                    case IRArgInst ai:
                        {
                            // Value-copy semantics: plain parameters copy value
                            // types on entry. The receiver (`this`) is by-ref —
                            // methods mutate the caller's object (C# struct
                            // mutating-method style); constructors receive the
                            // freshly allocated object.
                            if (ai.ParamIndex < _arguments.Count)
                                Store(ai.Result, ai.ParamName == "this" ? _arguments[ai.ParamIndex] : CopyValue(_arguments[ai.ParamIndex]));
                        }
                        break;

                    case IRAssignInst ai:
                        {
                            var src = Resolve(ai.Src);
                            // Alias-chain assignments materialize receivers /
                            // write-chain temps and must share, not copy.
                            Store(ai.Dest, ai.IsAliasChain ? src : CopyValue(src));
                        }
                        break;

                    case IRCastInst ci:
                        {
                            var operand = Resolve(ci.Operand);
                            var castResult = CastValue(operand, ci.FromType, ci.ToType);
                            Store(ci.Result, castResult);
                        }
                        break;

                    case IRBinOpInst bi:
                        {
                            var left = Resolve(bi.Left);
                            var right = Resolve(bi.Right);
                            var binResult = EvalBinOp(bi.Op, left, right, bi.IrType);
                            Store(bi.Result, binResult);
                        }
                        break;

                    case IRUnaryOpInst ui:
                        {
                            var operand = Resolve(ui.Operand);
                            var unaryResult = EvalUnaryOp(ui.Op, operand, ui.IrType);
                            Store(ui.Result, unaryResult);
                        }
                        break;

                    case IRRdmbrInst ri:
                        {
                            var obj = Resolve(ri.Obj);
                            var fieldVal = ReadField(obj, ri.FieldName);
                            // Set owner for function pointers (fat pointer / method reference)
                            if (fieldVal is FunctionRuntimeValue frv && frv.Owner is NotInitializedRuntimeValue)
                                frv.Owner = obj;
                            // Write-chain reads alias the slot (lvalue addressing);
                            // every other read copies value types (binding copy).
                            Store(ri.Result, ri.IsWriteChain ? fieldVal : CopyValue(fieldVal));
                        }
                        break;

                    case IRWrmbrInst wi:
                        {
                            var obj = Resolve(wi.Obj);
                            var value = Resolve(wi.Value);
                            WriteField(obj, wi.FieldName, value);
                        }
                        break;

                    case IRBrInst bi:
                        {
                            _ip = _labelMap[bi.Target.Name];
                            continue;
                        }

                    case IRBrCondInst bi:
                        {
                            var cond = Resolve(bi.Cond);
                            if (Global.EnableDebugPrint && cond is NotInitializedRuntimeValue)
                            {
                                Global.DebugFunc($"  [BR_COND WARN] {_function.Name} ip={_ip}: cond {bi.Cond.Display()} is NotInitialized\n");
                                // Dump all registers
                                for (int ri = 0; ri < _registers.Length; ri++)
                                    if (_registers[ri] != null)
                                        Global.DebugFunc($"    [{ri}] = {_registers[ri].GetType().Name}\n");
                            }
                            var condBool = ToBool(cond);
                            if (condBool)
                            {
                                if (_labelMap.TryGetValue(bi.TrueLabel.Name, out var trueIp))
                                {
                                    _ip = trueIp;
                                    continue;
                                }
                            }
                            else
                            {
                                if (_labelMap.TryGetValue(bi.FalseLabel.Name, out var falseIp))
                                {
                                    _ip = falseIp;
                                    continue;
                                }
                            }
                        }
                        break;

                    case IRRetInst ri:
                        {
                            _returnValue = Resolve(ri.Value);
                            var retSym = _returnValue != null ? new SimpleRuntimeSymbol(_returnValue, Model) : null;
                            LastReturnVar = retSym;
                            var status = (ReturnStatus)ri.ReturnStatus;
                            // If Blocked or YieldNotFinished, save IP for resumption
                            if (status == ReturnStatus.Blocked || status == ReturnStatus.YieldNotFinished || status == ReturnStatus.YieldFinished)
                            {
                                _ip++;
                                yield return new RuntimeFrameResult(retSym, status);
                                yield break;
                            }
                            _hasReturned = true;
                            yield return new RuntimeFrameResult(retSym, ReturnStatus.Finished);
                            yield break;
                        }

                    case IRRetVoidInst ri:
                        {
                            var status = (ReturnStatus)ri.ReturnStatus;
                            // If Blocked or YieldNotFinished, save IP for resumption
                            if (status == ReturnStatus.Blocked || status == ReturnStatus.YieldNotFinished)
                            {
                                _ip++;
                                yield return new RuntimeFrameResult(null, status);
                                yield break;
                            }
                            _hasReturned = true;
                            yield return new RuntimeFrameResult(null, ReturnStatus.Finished);
                            yield break;
                        }

                    case IRCallInst ci:
                        {
                            // If resuming from a blocked call, use the saved result
                            if (_pendingCallResult != null)
                            {
                                Store(ci.ResultValue, _pendingCallResult);
                                LastReturnVar = new SimpleRuntimeSymbol(_pendingCallResult, Model);
                                _pendingCallResult = null;
                                break;
                            }

                            var args = ci.Args.Select(Resolve).ToList();

                            // Try extern function first
                            var extResult = TryCallExternFunction(ci.FuncName, args, ci.RetType);
                            if (extResult != null)
                            {
                                Store(ci.ResultValue, extResult.Value.Value);
                                if (extResult.Value.Exited)
                                {
                                    yield return new RuntimeBreak(RuntimeBreakReason.Exited, this);
                                    yield break;
                                }
                                if (extResult.Value.Value != null)
                                {
                                    var retSym = new SimpleRuntimeSymbol(extResult.Value.Value, Model);
                                    LastReturnVar = retSym;
                                }
                            }
                            else
                            {
                                // Module function
                                var callee = Global.IRModule?.FindFunction(ci.FuncName);
                                if (callee != null)
                                {
                                    var calleeCC = FindCodeContainer(ci.FuncName);
                                    var childFrame = new RuntimeFrame(calleeCC, Global, args, this);
                                    var isStepOver = Global.StepMode == RuntimeGlobal.StepModeEnum.StepOver || Global.StepMode == RuntimeGlobal.StepModeEnum.StepOut;
                                    if (isStepOver) Global.StepMode = RuntimeGlobal.StepModeEnum.Run;

                                    foreach (var res in childFrame.Run())
                                    {
                                        if (res.IsLeft)
                                            yield return res;
                                        else
                                        {
                                            if (res.Right!.ReturnStatus == ReturnStatus.Blocked)
                                            {
                                                ChildFrame = childFrame;
                                                yield return new RuntimeFrameResult(null, ReturnStatus.Blocked);
                                                yield break;
                                            }
                                            if (res.Right.ReturnValue != null)
                                            {
                                                Store(ci.ResultValue, res.Right.ReturnValue.Value);
                                                LastReturnVar = res.Right.ReturnValue;
                                            }
                                            if (res.Right.ReturnStatus == ReturnStatus.Finished || res.Right.ReturnStatus == ReturnStatus.YieldFinished)
                                                ChildFrame = null;
                                        }
                                    }
                                    if (isStepOver)
                                        yield return new RuntimeBreak(RuntimeBreakReason.Step, this);
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found", code: ErrorCode.E_RUNTIME_LOOKUP);
                                }
                            }
                        }
                        break;

                    case IRCallVoidInst ci:
                        {
                            // If resuming from a blocked call, skip re-execution
                            if (_pendingCallResult != null)
                            {
                                _pendingCallResult = null;
                                break;
                            }

                            var args = ci.Args.Select(Resolve).ToList();

                            var (found, exited) = TryCallExternFunctionVoid(ci.FuncName, args);
                            if (exited)
                            {
                                yield return new RuntimeBreak(RuntimeBreakReason.Exited, this);
                                yield break;
                            }
                            if (!found)
                            {
                                var callee = Global.IRModule?.FindFunction(ci.FuncName);
                                if (callee != null)
                                {
                                    var calleeCC = FindCodeContainer(ci.FuncName);
                                    var childFrame = new RuntimeFrame(calleeCC, Global, args, this);
                                    var isStepOver = Global.StepMode == RuntimeGlobal.StepModeEnum.StepOver || Global.StepMode == RuntimeGlobal.StepModeEnum.StepOut;
                                    if (isStepOver) Global.StepMode = RuntimeGlobal.StepModeEnum.Run;

                                    foreach (var res in childFrame.Run())
                                    {
                                        if (res.IsLeft)
                                            yield return res;
                                        else
                                        {
                                            if (res.Right!.ReturnStatus == ReturnStatus.Blocked)
                                            {
                                                ChildFrame = childFrame;
                                                yield return new RuntimeFrameResult(null, ReturnStatus.Blocked);
                                                yield break;
                                            }
                                            if (res.Right.ReturnValue != null)
                                                LastReturnVar = res.Right.ReturnValue;
                                            if (res.Right.ReturnStatus == ReturnStatus.Finished || res.Right.ReturnStatus == ReturnStatus.YieldFinished)
                                                ChildFrame = null;
                                        }
                                    }
                                    if (isStepOver)
                                        yield return new RuntimeBreak(RuntimeBreakReason.Step, this);
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found", code: ErrorCode.E_RUNTIME_LOOKUP);
                                }
                            }
                        }
                        break;

                    case IRNewInst ni:
                        {
                            var args = ni.Args.Select(Resolve).ToList();
                            var typeNode = Model.ResolveTypeNode(ni.TypeName);
                            var typeInfo = typeNode?.ToType(Mutability.Mutable)
                                ?? Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);

                            IRuntimeValue newObj;
                            if (typeNode is IEnumNode)
                            {
                                // Enum types should be created as EnumRuntimeValue
                                newObj = CreateDefaultEnum(typeInfo);
                            }
                            else
                            {
                                newObj = CreateNewObject(typeInfo, args);
                            }
                            Store(ni.Result, newObj);
                        }
                        break;

                    case IRNewEnumInst nei:
                        {
                            var typeNode = Model.ResolveTypeNode(nei.TypeName);
                            var typeInfo = typeNode?.ToType(Mutability.Mutable)
                                ?? Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
                            var payload = nei.Payload != null ? Resolve(nei.Payload) : null;
                            var enumVal = CreateEnumValue(typeInfo, nei.VariantIdx, payload);
                            Store(nei.Result, enumVal);
                        }
                        break;

                    case IRIsEnumInst isi:
                        {
                            var enumVal = Resolve(isi.EnumValue);
                            var variantIdx = Resolve(isi.VariantIdx);
                            bool matches = CheckEnumVariant(enumVal, variantIdx);
                            Store(isi.Result, new BasicRuntimeValue(Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable)) { BoolValue = matches });
                        }
                        break;

                    case IRRdenumInst ri:
                        {
                            var enumVal = Resolve(ri.EnumValue);
                            var payload = ExtractEnumPayload(enumVal);
                            // Write-chain extractions alias the payload slot
                            // (`e.a.x = 42` writes in place); binding
                            // extractions (`let q = e.a`) copy value payloads.
                            Store(ri.Result, ri.IsWriteChain ? payload : CopyValue(payload));
                        }
                        break;

                    case IRGlobalLoadInst gi:
                        {
                            // Globals share their storage object in the
                            // register: binding boundaries (ASSIGN/ARG/RDMBR)
                            // copy, and member writes go through the shared
                            // object. Copying here would orphan writes.
                            if (Global.GlobalVariables.TryGetValue(gi.GlobalName, out var globalSym))
                                Store(gi.Result, globalSym.Value);
                            else
                                Store(gi.Result, new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)));
                        }
                        break;

                    case IRGlobalStoreInst gi:
                        {
                            var val = Resolve(gi.Value);
                            if (Global.GlobalVariables.TryGetValue(gi.GlobalName, out var globalSym))
                                globalSym.AssignFrom(val);
                            else
                                Global.GlobalVariables[gi.GlobalName] = new SimpleRuntimeSymbol(val, Model);
                        }
                        break;

                    case IRLabelInst:
                        break;

                    case IRCallFuncPtrInst ci:
                        {
                            // If resuming from a blocked call, use the saved result
                            if (_pendingCallResult != null)
                            {
                                Store(ci.ResultValue, _pendingCallResult);
                                LastReturnVar = new SimpleRuntimeSymbol(_pendingCallResult, Model);
                                _pendingCallResult = null;
                                break;
                            }

                            var funcPtrVal = Resolve(ci.FuncPtr);
                            var callArgs = ci.Args.Select(Resolve).ToList();
                            // Try extern function first
                            if (funcPtrVal is FunctionRuntimeValue frv)
                            {
                                var funcName = SanitizeName(frv.FunctionSymbol.FullName());
                                var fullArgs = callArgs;
                                if (frv.Owner is not NotInitializedRuntimeValue && !frv.IsStatic)
                                    fullArgs = [frv.Owner, .. callArgs];
                                var extResult = TryCallExternFunction(funcName, fullArgs, ci.RetType);
                                if (extResult != null)
                                {
                                    Store(ci.ResultValue, extResult.Value.Value);
                                    if (extResult.Value.Exited)
                                    {
                                        yield return new RuntimeBreak(RuntimeBreakReason.Exited, this);
                                        yield break;
                                    }
                                    if (extResult.Value.Value != null)
                                    {
                                        LastReturnVar = new SimpleRuntimeSymbol(extResult.Value.Value, Model);
                                    }
                                    break;
                                }
                                // Module function - run with yield propagation
                                var calleeCC = FindCodeContainer(funcName);
                                if (calleeCC != null)
                                {
                                    var childFrame = new RuntimeFrame(calleeCC, Global, fullArgs, this);
                                    IRuntimeValue? retVal = null;
                                    foreach (var res in childFrame.Run())
                                    {
                                        if (res.IsLeft)
                                        {
                                            yield return res;
                                            if (res.Left!.Reason == RuntimeBreakReason.Exited)
                                                yield break;
                                        }
                                        else
                                        {
                                            if (res.Right!.ReturnStatus == ReturnStatus.Blocked)
                                            {
                                                ChildFrame = childFrame;
                                                yield return new RuntimeFrameResult(null, ReturnStatus.Blocked);
                                                yield break;
                                            }
                                            if (res.Right.ReturnValue != null)
                                                retVal = res.Right.ReturnValue.Value;
                                            if (res.Right.ReturnStatus == ReturnStatus.Finished || res.Right.ReturnStatus == ReturnStatus.YieldFinished)
                                                ChildFrame = null;
                                        }
                                    }
                                    if (retVal != null)
                                    {
                                        Store(ci.ResultValue, retVal);
                                        LastReturnVar = new SimpleRuntimeSymbol(retVal, Model);
                                    }
                                }
                            }
                        }
                        break;

                    case IRCallFuncPtrVoidInst ci:
                        {
                            // If resuming from a blocked call, skip re-execution
                            if (_pendingCallResult != null)
                            {
                                _pendingCallResult = null;
                                break;
                            }

                            var funcPtrVal = Resolve(ci.FuncPtr);
                            var callArgs = ci.Args.Select(Resolve).ToList();
                            // Try extern function first
                            if (funcPtrVal is FunctionRuntimeValue frv)
                            {
                                var funcName = SanitizeName(frv.FunctionSymbol.FullName());
                                var fullArgs = callArgs;
                                if (frv.Owner is not NotInitializedRuntimeValue && !frv.IsStatic)
                                    fullArgs = [frv.Owner, .. callArgs];
                                var (found, exited) = TryCallExternFunctionVoid(funcName, fullArgs);
                                if (exited)
                                {
                                    yield return new RuntimeBreak(RuntimeBreakReason.Exited, this);
                                    yield break;
                                }
                                if (found)
                                    break;
                                // Module function - run with yield propagation
                                var calleeCC = FindCodeContainer(funcName);
                                if (calleeCC != null)
                                {
                                    var childFrame = new RuntimeFrame(calleeCC, Global, fullArgs, this);
                                    foreach (var res in childFrame.Run())
                                    {
                                        if (res.IsLeft)
                                        {
                                            yield return res;
                                            if (res.Left!.Reason == RuntimeBreakReason.Exited)
                                                yield break;
                                        }
                                        else
                                        {
                                            if (res.Right!.ReturnStatus == ReturnStatus.Blocked)
                                            {
                                                ChildFrame = childFrame;
                                                yield return new RuntimeFrameResult(null, ReturnStatus.Blocked);
                                                yield break;
                                            }
                                            if (res.Right.ReturnStatus == ReturnStatus.Finished || res.Right.ReturnStatus == ReturnStatus.YieldFinished)
                                                ChildFrame = null;
                                        }
                                    }
                                }
                            }
                        }
                        break;

                    case IRIsInstanceInst isInst:
                        {
                            var objRtv = Resolve(isInst.Obj);
                            var typeId = isInst.TypeId;
                            bool typeCheckResult = false;
                            if (objRtv != null && !(objRtv is NotInitializedRuntimeValue))
                            {
                                var actual = objRtv;
                                if (actual is InterfaceRuntimeSymbol intfVal)
                                    actual = intfVal.Value;
                                if (actual != null && actual.TypeInfo != null)
                                {
                                    var ti = actual.TypeInfo;
                                    // Check type node directly if available
                                    if (ti.TypeNode is IVTableContainer vtc)
                                    {
                                        typeCheckResult = vtc.FullName() == typeId
                                            || vtc.VTables.Any(v => v.Interface.FullName() == typeId);
                                    }
                                    else
                                    {
                                        // Fallback: compare by name
                                        var typeName = ti.FullName();
                                        if (typeName.StartsWith("!mut ")) typeName = typeName[5..];
                                        if (typeName.StartsWith("ref<") && typeName.EndsWith(">"))
                                            typeName = typeName.Substring(4, typeName.Length - 5);
                                        if (typeName == typeId)
                                            typeCheckResult = true;
                                    }
                                }
                            }
                            var boolType = Model.BasicTypeNodes.GetCachedImmutableType("bool")!;
                            Store(isInst.Result, new BasicRuntimeValue(boolType) { BoolValue = typeCheckResult });
                        }
                        break;
                    case IRBoxInst:
                    case IRUnboxInst:
                    case IRCallVirtInst:
                        throw new NotImplementedException($"Instruction {inst.GetType().Name} not yet implemented");
                }

                _ip++;

                if (result != null)
                {
                    yield return result;
                    break;
                }
            }

            if (!_hasReturned && result == null)
            {
                yield return new RuntimeFrameResult(null, ReturnStatus.Finished);
            }
        }

        /// <summary>
        /// Fast-path execution: directly returns the result without using yield/iterator.
        /// Avoids the massive overhead of iterator state machines for normal (non-debug, non-coroutine) execution.
        /// Throws ProgramExitException on __builtin.exit() instead of yielding.
        /// </summary>
        public IRuntimeValue? RunDirect()
        {
            while (_ip < _function.Instructions.Count && !_hasReturned)
            {
                // Periodic GC check using global counter (works across recursive calls)
                if (Global.GCEnabled)
                {
                    Global.GlobalInstructionCount++;
                    if (Environment.GetEnvironmentVariable("BP_STEP") != null && _function.Name.Contains(Environment.GetEnvironmentVariable("BP_STEP")!))
                        Console.Error.WriteLine($"[STEP] {_function.Name} ip={_ip} {_function.Instructions[_ip].Display()}");
                    if (Global.GlobalInstructionCount % Global.GCCheckInterval == 0)
                    {
                        if (Global.AllObjects.Count > Global.GCThreshold)
                            Global.CollectGarbage(this);
                    }
                }

                var inst = _function.Instructions[_ip];

                switch (inst)
                {
                    case IRConstInst ci:
                        {
                            var val = MakeValue(ci.Value, ci.Result.GetIrType());
                            Store(ci.Result, val);
                        }
                        break;

                    case IRArgInst ai:
                        {
                            // Value-copy semantics: plain parameters copy value
                            // types on entry. The receiver (`this`) is by-ref —
                            // methods mutate the caller's object (C# struct
                            // mutating-method style); constructors receive the
                            // freshly allocated object.
                            if (ai.ParamIndex < _arguments.Count)
                                Store(ai.Result, ai.ParamName == "this" ? _arguments[ai.ParamIndex] : CopyValue(_arguments[ai.ParamIndex]));
                        }
                        break;

                    case IRAssignInst ai:
                        {
                            var src = Resolve(ai.Src);
                            // Alias-chain assignments materialize receivers /
                            // write-chain temps and must share, not copy.
                            Store(ai.Dest, ai.IsAliasChain ? src : CopyValue(src));
                        }
                        break;

                    case IRCastInst ci:
                        {
                            var operand = Resolve(ci.Operand);
                            var castResult = CastValue(operand, ci.FromType, ci.ToType);
                            Store(ci.Result, castResult);
                        }
                        break;

                    case IRBinOpInst bi:
                        {
                            var left = Resolve(bi.Left);
                            var right = Resolve(bi.Right);
                            var binResult = EvalBinOp(bi.Op, left, right, bi.IrType);
                            Store(bi.Result, binResult);
                        }
                        break;

                    case IRUnaryOpInst ui:
                        {
                            var operand = Resolve(ui.Operand);
                            var unaryResult = EvalUnaryOp(ui.Op, operand, ui.IrType);
                            Store(ui.Result, unaryResult);
                        }
                        break;

                    case IRRdmbrInst ri:
                        {
                            var obj = Resolve(ri.Obj);
                            var fieldVal = ReadField(obj, ri.FieldName);
                            if (fieldVal is FunctionRuntimeValue frv && frv.Owner is NotInitializedRuntimeValue)
                                frv.Owner = obj;
                            Store(ri.Result, ri.IsWriteChain ? fieldVal : CopyValue(fieldVal));
                        }
                        break;

                    case IRWrmbrInst wi:
                        {
                            var obj = Resolve(wi.Obj);
                            var value = Resolve(wi.Value);
                            WriteField(obj, wi.FieldName, value);
                        }
                        break;

                    case IRBrInst bi:
                        {
                            _ip = _labelMap[bi.Target.Name];
                            continue;
                        }

                    case IRBrCondInst bi:
                        {
                            var cond = Resolve(bi.Cond);
                            var condBool = ToBool(cond);
                            if (condBool)
                            {
                                _ip = _labelMap[bi.TrueLabel.Name];
                                continue;
                            }
                            else
                            {
                                _ip = _labelMap[bi.FalseLabel.Name];
                                continue;
                            }
                        }

                    case IRRetInst ri:
                        {
                            _returnValue = Resolve(ri.Value);
                            _hasReturned = true;
                            return _returnValue;
                        }

                    case IRRetVoidInst:
                        {
                            _hasReturned = true;
                            return null;
                        }

                    case IRCallInst ci:
                        {
                            var args = ci.Args.Select(Resolve).ToList();

                            // Try extern function first
                            var extResult = TryCallExternFunction(ci.FuncName, args, ci.RetType);
                            if (extResult != null)
                            {
                                if (extResult.Value.Exited)
                                    throw new ProgramExitException();
                                Store(ci.ResultValue, extResult.Value.Value);
                                if (extResult.Value.Value != null)
                                    LastReturnVar = new SimpleRuntimeSymbol(extResult.Value.Value, Model);
                            }
                            else
                            {
                                // Module function - call directly (no yield)
                                var callee = Global.IRModule?.FindFunction(ci.FuncName)
                                    ?? throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found", code: ErrorCode.E_RUNTIME_LOOKUP);
                                var calleeCC = FindCodeContainer(ci.FuncName);
                                var childFrame = new RuntimeFrame(calleeCC, Global, args, this);
                                var retVal = childFrame.RunDirect();
                                if (retVal != null)
                                {
                                    Store(ci.ResultValue, retVal);
                                    LastReturnVar = new SimpleRuntimeSymbol(retVal, Model);
                                }
                            }
                        }
                        break;

                    case IRCallVoidInst ci:
                        {
                            var args = ci.Args.Select(Resolve).ToList();

                            var (found, exited) = TryCallExternFunctionVoid(ci.FuncName, args);
                            if (exited)
                                throw new ProgramExitException();
                            if (!found)
                            {
                                var callee = Global.IRModule?.FindFunction(ci.FuncName)
                                    ?? throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found", code: ErrorCode.E_RUNTIME_LOOKUP);
                                var calleeCC = FindCodeContainer(ci.FuncName);
                                var childFrame = new RuntimeFrame(calleeCC, Global, args, this);
                                childFrame.RunDirect();
                            }
                        }
                        break;

                    case IRNewInst ni:
                        {
                            var args = ni.Args.Select(Resolve).ToList();
                            var typeNode = Model.ResolveTypeNode(ni.TypeName);
                            var typeInfo = typeNode?.ToType(Mutability.Mutable)
                                ?? Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);

                            IRuntimeValue newObj;
                            if (typeNode is IEnumNode)
                                newObj = CreateDefaultEnum(typeInfo);
                            else
                                newObj = CreateNewObject(typeInfo, args);
                            Store(ni.Result, newObj);
                        }
                        break;

                    case IRNewEnumInst nei:
                        {
                            var typeNode = Model.ResolveTypeNode(nei.TypeName);
                            var typeInfo = typeNode?.ToType(Mutability.Mutable)
                                ?? Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
                            var payload = nei.Payload != null ? Resolve(nei.Payload) : null;
                            var enumVal = CreateEnumValue(typeInfo, nei.VariantIdx, payload);
                            Store(nei.Result, enumVal);
                        }
                        break;

                    case IRIsEnumInst isi:
                        {
                            var enumVal = Resolve(isi.EnumValue);
                            var variantIdx = Resolve(isi.VariantIdx);
                            bool matches = CheckEnumVariant(enumVal, variantIdx);
                            Store(isi.Result, new BasicRuntimeValue(Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable)) { BoolValue = matches });
                        }
                        break;

                    case IRRdenumInst ri:
                        {
                            var enumVal = Resolve(ri.EnumValue);
                            var payload = ExtractEnumPayload(enumVal);
                            // Write-chain extractions alias the payload slot
                            // (`e.a.x = 42` writes in place); binding
                            // extractions (`let q = e.a`) copy value payloads.
                            Store(ri.Result, ri.IsWriteChain ? payload : CopyValue(payload));
                        }
                        break;

                    case IRGlobalLoadInst gi:
                        {
                            // Globals share their storage object in the
                            // register: binding boundaries (ASSIGN/ARG/RDMBR)
                            // copy, and member writes go through the shared
                            // object. Copying here would orphan writes.
                            if (Global.GlobalVariables.TryGetValue(gi.GlobalName, out var globalSym))
                                Store(gi.Result, globalSym.Value);
                            else
                                Store(gi.Result, new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)));
                        }
                        break;

                    case IRGlobalStoreInst gi:
                        {
                            var val = Resolve(gi.Value);
                            if (Global.GlobalVariables.TryGetValue(gi.GlobalName, out var globalSym))
                                globalSym.AssignFrom(val);
                            else
                                Global.GlobalVariables[gi.GlobalName] = new SimpleRuntimeSymbol(val, Model);
                        }
                        break;

                    case IRLabelInst:
                        break;

                    case IRCallFuncPtrInst ci:
                        {
                            var funcPtrVal = Resolve(ci.FuncPtr);
                            var callArgs = ci.Args.Select(Resolve).ToList();
                            if (funcPtrVal is FunctionRuntimeValue frv)
                            {
                                var funcName = SanitizeName(frv.FunctionSymbol.FullName());
                                var fullArgs = callArgs;
                                if (frv.Owner is not NotInitializedRuntimeValue && !frv.IsStatic)
                                    fullArgs = [frv.Owner, .. callArgs];
                                var extResult = TryCallExternFunction(funcName, fullArgs, ci.RetType);
                                if (extResult != null)
                                {
                                    Store(ci.ResultValue, extResult.Value.Value);
                                    if (extResult.Value.Exited)
                                        throw new ProgramExitException();
                                    if (extResult.Value.Value != null)
                                        LastReturnVar = new SimpleRuntimeSymbol(extResult.Value.Value, Model);
                                    break;
                                }
                                var calleeCC = FindCodeContainer(funcName);
                                if (calleeCC != null)
                                {
                                    var childFrame = new RuntimeFrame(calleeCC, Global, fullArgs, this);
                                    var retVal = childFrame.RunDirect();
                                    if (retVal != null)
                                    {
                                        Store(ci.ResultValue, retVal);
                                        LastReturnVar = new SimpleRuntimeSymbol(retVal, Model);
                                    }
                                }
                            }
                        }
                        break;

                    case IRCallFuncPtrVoidInst ci:
                        {
                            var funcPtrVal = Resolve(ci.FuncPtr);
                            var callArgs = ci.Args.Select(Resolve).ToList();
                            if (funcPtrVal is FunctionRuntimeValue frv)
                            {
                                var funcName = SanitizeName(frv.FunctionSymbol.FullName());
                                var fullArgs = callArgs;
                                if (frv.Owner is not NotInitializedRuntimeValue && !frv.IsStatic)
                                    fullArgs = [frv.Owner, .. callArgs];
                                var (found, exited) = TryCallExternFunctionVoid(funcName, fullArgs);
                                if (exited)
                                    throw new ProgramExitException();
                                if (found)
                                    break;
                                var calleeCC = FindCodeContainer(funcName);
                                if (calleeCC != null)
                                {
                                    var childFrame = new RuntimeFrame(calleeCC, Global, fullArgs, this);
                                    childFrame.RunDirect();
                                }
                            }
                        }
                        break;

                    case IRIsInstanceInst isInst:
                        {
                            var objRtv = Resolve(isInst.Obj);
                            var typeId = isInst.TypeId;
                            bool typeCheckResult = false;
                            if (objRtv != null && !(objRtv is NotInitializedRuntimeValue))
                            {
                                var actual = objRtv;
                                if (actual is InterfaceRuntimeSymbol intfVal)
                                    actual = intfVal.Value;
                                if (actual != null && actual.TypeInfo != null)
                                {
                                    var ti = actual.TypeInfo;
                                    // Check type node directly if available
                                    if (ti.TypeNode is IVTableContainer vtc)
                                    {
                                        typeCheckResult = vtc.FullName() == typeId
                                            || vtc.VTables.Any(v => v.Interface.FullName() == typeId);
                                    }
                                    else
                                    {
                                        // Fallback: compare by name
                                        var typeName = ti.FullName();
                                        if (typeName.StartsWith("!mut ")) typeName = typeName[5..];
                                        if (typeName.StartsWith("ref<") && typeName.EndsWith(">"))
                                            typeName = typeName.Substring(4, typeName.Length - 5);
                                        if (typeName == typeId)
                                            typeCheckResult = true;
                                    }
                                }
                            }
                            var boolType = Model.BasicTypeNodes.GetCachedImmutableType("bool")!;
                            Store(isInst.Result, new BasicRuntimeValue(boolType) { BoolValue = typeCheckResult });
                        }
                        break;
                    case IRBoxInst:
                    case IRUnboxInst:
                    case IRCallVirtInst:
                        throw new NotImplementedException($"Instruction {inst.GetType().Name} not yet implemented");
                }

                _ip++;
            }

            return _returnValue;
        }

        // === Resolve / Store ===

        private IRuntimeValue Resolve(IRValue val)
        {
            return val switch
            {
                IRNamedRegister nr => nr.Index < _registers.Length && _registers[nr.Index] != null
                    ? _registers[nr.Index]
                    : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                IRTempRegister tr => tr.Index < _registers.Length && _registers[tr.Index] != null
                    ? _registers[tr.Index]
                    : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                IRConstant c => ResolveConstant(c),
                IRGlobalRef g => Global.GlobalVariables.TryGetValue(g.Name, out var sym) ? sym.Value : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                _ => throw new BabyPenguinRuntimeException($"Cannot resolve IR value: {val.Display()}", code: ErrorCode.E_RUNTIME_INVALID_OP)
            };
        }

        private IRuntimeValue ResolveConstant(IRConstant c)
        {
            // Function pointer: resolve to FunctionRuntimeValue
            if (c.IrType == "funptr")
            {
                var funcSymbol = Model.ResolveSymbol(c.Value);
                if (funcSymbol != null)
                    return new FunctionRuntimeValue(funcSymbol.TypeInfo, funcSymbol);

                // Try matching by sanitized code container names using O(1) index
                if (Global.CodeContainerIndex.TryGetValue(c.Value, out var cc))
                {
                    var sym = Model.ResolveSymbol(cc.FullName());
                    if (sym != null)
                        return new FunctionRuntimeValue(sym.TypeInfo, sym);
                }

                // Try matching by sanitized symbol full names (for VTable functions, etc.)
                foreach (var sym in Model.Symbols)
                {
                    if (sym.IsFunction && SanitizeName(sym.FullName()) == c.Value)
                        return new FunctionRuntimeValue(sym.TypeInfo, sym);
                }

                throw new BabyPenguinRuntimeException($"Cannot resolve function pointer: {c.Value}", code: ErrorCode.E_RUNTIME_LOOKUP);
            }
            return MakeValue(c.Value, c.IrType);
        }

        private void Store(IRValue target, IRuntimeValue value)
        {
            switch (target)
            {
                case IRNamedRegister nr:
                    _registers[nr.Index] = value;
                    // Sync with LocalVariables for DAP variable inspection (best-effort, debug mode only)
                    if (_needsDebugSync && value is not NotInitializedRuntimeValue && LocalVariables.TryGetValue(nr.Name, out var sym))
                    {
                        try { if (value.TypeInfo.CanImplicitlyCastTo(sym.TypeInfo)) sym.AssignFrom(value); }
                        catch { /* skip sync on type mismatch */ }
                    }
                    break;
                case IRTempRegister tr:
                    _registers[tr.Index] = value;
                    break;
            }
        }

        // === Extern function bridge ===

        private (IRuntimeValue? Value, bool Exited)? TryCallExternFunction(string funcName, List<IRuntimeValue> args, string retType)
        {
            var match = FindExternFunction(funcName);
            if (match == null) return null;

            var returnType = IrTypeToType(retType);
            var resultSym = CreateResultSymbol(returnType);
            foreach (var brk in match(this, resultSym, args))
            {
                if (brk.Reason == RuntimeBreakReason.Exited)
                {
                    Global.HasExited = true;
                    return (resultSym.Value, true);
                }
                throw new BabyPenguinRuntimeException($"Extern function {funcName} yielded break: {brk.Reason}", code: ErrorCode.E_RUNTIME_INVALID_OP);
            }
            return (resultSym.Value, false);
        }

        private (bool Found, bool Exited) TryCallExternFunctionVoid(string funcName, List<IRuntimeValue> args)
        {
            var match = FindExternFunction(funcName);
            if (match == null) return (false, false);

            foreach (var brk in match(this, null, args))
            {
                if (brk.Reason == RuntimeBreakReason.Exited)
                {
                    Global.HasExited = true;
                    return (true, true);
                }
                throw new BabyPenguinRuntimeException($"Extern function {funcName} yielded break: {brk.Reason}", code: ErrorCode.E_RUNTIME_INVALID_OP);
            }
            return (true, false);
        }

        private Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>? FindExternFunction(string funcName)
        {
            // Try direct lookup first
            if (Global.ExternFunctions.TryGetValue(funcName, out var func))
                return func;

            // Try pre-built sanitized index for O(1) lookup
            if (Global.SanitizedExternFunctionIndex.TryGetValue(funcName, out var sanitizedFunc))
                return sanitizedFunc;

            return null;
        }

        private IRuntimeSymbol CreateResultSymbol(IType returnType)
        {
            var fakeSymbol = new ExternResultSymbol(returnType);
            return IRuntimeSymbol.FromSymbol(Model, fakeSymbol, Global);
        }

        private IType IrTypeToType(string irType)
        {
            var cached = Model.BasicTypeNodes.GetCachedImmutableType(irType);
            if (cached != null) return cached;

            return irType switch
            {
                _ when irType.StartsWith("enum<") => ResolveComplexType(irType),
                _ when irType.StartsWith("ref<") => ResolveComplexType(irType),
                _ when irType.StartsWith("struct<") => ResolveComplexType(irType),
                _ => Model.BasicTypeNodes.GetCachedImmutableType("void")!
            };
        }

        private IType ResolveComplexType(string irType)
        {
            // Extract type name from ref<X>, enum<X>, struct<X>
            var innerStart = irType.IndexOf('<') + 1;
            var innerEnd = irType.LastIndexOf('>');
            if (innerStart > 0 && innerEnd > innerStart)
            {
                var innerName = irType[innerStart..innerEnd];
                var typeNode = Model.ResolveTypeNode(innerName);
                if (typeNode != null)
                    return typeNode.ToType(Mutability.Mutable);
            }
            return Model.BasicTypeNodes.Void.ToType(Mutability.Immutable);
        }

        // === Value operations ===

        /// <summary>
        /// Value-copy semantics: storing a value into a binding / parameter /
        /// field / extraction copies value types memberwise (value classes,
        /// enums with value payloads, primitives); reference classes are
        /// shared like a copied pointer. `mut` never affects this.
        /// </summary>
        private IRuntimeValue CopyValue(IRuntimeValue val)
        {
            return RuntimeValueCopier.CopyIfValueSemantic(val, Global);
        }

        private IRuntimeValue MakeValue(string literal, string irType)
        {
            if (irType == "bool")
                return new BasicRuntimeValue(Model.BasicTypeNodes.GetCachedImmutableType("bool")!) { BoolValue = literal == "true" };

            if (irType == "string" || irType == "ref<string>")
            {
                var type = Model.BasicTypeNodes.GetCachedImmutableType("ref<string>")!;
                var val = literal;
                if (val.StartsWith("\"") && val.EndsWith("\""))
                    val = UnescapeString(val[1..^1]);
                return new BasicRuntimeValue(type) { StringValue = val };
            }

            if (irType == "char")
                return new BasicRuntimeValue(Model.BasicTypeNodes.GetCachedImmutableType("char")!) { CharValue = literal.Length > 0 ? literal[0] : '\0' };

            if (irType == "void")
                return new NotInitializedRuntimeValue(Model.BasicTypeNodes.GetCachedImmutableType("void")!);

            var numericType = Model.BasicTypeNodes.GetCachedImmutableType(irType);

            if (numericType != null)
            {
                var bv = new BasicRuntimeValue(numericType);
                switch (irType)
                {
                    case "i8": bv.I8Value = sbyte.Parse(literal); break;
                    case "i16": bv.I16Value = short.Parse(literal); break;
                    case "i32": bv.I32Value = int.Parse(literal); break;
                    case "i64": bv.I64Value = long.Parse(literal); break;
                    case "u8": bv.U8Value = byte.Parse(literal); break;
                    case "u16": bv.U16Value = ushort.Parse(literal); break;
                    case "u32": bv.U32Value = uint.Parse(literal); break;
                    case "u64": bv.U64Value = ulong.Parse(literal); break;
                    case "f32": bv.FloatValue = float.Parse(literal); break;
                    case "f64": bv.DoubleValue = double.Parse(literal); break;
                }
                return bv;
            }

            if (long.TryParse(literal, out var intVal))
                return new BasicRuntimeValue(Model.BasicTypeNodes.GetCachedImmutableType("i64")!) { I64Value = intVal };

            return new NotInitializedRuntimeValue(Model.BasicTypeNodes.GetCachedImmutableType("void")!);
        }

        private IRuntimeValue CastValue(IRuntimeValue operand, string fromType, string toType)
        {
            if (fromType == toType) return operand;

            // Value-class -> interface boxing COPIES the struct (native
            // emit_box heap-copies into a fresh GC allocation); later mutation
            // of the original is not visible through the interface value.
            if (fromType.StartsWith("struct<") && toType.StartsWith("ref<"))
                return RuntimeValueCopier.CopyIfValueSemantic(operand, Global);

            if (toType == "ref<string>" || toType == "string")
            {
                var strType = Model.BasicTypeNodes.GetCachedImmutableType("ref<string>") ?? Model.BasicTypeNodes.String.ToType(Mutability.Immutable);
                if (operand is BasicRuntimeValue bv)
                {
                    if (fromType == "bool")
                        return new BasicRuntimeValue(strType) { StringValue = bv.BoolValue ? "true" : "false" };
                    // Static dispatch — no dynamic/CallSite allocation
                    return new BasicRuntimeValue(strType) { StringValue = StaticToString(bv) };
                }
                if (operand is EnumRuntimeValue ev)
                {
                    var enumInt = -1;
                    if (ev.FieldsValue.Fields.TryGetValue("_value", out var v))
                        enumInt = v.As<BasicRuntimeValue>().I32Value;
                    var enumNode = ev.TypeInfo.TypeNode as IEnumNode;
                    var enumName = enumNode?.EnumDeclarations.Find(d => d.Value == enumInt)?.Name ?? enumInt.ToString();
                    if (ev.ContainingValue != null)
                        return new BasicRuntimeValue(strType) { StringValue = $"{enumName}({ev.ContainingValue})" };
                    return new BasicRuntimeValue(strType) { StringValue = enumName };
                }
            }

            if (operand is not BasicRuntimeValue bv2) return operand;

            var targetType = Model.BasicTypeNodes.GetCachedImmutableType(toType);
            if (targetType == null) return operand;

            // Direct field-to-field copy — no boxing via DynamicValue
            var castResult = new BasicRuntimeValue(targetType);
            CopyValueDirect(castResult, bv2);
            return castResult;
        }

        private IRuntimeValue EvalBinOp(string op, IRuntimeValue left, IRuntimeValue right, string irType)
        {
            if (left is not BasicRuntimeValue lbv || right is not BasicRuntimeValue rbv)
                throw new BabyPenguinRuntimeException($"Cannot evaluate binary op on non-basic values (op={op} irType={irType} left={left.GetType().Name}:{left.TypeInfo?.FullName()} right={right.GetType().Name}:{right.TypeInfo?.FullName()} func={CodeContainer?.FullName()})", code: ErrorCode.E_RUNTIME_INVALID_OP);

            // Determine result type from irType (matches original behavior)
            var resultTypeInfo = GetResultTypeInfo(irType);
            var result = new BasicRuntimeValue(resultTypeInfo);

            // Fast path: string concatenation
            if (irType == "ref<string>" && op == "add")
            {
                result.StringValue = (lbv.StringValue ?? "") + (rbv.StringValue ?? "");
                return result;
            }

            // Direct field-to-field operations — no boxing via DynamicValue.
            var lt = lbv.TypeInfo.Type;
            switch (op)
            {
                case "add": BinOpAdd(result, lt, lbv, rbv); break;
                case "sub": BinOpSub(result, lt, lbv, rbv); break;
                case "mul": BinOpMul(result, lt, lbv, rbv); break;
                case "div": BinOpDiv(result, lt, lbv, rbv); break;
                case "mod": BinOpMod(result, lt, lbv, rbv); break;
                case "band": BinOpBand(result, lt, lbv, rbv); break;
                case "bor": BinOpBor(result, lt, lbv, rbv); break;
                case "bxor": BinOpBxor(result, lt, lbv, rbv); break;
                case "eq": BinCmpEq(result, lt, lbv, rbv); break;
                case "ne": BinCmpNe(result, lt, lbv, rbv); break;
                case "lt": BinCmpLt(result, lt, lbv, rbv); break;
                case "gt": BinCmpGt(result, lt, lbv, rbv); break;
                case "le": BinCmpLe(result, lt, lbv, rbv); break;
                case "ge": BinCmpGe(result, lt, lbv, rbv); break;
                case "land": result.BoolValue = ToBoolFromAny(lbv) && ToBoolFromAny(rbv); break;
                case "lor": result.BoolValue = ToBoolFromAny(lbv) || ToBoolFromAny(rbv); break;
                default: throw new BabyPenguinRuntimeException($"Unknown binary op: {op}", code: ErrorCode.E_RUNTIME_INVALID_OP);
            }
            return result;
        }

        // Arithmetic helpers — direct field read/write, no boxing via DynamicValue/object?
        // Arithmetic helpers -- direct field read/write, no boxing via DynamicValue/object?
        // Left operand is read from its native field (fast path: same type as result).
        // Right operand may differ in type (e.g. i64 + u8 literal), so read via ReadAsLong/ReadAsDouble.
        private static void BinOpAdd(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue | (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value + ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value + ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value + ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value + ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value + ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value + ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value + ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value + (ulong)ReadAsLong(r))); break;
                case TypeEnum.Float: res.FloatValue = l.FloatValue + (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.DoubleValue = l.DoubleValue + ReadAsDouble(r); break;
                case TypeEnum.Char: res.CharValue = unchecked((char)(l.CharValue + ReadAsLong(r))); break;
            }
        }
        private static void BinOpSub(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value - ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value - ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value - ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value - ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value - ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value - ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value - ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value - (ulong)ReadAsLong(r))); break;
                case TypeEnum.Float: res.FloatValue = l.FloatValue - (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.DoubleValue = l.DoubleValue - ReadAsDouble(r); break;
                case TypeEnum.Char: res.CharValue = unchecked((char)(l.CharValue - ReadAsLong(r))); break;
            }
        }
        private static void BinOpMul(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value * ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value * ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value * ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value * ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value * ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value * ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value * ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value * (ulong)ReadAsLong(r))); break;
                case TypeEnum.Float: res.FloatValue = l.FloatValue * (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.DoubleValue = l.DoubleValue * ReadAsDouble(r); break;
            }
        }
        private static void BinOpDiv(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value / ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value / ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value / ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value / ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value / ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value / ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value / ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value / (ulong)ReadAsLong(r))); break;
                case TypeEnum.Float: res.FloatValue = l.FloatValue / (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.DoubleValue = l.DoubleValue / ReadAsDouble(r); break;
            }
        }
        private static void BinOpMod(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value % ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value % ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value % ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value % ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value % ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value % ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value % ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value % (ulong)ReadAsLong(r))); break;
                case TypeEnum.Float: res.FloatValue = l.FloatValue % (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.DoubleValue = l.DoubleValue % ReadAsDouble(r); break;
            }
        }
        private static void BinOpBand(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue & (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value & ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value & ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value & ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value & ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value & ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value & ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value & ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value & (ulong)ReadAsLong(r))); break;
            }
        }
        private static void BinOpBor(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue | (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value | ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value | ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value | ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value | ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value | ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value | ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value | ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value | (ulong)ReadAsLong(r))); break;
            }
        }
        private static void BinOpBxor(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue ^ (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.I8Value = unchecked((sbyte)(l.I8Value ^ ReadAsLong(r))); break;
                case TypeEnum.I16: res.I16Value = unchecked((short)(l.I16Value ^ ReadAsLong(r))); break;
                case TypeEnum.I32: res.I32Value = unchecked((int)(l.I32Value ^ ReadAsLong(r))); break;
                case TypeEnum.I64: res.I64Value = l.I64Value ^ ReadAsLong(r); break;
                case TypeEnum.U8: res.U8Value = unchecked((byte)(l.U8Value ^ ReadAsLong(r))); break;
                case TypeEnum.U16: res.U16Value = unchecked((ushort)(l.U16Value ^ ReadAsLong(r))); break;
                case TypeEnum.U32: res.U32Value = unchecked((uint)(l.U32Value ^ ReadAsLong(r))); break;
                case TypeEnum.U64: res.U64Value = unchecked((ulong)(l.U64Value ^ (ulong)ReadAsLong(r))); break;
            }
        }
        // Comparison helpers -- write result.BoolValue directly, no boxing
        // Right operand read via ReadAsLong/ReadAsDouble to handle type mismatches
        private static void BinCmpEq(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue == (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.BoolValue = l.I8Value == unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value == unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value == unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value == ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value == unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value == unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value == unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value == unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue == (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue == ReadAsDouble(r); break;
                case TypeEnum.Char: res.BoolValue = l.CharValue == unchecked((char)ReadAsLong(r)); break;
                case TypeEnum.String: res.BoolValue = l.StringValue == r.StringValue; break;
                default: res.BoolValue = false; break;
            }
        }
        private static void BinCmpNe(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.Bool: res.BoolValue = l.BoolValue != (ReadAsLong(r) != 0); break;
                case TypeEnum.I8: res.BoolValue = l.I8Value != unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value != unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value != unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value != ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value != unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value != unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value != unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value != unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue != (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue != ReadAsDouble(r); break;
                case TypeEnum.Char: res.BoolValue = l.CharValue != unchecked((char)ReadAsLong(r)); break;
                case TypeEnum.String: res.BoolValue = l.StringValue != r.StringValue; break;
                default: res.BoolValue = false; break;
            }
        }
        private static void BinCmpLt(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.BoolValue = l.I8Value < unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value < unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value < unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value < ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value < unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value < unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value < unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value < unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue < (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue < ReadAsDouble(r); break;
                default: res.BoolValue = false; break;
            }
        }
        private static void BinCmpGt(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.BoolValue = l.I8Value > unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value > unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value > unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value > ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value > unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value > unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value > unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value > unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue > (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue > ReadAsDouble(r); break;
                default: res.BoolValue = false; break;
            }
        }
        private static void BinCmpLe(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.BoolValue = l.I8Value <= unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value <= unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value <= unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value <= ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value <= unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value <= unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value <= unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value <= unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue <= (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue <= ReadAsDouble(r); break;
                default: res.BoolValue = false; break;
            }
        }
        private static void BinCmpGe(BasicRuntimeValue res, TypeEnum t, BasicRuntimeValue l, BasicRuntimeValue r)
        {
            switch (t)
            {
                case TypeEnum.I8: res.BoolValue = l.I8Value >= unchecked((sbyte)ReadAsLong(r)); break;
                case TypeEnum.I16: res.BoolValue = l.I16Value >= unchecked((short)ReadAsLong(r)); break;
                case TypeEnum.I32: res.BoolValue = l.I32Value >= unchecked((int)ReadAsLong(r)); break;
                case TypeEnum.I64: res.BoolValue = l.I64Value >= ReadAsLong(r); break;
                case TypeEnum.U8: res.BoolValue = l.U8Value >= unchecked((byte)ReadAsLong(r)); break;
                case TypeEnum.U16: res.BoolValue = l.U16Value >= unchecked((ushort)ReadAsLong(r)); break;
                case TypeEnum.U32: res.BoolValue = l.U32Value >= unchecked((uint)ReadAsLong(r)); break;
                case TypeEnum.U64: res.BoolValue = l.U64Value >= unchecked((ulong)ReadAsLong(r)); break;
                case TypeEnum.Float: res.BoolValue = l.FloatValue >= (float)ReadAsDouble(r); break;
                case TypeEnum.Double: res.BoolValue = l.DoubleValue >= ReadAsDouble(r); break;
                default: res.BoolValue = false; break;
            }
        }

        /// <summary>
        /// Direct field-to-field copy for CastValue -- no boxing via DynamicValue.
        /// </summary>
        private static void CopyValueDirect(BasicRuntimeValue target, BasicRuntimeValue source)
        {
            var tt = target.TypeInfo.Type;
            switch (tt)
            {
                case TypeEnum.Bool: target.BoolValue = source.TypeInfo.Type == TypeEnum.Bool ? source.BoolValue : ReadAsLong(source) != 0; break;
                case TypeEnum.I8: target.I8Value = unchecked((sbyte)ReadAsLong(source)); break;
                case TypeEnum.I16: target.I16Value = unchecked((short)ReadAsLong(source)); break;
                case TypeEnum.I32: target.I32Value = unchecked((int)ReadAsLong(source)); break;
                case TypeEnum.I64: target.I64Value = ReadAsLong(source); break;
                case TypeEnum.U8: target.U8Value = unchecked((byte)ReadAsLong(source)); break;
                case TypeEnum.U16: target.U16Value = unchecked((ushort)ReadAsLong(source)); break;
                case TypeEnum.U32: target.U32Value = unchecked((uint)ReadAsLong(source)); break;
                case TypeEnum.U64: target.U64Value = unchecked((ulong)ReadAsLong(source)); break;
                case TypeEnum.Float: target.FloatValue = (float)ReadAsDouble(source); break;
                case TypeEnum.Double: target.DoubleValue = ReadAsDouble(source); break;
                case TypeEnum.Char: target.CharValue = unchecked((char)ReadAsLong(source)); break;
                case TypeEnum.String: target.StringValue = source.StringValue; break;
            }
        }

        /// Read any integer-typed BasicRuntimeValue as a long (widened, no boxing)
        private static long ReadAsLong(BasicRuntimeValue v) => v.TypeInfo.Type switch
        {
            TypeEnum.Bool => v.BoolValue ? 1 : 0,
            TypeEnum.I8 => v.I8Value,
            TypeEnum.I16 => v.I16Value,
            TypeEnum.I32 => v.I32Value,
            TypeEnum.I64 => v.I64Value,
            TypeEnum.U8 => v.U8Value,
            TypeEnum.U16 => v.U16Value,
            TypeEnum.U32 => v.U32Value,
            TypeEnum.U64 => unchecked((long)v.U64Value),
            TypeEnum.Char => v.CharValue,
            _ => 0
        };

        /// Read any float/double-typed BasicRuntimeValue as double (widened, no boxing)
        private static double ReadAsDouble(BasicRuntimeValue v) => v.TypeInfo.Type switch
        {
            TypeEnum.Float => v.FloatValue,
            TypeEnum.Double => v.DoubleValue,
            _ => ReadAsLong(v)
        };

        private IType GetResultTypeInfo(string irType) => irType switch
        {
            "bool" => Model.BasicTypeNodes.GetCachedImmutableType("bool")!,
            "i8" => Model.BasicTypeNodes.GetCachedImmutableType("i8")!,
            "i16" => Model.BasicTypeNodes.GetCachedImmutableType("i16")!,
            "i32" => Model.BasicTypeNodes.GetCachedImmutableType("i32")!,
            "i64" => Model.BasicTypeNodes.GetCachedImmutableType("i64")!,
            "f32" => Model.BasicTypeNodes.GetCachedImmutableType("f32")!,
            "f64" => Model.BasicTypeNodes.GetCachedImmutableType("f64")!,
            "u8" => Model.BasicTypeNodes.GetCachedImmutableType("u8")!,
            "u16" => Model.BasicTypeNodes.GetCachedImmutableType("u16")!,
            "u32" => Model.BasicTypeNodes.GetCachedImmutableType("u32")!,
            "u64" => Model.BasicTypeNodes.GetCachedImmutableType("u64")!,
            "char" => Model.BasicTypeNodes.GetCachedImmutableType("char")!,
            "ref<string>" or "string" => Model.BasicTypeNodes.GetCachedImmutableType("ref<string>")!,
            _ => Model.BasicTypeNodes.GetCachedImmutableType("i64")!
        };


        /// Convert any BasicRuntimeValue to bool: 0 → false, non-zero → true (matches C# (bool)cast behavior on dynamic values)
        private static bool ToBoolFromAny(BasicRuntimeValue bv) => bv.TypeInfo.Type switch
        {
            TypeEnum.Bool => bv.BoolValue,
            TypeEnum.I8 => bv.I8Value != 0,
            TypeEnum.I16 => bv.I16Value != 0,
            TypeEnum.I32 => bv.I32Value != 0,
            TypeEnum.I64 => bv.I64Value != 0,
            TypeEnum.U8 => bv.U8Value != 0,
            TypeEnum.U16 => bv.U16Value != 0,
            TypeEnum.U32 => bv.U32Value != 0,
            TypeEnum.U64 => bv.U64Value != 0,
            TypeEnum.Float => bv.FloatValue != 0,
            TypeEnum.Double => bv.DoubleValue != 0,
            TypeEnum.Char => bv.CharValue != '\0',
            _ => false
        };

        private IRuntimeValue EvalUnaryOp(string op, IRuntimeValue operand, string irType)
        {
            if (operand is not BasicRuntimeValue bv)
                throw new BabyPenguinRuntimeException("Cannot apply unary op to non-basic value", code: ErrorCode.E_RUNTIME_INVALID_OP);

            // Determine result type from irType (matches original behavior)
            var resultTypeInfo = GetResultTypeInfo(irType);
            var result = new BasicRuntimeValue(resultTypeInfo);
            var t = bv.TypeInfo.Type;

            switch (op)
            {
                case "neg":
                    {
                        // Negate operand and write to result field based on RESULT type (not operand type).
                        // Operand may differ in type (e.g. U8 literal -> I64 result).
                        var rt = result.TypeInfo.Type;
                        switch (rt)
                        {
                            case TypeEnum.I8: result.I8Value = unchecked((sbyte)(-ReadAsLong(bv))); break;
                            case TypeEnum.I16: result.I16Value = unchecked((short)(-ReadAsLong(bv))); break;
                            case TypeEnum.I32: result.I32Value = unchecked((int)(-ReadAsLong(bv))); break;
                            case TypeEnum.I64: result.I64Value = -ReadAsLong(bv); break;
                            case TypeEnum.U8: result.U8Value = unchecked((byte)(-ReadAsLong(bv))); break;
                            case TypeEnum.U16: result.U16Value = unchecked((ushort)(-ReadAsLong(bv))); break;
                            case TypeEnum.U32: result.U32Value = unchecked((uint)(-(long)ReadAsLong(bv))); break;
                            case TypeEnum.U64: result.U64Value = unchecked((ulong)(-(long)ReadAsLong(bv))); break;
                            case TypeEnum.Float: result.FloatValue = -(float)ReadAsDouble(bv); break;
                            case TypeEnum.Double: result.DoubleValue = -ReadAsDouble(bv); break;
                        }
                    }
                    break;
                case "bnot":
                    {
                        // Bitwise NOT — write based on RESULT type, read operand via ReadAsLong
                        var rt = result.TypeInfo.Type;
                        switch (rt)
                        {
                            case TypeEnum.I8: result.I8Value = unchecked((sbyte)(~ReadAsLong(bv))); break;
                            case TypeEnum.I16: result.I16Value = unchecked((short)(~ReadAsLong(bv))); break;
                            case TypeEnum.I32: result.I32Value = unchecked((int)(~ReadAsLong(bv))); break;
                            case TypeEnum.I64: result.I64Value = ~ReadAsLong(bv); break;
                            case TypeEnum.U8: result.U8Value = unchecked((byte)(~ReadAsLong(bv))); break;
                            case TypeEnum.U16: result.U16Value = unchecked((ushort)(~ReadAsLong(bv))); break;
                            case TypeEnum.U32: result.U32Value = unchecked((uint)(~ReadAsLong(bv))); break;
                            case TypeEnum.U64: result.U64Value = unchecked((ulong)(~ReadAsLong(bv))); break;
                        }
                    }
                    break;
                case "lnot":
                    result.BoolValue = !bv.BoolValue;
                    break;
                case "plus":
                    // Direct field copy -- no boxing via DynamicValue
                    CopyValueDirect(result, bv);
                    break;
                default:
                    throw new BabyPenguinRuntimeException($"Unknown unary op: {op}", code: ErrorCode.E_RUNTIME_INVALID_OP);
            }
            return result;
        }

        // Static helper methods — no allocations, no dynamic, no boxing

        /// Convert any BasicRuntimeValue to string without dynamic/boxing
        private static string StaticToString(BasicRuntimeValue bv) => bv.TypeInfo.Type switch
        {
            TypeEnum.Bool => bv.BoolValue.ToString(),
            TypeEnum.U8 => bv.U8Value.ToString(),
            TypeEnum.U16 => bv.U16Value.ToString(),
            TypeEnum.U32 => bv.U32Value.ToString(),
            TypeEnum.U64 => bv.U64Value.ToString(),
            TypeEnum.I8 => bv.I8Value.ToString(),
            TypeEnum.I16 => bv.I16Value.ToString(),
            TypeEnum.I32 => bv.I32Value.ToString(),
            TypeEnum.I64 => bv.I64Value.ToString(),
            TypeEnum.Float => bv.FloatValue.ToString(),
            TypeEnum.Double => bv.DoubleValue.ToString(),
            TypeEnum.String => bv.StringValue ?? "",
            TypeEnum.Char => bv.CharValue.ToString(),
            _ => ""
        };


        // === Object / Field operations ===

        private IRuntimeValue ReadField(IRuntimeValue obj, string fieldName)
        {
            if (obj is ReferenceRuntimeValue refVal)
            {
                if (refVal.Fields.TryGetValue(fieldName, out var fieldVal))
                    return fieldVal;

                // If not a data field, try to resolve as a method on the type
                var methodFunc = TryResolveMethod(obj, fieldName);
                if (methodFunc != null)
                    return methodFunc;

                throw new BabyPenguinRuntimeException($"Field '{fieldName}' not found on {obj.TypeInfo}", code: ErrorCode.E_RUNTIME_LOOKUP);
            }
            if (obj is EnumRuntimeValue enumVal)
            {
                if (fieldName == "_value")
                    return enumVal.FieldsValue.Fields["_value"];

                // For named variant access (e.g., opt.some), return the containing value
                var variantIdx = enumVal.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value;
                if (enumVal.TypeInfo.TypeNode is IEnumNode enumNode)
                {
                    var variant = enumNode.EnumDeclarations.Find(e => e.Value == variantIdx);
                    if (variant != null && variant.Name == fieldName && enumVal.ContainingValue != null)
                        return enumVal.ContainingValue;
                }

                // Try to resolve as an enum method (e.g., is_some, is_none)
                var methodFunc = TryResolveMethod(obj, fieldName);
                if (methodFunc != null)
                    return methodFunc;

                if (enumVal.ContainingValue != null)
                    return enumVal.ContainingValue;
                throw new BabyPenguinRuntimeException($"Enum has no containing value and field '{fieldName}' not found", code: ErrorCode.E_RUNTIME_LOOKUP);
            }
            if (obj is BasicRuntimeValue)
            {
                // For primitives, resolve methods through the type's interface implementations
                var typeNode = obj.TypeInfo?.TypeNode;
                if (typeNode is IVTableContainer vtc)
                {
                    foreach (var vtable in vtc.VTables)
                    {
                        var slot = vtable.Slots.FirstOrDefault(s => s.InterfaceSymbol.Name == fieldName);
                        if (slot != null)
                            return new FunctionRuntimeValue(slot.ImplementationSymbol.TypeInfo, slot.ImplementationSymbol) { Owner = obj };
                    }
                }
                // Fallback: look up in type's own symbols
                if (typeNode is ISymbolContainer container)
                {
                    var method = container.Symbols.FirstOrDefault(s => s.IsFunction && s.Name == fieldName);
                    if (method != null)
                        return new FunctionRuntimeValue(method.TypeInfo, method) { Owner = obj };
                }
            }
            throw new BabyPenguinRuntimeException($"Cannot read field '{fieldName}' from {obj.GetType().Name} (type={obj.TypeInfo?.FullName() ?? "unknown"}) in {_function.Name} ip={_ip}", code: ErrorCode.E_RUNTIME_LOOKUP);
        }

        private FunctionRuntimeValue? TryResolveMethod(IRuntimeValue obj, string methodName)
        {
            var typeNode = obj.TypeInfo?.TypeNode;
            if (typeNode == null) return null;

            // Method dispatch fires on every method-call instruction. The resolved
            // symbol for a (type, methodName) pair is stable on a frozen model, so
            // memoize it and skip the linear Symbols/VTables scans on repeat calls.
            // Only the receiver (Owner) varies per call — it is attached afterward.
            var key = (typeNode.FullName(), methodName);
            if (Global.MethodDispatchCache.TryGetValue(key, out var cached))
            {
                if (cached == null) return null;
                return new FunctionRuntimeValue(cached.Value.TypeInfo, cached.Value.Symbol) { Owner = obj };
            }

            (IType TypeInfo, ISymbol Symbol)? found = null;

            // Look up the method in the type's own symbols (works for IClassNode and IEnumNode)
            if (typeNode is ISymbolContainer container)
            {
                var method = container.Symbols.FirstOrDefault(s => s.IsFunction && s.Name == methodName);
                if (method != null)
                {
                    var methodSym = Model.ResolveSymbol(method.FullName());
                    if (methodSym != null)
                        found = (method.TypeInfo, methodSym);
                }
            }

            // Look up in interface implementations (VTables)
            if (found == null && typeNode is IVTableContainer vtableContainer)
            {
                foreach (var vtable in vtableContainer.VTables)
                {
                    var slot = vtable.Slots.FirstOrDefault(s => s.InterfaceSymbol.Name == methodName);
                    if (slot != null)
                    {
                        var implSym = slot.ImplementationSymbol;
                        found = (implSym.TypeInfo, implSym);
                        break;
                    }
                }
            }

            Global.MethodDispatchCache[key] = found;
            if (found == null) return null;
            return new FunctionRuntimeValue(found.Value.TypeInfo, found.Value.Symbol) { Owner = obj };
        }

        private void WriteField(IRuntimeValue obj, string fieldName, IRuntimeValue value)
        {
            if (obj is ReferenceRuntimeValue refVal)
            {
                // Storing into a field copies value types (native inlines the
                // struct into the field slot); reference types share.
                refVal.Fields[fieldName] = RuntimeValueCopier.CopyIfValueSemantic(value, Global);
                return;
            }
            if (obj is EnumRuntimeValue enumVal)
            {
                if (fieldName == "_containing_value")
                {
                    // Value-class payloads copy on store (native inlines the
                    // struct into the enum); reference payloads share.
                    enumVal.ContainingValue = RuntimeValueCopier.CopyIfValueSemantic(value, Global);
                    return;
                }
                enumVal.FieldsValue.Fields[fieldName] = value;
                return;
            }
            throw new BabyPenguinRuntimeException($"Cannot write field '{fieldName}' to {obj.GetType().Name}", code: ErrorCode.E_RUNTIME_TYPE);
        }

        private bool ToBool(IRuntimeValue val)
        {
            if (val is BasicRuntimeValue bv)
                return bv.BoolValue;
            var currentInst = _ip < _function.Instructions.Count ? _function.Instructions[_ip] : null;
            throw new BabyPenguinRuntimeException(
                $"Cannot convert {val.GetType().Name} to bool in function {_function.Name} at ip={_ip}, inst={currentInst?.Display() ?? "none"}", code: ErrorCode.E_RUNTIME_INVALID_OP);
        }

        private IRuntimeValue CreateNewObject(IType type, List<IRuntimeValue> args)
        {
            var fields = new Dictionary<string, IRuntimeValue>();
            if (type.TypeNode is IClassNode cls)
            {
                foreach (var field in cls.Symbols.Where(s => (!s.IsFunction || s.IsVariable) && !s.IsStatic))
                {
                    var fieldType = field.TypeInfo;
                    if (fieldType.IsSimpleValueType || fieldType.IsStringType)
                        fields[field.Name] = CreateDefault(fieldType);
                    else if (fieldType.IsClassType)
                        fields[field.Name] = CreateNewObject(fieldType, []);
                    else if (fieldType.IsEnumType)
                        fields[field.Name] = CreateDefaultEnum(fieldType);
                    else if (fieldType.IsInterfaceType)
                        fields[field.Name] = new NotInitializedRuntimeValue(fieldType);
                    else
                        fields[field.Name] = new NotInitializedRuntimeValue(fieldType);
                }

                // NOTE: Method references are NOT pre-populated here.
                // They are resolved on-demand via TryResolveMethod(), which creates
                // FunctionRuntimeValue with the correct Owner set. This saves significant
                // memory since each class instance would otherwise hold N method references
                // that are rarely all used.
            }

            // Try to reuse a pooled object instead of allocating a new one
            var pooled = Global.TryTakeFromPool();
            if (pooled != null)
            {
                pooled.Reuse(type, fields);
                return pooled;
            }
            return new ReferenceRuntimeValue(type, fields, Global);
        }

        private IRuntimeValue CreateDefault(IType type)
        {
            return new BasicRuntimeValue(type);
        }

        private IRuntimeValue CreateDefaultEnum(IType type)
        {
            if (type.TypeNode is IEnumNode)
            {
                var fieldsRef = CreatePooledRef(type);
                fieldsRef.Fields["_value"] = new BasicRuntimeValue(Model.BasicTypeNodes.I32.ToType(Mutability.Immutable));
                return new EnumRuntimeValue(type, fieldsRef, null);
            }
            return new NotInitializedRuntimeValue(type);
        }

        private IRuntimeValue CreateEnumValue(IType type, int variantIdx, IRuntimeValue? payload)
        {
            var fieldsRef = CreatePooledRef(type);
            fieldsRef.Fields["_value"] = new BasicRuntimeValue(Model.BasicTypeNodes.I32.ToType(Mutability.Immutable)) { I32Value = variantIdx };
            // Value-class payloads are stored INLINE in EmperorPenguin (struct
            // copy at construction), so mutating the source after `new E.v(p)`
            // must not alias the stored payload. Copy value-semantic payloads;
            // reference payloads stay shared (native copies the pointer).
            var stored = payload != null ? RuntimeValueCopier.CopyIfValueSemantic(payload, Global) : null;
            return new EnumRuntimeValue(type, fieldsRef, stored);
        }

        /// <summary>
        /// Create a ReferenceRuntimeValue using the object pool if available.
        /// </summary>
        private ReferenceRuntimeValue CreatePooledRef(IType type)
        {
            var pooled = Global.TryTakeFromPool();
            if (pooled != null)
            {
                pooled.Reuse(type, []);
                return pooled;
            }
            return new ReferenceRuntimeValue(type, [], Global);
        }

        private bool CheckEnumVariant(IRuntimeValue enumVal, IRuntimeValue variantIdx)
        {
            if (enumVal is EnumRuntimeValue ev && variantIdx is BasicRuntimeValue idx)
            {
                var currentIdx = ev.FieldsValue.Fields.TryGetValue("_value", out var v) ? v.As<BasicRuntimeValue>().I32Value : -1;
                return currentIdx == idx.I32Value;
            }
            return false;
        }

        private IRuntimeValue ExtractEnumPayload(IRuntimeValue enumVal)
        {
            if (enumVal is EnumRuntimeValue ev)
                return ev.ContainingValue ?? new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));
            throw new BabyPenguinRuntimeException("Cannot extract payload from non-enum value", code: ErrorCode.E_RUNTIME_INVALID_OP);
        }

        // === Helpers ===

        private ICodeContainer FindCodeContainer(string sanitizedFuncName)
        {
            // O(1) lookup via pre-built index (eliminates full semantic tree traversal)
            if (Global.CodeContainerIndex.TryGetValue(sanitizedFuncName, out var cc))
                return cc;
            throw new BabyPenguinRuntimeException($"No code container found for function '{sanitizedFuncName}'", code: ErrorCode.E_RUNTIME_LOOKUP);
        }

        private static string SanitizeName(string name) => name.Replace(".", "_");

        private static IRSourceLocation GetLocation(IRInstruction inst)
        {
            return inst switch
            {
                IRConstInst ci => ci.Location,
                IRArgInst ai => ai.Location,
                IRAssignInst ai => ai.Location,
                IRCastInst ci => ci.Location,
                IRBinOpInst bi => bi.Location,
                IRUnaryOpInst ui => ui.Location,
                IRRdmbrInst ri => ri.Location,
                IRWrmbrInst wi => wi.Location,
                IRBrInst bi => bi.Location,
                IRBrCondInst bi => bi.Location,
                IRRetInst ri => ri.Location,
                IRRetVoidInst ri => ri.Location,
                IRCallInst ci => ci.Location,
                IRCallVoidInst ci => ci.Location,
                IRNewInst ni => ni.Location,
                IRNewEnumInst ni => ni.Location,
                IRIsEnumInst ii => ii.Location,
                IRRdenumInst ri => ri.Location,
                IRGlobalLoadInst gi => gi.Location,
                IRGlobalStoreInst gi => gi.Location,
                _ => IRSourceLocation.Empty
            };
        }

        private static string UnescapeString(string input)
        {
            var result = new System.Text.StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    var next = input[i + 1];
                    switch (next)
                    {
                        case 'n': result.Append('\n'); i++; break;
                        case 't': result.Append('\t'); i++; break;
                        case 'r': result.Append('\r'); i++; break;
                        case '\\': result.Append('\\'); i++; break;
                        case '"': result.Append('"'); i++; break;
                        case '0': result.Append('\0'); i++; break;
                        default: result.Append(input[i]); break;
                    }
                }
                else
                {
                    result.Append(input[i]);
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// Minimal ISymbol implementation for creating extern function result symbols.
        /// </summary>
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

    /// <summary>
    /// Simple wrapper to adapt IRuntimeValue to IRuntimeSymbol for RuntimeFrameResult.
    /// </summary>
    public class SimpleRuntimeSymbol : IRuntimeSymbol
    {
        private readonly IRuntimeValue _value;
        private readonly SemanticModel _model;

        public SimpleRuntimeSymbol(IRuntimeValue value, SemanticModel model)
        {
            _value = value;
            _model = model;
        }

        public SemanticModel Model => _model;
        public IType TypeInfo => _value.TypeInfo;
        public ISymbol Symbol => throw new NotImplementedException();
        public IRuntimeValue Value => _value;

        public void AssignFrom(IRuntimeSymbol other) => throw new NotImplementedException();
        public void AssignFrom(IRuntimeValue other) => throw new NotImplementedException();
        public IRuntimeSymbol Clone() => new SimpleRuntimeSymbol(_value.Clone(), _model);
    }

    public class ProgramExitException : Exception { }
}
