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
        private readonly Dictionary<string, IRuntimeValue> _namedRegisters = [];
        private readonly Dictionary<int, IRuntimeValue> _tempRegisters = [];
        private readonly Dictionary<string, int> _labelMap = [];
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
                ?? throw new BabyPenguinRuntimeException($"No IR function found for {container.FullName()} (tried {sanitizedName})");

            for (int i = 0; i < _function.Instructions.Count; i++)
            {
                if (_function.Instructions[i] is IRLabelInst labelInst)
                    _labelMap[labelInst.Label.Name] = i;
            }

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
            while (_ip < _function.Instructions.Count && !_hasReturned)
            {
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
                            if (ai.ParamIndex < _arguments.Count)
                                Store(ai.Result, MaybeCopy(_arguments[ai.ParamIndex], ai.IrType));
                        }
                        break;

                    case IRAssignInst ai:
                        {
                            var src = Resolve(ai.Src);
                            Store(ai.Dest, MaybeCopy(src, ai.Dest.GetIrType()));
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
                            Store(ri.Result, MaybeCopy(fieldVal, ri.IrType));
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
                                // Dump all named registers
                                foreach (var kvp in _namedRegisters)
                                    Global.DebugFunc($"    {kvp.Key} = {kvp.Value.GetType().Name}\n");
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
                                    throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found");
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
                                    throw new BabyPenguinRuntimeException($"Function '{ci.FuncName}' not found");
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
                            Store(ri.Result, payload);
                        }
                        break;

                    case IRGlobalLoadInst gi:
                        {
                            if (Global.GlobalVariables.TryGetValue(gi.GlobalName, out var globalSym))
                                Store(gi.Result, MaybeCopy(globalSym.Value, gi.IrType));
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

                    case IRIsInstanceInst:
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

        // === Resolve / Store ===

        private IRuntimeValue Resolve(IRValue val)
        {
            return val switch
            {
                IRNamedRegister nr => _namedRegisters.TryGetValue(nr.Name, out var v) ? v : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                IRTempRegister tr => _tempRegisters.TryGetValue(tr.Index, out var v) ? v : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                IRConstant c => ResolveConstant(c),
                IRGlobalRef g => Global.GlobalVariables.TryGetValue(g.Name, out var sym) ? sym.Value : new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable)),
                _ => throw new BabyPenguinRuntimeException($"Cannot resolve IR value: {val.Display()}")
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

                // Try matching by sanitized code container names
                foreach (var node in Model.FindAll(n => n is ICodeContainer))
                {
                    var cc = (ICodeContainer)node;
                    if (SanitizeName(cc.FullName()) == c.Value)
                    {
                        var sym = Model.ResolveSymbol(cc.FullName());
                        if (sym != null)
                            return new FunctionRuntimeValue(sym.TypeInfo, sym);
                    }
                }

                // Try matching by sanitized symbol full names (for VTable functions, etc.)
                foreach (var sym in Model.Symbols)
                {
                    if (sym.IsFunction && SanitizeName(sym.FullName()) == c.Value)
                        return new FunctionRuntimeValue(sym.TypeInfo, sym);
                }

                throw new BabyPenguinRuntimeException($"Cannot resolve function pointer: {c.Value}");
            }
            return MakeValue(c.Value, c.IrType);
        }

        private void Store(IRValue target, IRuntimeValue value)
        {
            switch (target)
            {
                case IRNamedRegister nr:
                    _namedRegisters[nr.Name] = value;
                    // Sync with LocalVariables for DAP variable inspection (best-effort)
                    if (value is not NotInitializedRuntimeValue && LocalVariables.TryGetValue(nr.Name, out var sym))
                    {
                        try { if (value.TypeInfo.CanImplicitlyCastTo(sym.TypeInfo)) sym.AssignFrom(value); }
                        catch { /* skip sync on type mismatch */ }
                    }
                    break;
                case IRTempRegister tr:
                    _tempRegisters[tr.Index] = value;
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
                throw new BabyPenguinRuntimeException($"Extern function {funcName} yielded break: {brk.Reason}");
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
                throw new BabyPenguinRuntimeException($"Extern function {funcName} yielded break: {brk.Reason}");
            }
            return (true, false);
        }

        private Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>? FindExternFunction(string funcName)
        {
            // Try direct lookup
            if (Global.ExternFunctions.TryGetValue(funcName, out var func))
                return func;

            // Try matching by sanitizing dict keys
            foreach (var kvp in Global.ExternFunctions)
            {
                if (SanitizeName(kvp.Key) == funcName)
                    return kvp.Value;
            }

            return null;
        }

        private IRuntimeSymbol CreateResultSymbol(IType returnType)
        {
            var fakeSymbol = new ExternResultSymbol(returnType);
            return IRuntimeSymbol.FromSymbol(Model, fakeSymbol, Global);
        }

        private IType IrTypeToType(string irType)
        {
            return irType switch
            {
                "bool" => Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable),
                "i8" => Model.BasicTypeNodes.I8.ToType(Mutability.Immutable),
                "i16" => Model.BasicTypeNodes.I16.ToType(Mutability.Immutable),
                "i32" => Model.BasicTypeNodes.I32.ToType(Mutability.Immutable),
                "i64" => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable),
                "u8" => Model.BasicTypeNodes.U8.ToType(Mutability.Immutable),
                "u16" => Model.BasicTypeNodes.U16.ToType(Mutability.Immutable),
                "u32" => Model.BasicTypeNodes.U32.ToType(Mutability.Immutable),
                "u64" => Model.BasicTypeNodes.U64.ToType(Mutability.Immutable),
                "f32" => Model.BasicTypeNodes.Float.ToType(Mutability.Immutable),
                "f64" => Model.BasicTypeNodes.Double.ToType(Mutability.Immutable),
                "char" => Model.BasicTypeNodes.Char.ToType(Mutability.Immutable),
                "void" => Model.BasicTypeNodes.Void.ToType(Mutability.Immutable),
                "string" or "ref<string>" => Model.BasicTypeNodes.String.ToType(Mutability.Mutable),
                _ when irType.StartsWith("enum<") => ResolveComplexType(irType),
                _ when irType.StartsWith("ref<") => ResolveComplexType(irType),
                _ when irType.StartsWith("struct<") => ResolveComplexType(irType),
                _ => Model.BasicTypeNodes.Void.ToType(Mutability.Immutable),
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

        private IRuntimeValue MaybeCopy(IRuntimeValue val, string _)
        {
            // All values are shared (reference semantics) like the old VM.
            return val;
        }

        private IRuntimeValue MakeValue(string literal, string irType)
        {
            if (irType == "bool")
                return new BasicRuntimeValue(Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable)) { BoolValue = literal == "true" };

            if (irType == "string" || irType == "ref<string>")
            {
                var type = Model.BasicTypeNodes.String.ToType(Mutability.Immutable);
                var val = literal;
                if (val.StartsWith("\"") && val.EndsWith("\""))
                    val = UnescapeString(val[1..^1]);
                return new BasicRuntimeValue(type) { StringValue = val };
            }

            if (irType == "char")
                return new BasicRuntimeValue(Model.BasicTypeNodes.Char.ToType(Mutability.Immutable)) { CharValue = literal.Length > 0 ? literal[0] : '\0' };

            if (irType == "void")
                return new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));

            var numericType = irType switch
            {
                "i8" => Model.BasicTypeNodes.I8.ToType(Mutability.Immutable),
                "i16" => Model.BasicTypeNodes.I16.ToType(Mutability.Immutable),
                "i32" => Model.BasicTypeNodes.I32.ToType(Mutability.Immutable),
                "i64" => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable),
                "u8" => Model.BasicTypeNodes.U8.ToType(Mutability.Immutable),
                "u16" => Model.BasicTypeNodes.U16.ToType(Mutability.Immutable),
                "u32" => Model.BasicTypeNodes.U32.ToType(Mutability.Immutable),
                "u64" => Model.BasicTypeNodes.U64.ToType(Mutability.Immutable),
                "f32" => Model.BasicTypeNodes.Float.ToType(Mutability.Immutable),
                "f64" => Model.BasicTypeNodes.Double.ToType(Mutability.Immutable),
                _ => null
            };

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
                return new BasicRuntimeValue(Model.BasicTypeNodes.I64.ToType(Mutability.Immutable)) { I64Value = intVal };

            return new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));
        }

        private IRuntimeValue CastValue(IRuntimeValue operand, string fromType, string toType)
        {
            if (fromType == toType) return operand;

            if (toType == "ref<string>" || toType == "string")
            {
                var strType = Model.BasicTypeNodes.String.ToType(Mutability.Immutable);
                if (operand is BasicRuntimeValue bv)
                {
                    if (fromType == "bool")
                        return new BasicRuntimeValue(strType) { StringValue = bv.BoolValue ? "true" : "false" };
                    return new BasicRuntimeValue(strType) { StringValue = bv.DynamicValue?.ToString() ?? "" };
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

            var targetType = toType switch
            {
                "i8" => Model.BasicTypeNodes.I8.ToType(Mutability.Immutable),
                "i16" => Model.BasicTypeNodes.I16.ToType(Mutability.Immutable),
                "i32" => Model.BasicTypeNodes.I32.ToType(Mutability.Immutable),
                "i64" => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable),
                "u8" => Model.BasicTypeNodes.U8.ToType(Mutability.Immutable),
                "u16" => Model.BasicTypeNodes.U16.ToType(Mutability.Immutable),
                "u32" => Model.BasicTypeNodes.U32.ToType(Mutability.Immutable),
                "u64" => Model.BasicTypeNodes.U64.ToType(Mutability.Immutable),
                "f32" => Model.BasicTypeNodes.Float.ToType(Mutability.Immutable),
                "f64" => Model.BasicTypeNodes.Double.ToType(Mutability.Immutable),
                "bool" => Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable),
                _ => null
            };

            if (targetType != null)
            {
                var castResult = new BasicRuntimeValue(targetType);
                switch (toType)
                {
                    case "i8": castResult.I8Value = Convert.ToSByte(bv2.DynamicValue); break;
                    case "i16": castResult.I16Value = Convert.ToInt16(bv2.DynamicValue); break;
                    case "i32": castResult.I32Value = Convert.ToInt32(bv2.DynamicValue); break;
                    case "i64": castResult.I64Value = Convert.ToInt64(bv2.DynamicValue); break;
                    case "u8": castResult.U8Value = Convert.ToByte(bv2.DynamicValue); break;
                    case "u16": castResult.U16Value = Convert.ToUInt16(bv2.DynamicValue); break;
                    case "u32": castResult.U32Value = Convert.ToUInt32(bv2.DynamicValue); break;
                    case "u64": castResult.U64Value = Convert.ToUInt64(bv2.DynamicValue); break;
                    case "f32": castResult.FloatValue = Convert.ToSingle(bv2.DynamicValue); break;
                    case "f64": castResult.DoubleValue = Convert.ToDouble(bv2.DynamicValue); break;
                    case "bool": castResult.BoolValue = Convert.ToBoolean(bv2.DynamicValue); break;
                }
                return castResult;
            }

            // Interface/class casts - pass through
            return operand;
        }

        private IRuntimeValue EvalBinOp(string op, IRuntimeValue left, IRuntimeValue right, string irType)
        {
            if (left is BasicRuntimeValue lbv && right is BasicRuntimeValue rbv)
            {
                var ld = lbv.DynamicValue;
                var rd = rbv.DynamicValue;

                var resultType = irType switch
                {
                    "bool" => Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable),
                    "i8" => Model.BasicTypeNodes.I8.ToType(Mutability.Immutable),
                    "i16" => Model.BasicTypeNodes.I16.ToType(Mutability.Immutable),
                    "i32" => Model.BasicTypeNodes.I32.ToType(Mutability.Immutable),
                    "i64" => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable),
                    "u8" => Model.BasicTypeNodes.U8.ToType(Mutability.Immutable),
                    "u16" => Model.BasicTypeNodes.U16.ToType(Mutability.Immutable),
                    "u32" => Model.BasicTypeNodes.U32.ToType(Mutability.Immutable),
                    "u64" => Model.BasicTypeNodes.U64.ToType(Mutability.Immutable),
                    "f32" => Model.BasicTypeNodes.Float.ToType(Mutability.Immutable),
                    "f64" => Model.BasicTypeNodes.Double.ToType(Mutability.Immutable),
                    "ref<string>" => Model.BasicTypeNodes.String.ToType(Mutability.Immutable),
                    _ => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable)
                };

                var result = new BasicRuntimeValue(resultType);

                if (irType == "ref<string>" && op == "add")
                {
                    result.StringValue = (lbv.StringValue ?? "") + (rbv.StringValue ?? "");
                    return result;
                }

                switch (op)
                {
                    case "add": result.DynamicValue = (dynamic)ld! + (dynamic)rd!; break;
                    case "sub": result.DynamicValue = (dynamic)ld! - (dynamic)rd!; break;
                    case "mul": result.DynamicValue = (dynamic)ld! * (dynamic)rd!; break;
                    case "div": result.DynamicValue = (dynamic)ld! / (dynamic)rd!; break;
                    case "mod": result.DynamicValue = (dynamic)ld! % (dynamic)rd!; break;
                    case "band": result.DynamicValue = (dynamic)ld! & (dynamic)rd!; break;
                    case "bor": result.DynamicValue = (dynamic)ld! | (dynamic)rd!; break;
                    case "bxor": result.DynamicValue = (dynamic)ld! ^ (dynamic)rd!; break;
                    case "land": result.DynamicValue = (bool)ld! && (bool)rd!; break;
                    case "lor": result.DynamicValue = (bool)ld! || (bool)rd!; break;
                    case "eq": result.DynamicValue = (dynamic)ld! == (dynamic)rd!; break;
                    case "ne": result.DynamicValue = (dynamic)ld! != (dynamic)rd!; break;
                    case "lt": result.DynamicValue = (dynamic)ld! < (dynamic)rd!; break;
                    case "gt": result.DynamicValue = (dynamic)ld! > (dynamic)rd!; break;
                    case "le": result.DynamicValue = (dynamic)ld! <= (dynamic)rd!; break;
                    case "ge": result.DynamicValue = (dynamic)ld! >= (dynamic)rd!; break;
                    default: throw new BabyPenguinRuntimeException($"Unknown binary op: {op}");
                }

                return result;
            }

            throw new BabyPenguinRuntimeException($"Cannot evaluate binary op on non-basic values");
        }

        private IRuntimeValue EvalUnaryOp(string op, IRuntimeValue operand, string irType)
        {
            if (operand is not BasicRuntimeValue bv)
                throw new BabyPenguinRuntimeException($"Cannot apply unary op to non-basic value");

            var resultType = irType switch
            {
                "bool" => Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable),
                "i8" => Model.BasicTypeNodes.I8.ToType(Mutability.Immutable),
                "i16" => Model.BasicTypeNodes.I16.ToType(Mutability.Immutable),
                "i32" => Model.BasicTypeNodes.I32.ToType(Mutability.Immutable),
                "i64" => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable),
                "f32" => Model.BasicTypeNodes.Float.ToType(Mutability.Immutable),
                "f64" => Model.BasicTypeNodes.Double.ToType(Mutability.Immutable),
                _ => Model.BasicTypeNodes.I64.ToType(Mutability.Immutable)
            };

            var result = new BasicRuntimeValue(resultType);
            switch (op)
            {
                case "neg": result.DynamicValue = -(dynamic)bv.DynamicValue!; break;
                case "bnot": result.DynamicValue = ~(dynamic)bv.DynamicValue!; break;
                case "lnot": result.BoolValue = !bv.BoolValue; break;
                case "plus": result.DynamicValue = bv.DynamicValue; break;
                default: throw new BabyPenguinRuntimeException($"Unknown unary op: {op}");
            }
            return result;
        }

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

                throw new BabyPenguinRuntimeException($"Field '{fieldName}' not found on {obj.TypeInfo}");
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
                throw new BabyPenguinRuntimeException($"Enum has no containing value and field '{fieldName}' not found");
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
            throw new BabyPenguinRuntimeException($"Cannot read field '{fieldName}' from {obj.GetType().Name} (type={obj.TypeInfo?.FullName() ?? "unknown"}) in {_function.Name} ip={_ip}");
        }

        private FunctionRuntimeValue? TryResolveMethod(IRuntimeValue obj, string methodName)
        {
            var typeNode = obj.TypeInfo?.TypeNode;
            if (typeNode == null) return null;

            // Look up the method in the type's own symbols (works for IClassNode and IEnumNode)
            if (typeNode is ISymbolContainer container)
            {
                var method = container.Symbols.FirstOrDefault(s => s.IsFunction && s.Name == methodName);
                if (method != null)
                {
                    var methodSym = Model.ResolveSymbol(method.FullName());
                    if (methodSym != null)
                        return new FunctionRuntimeValue(method.TypeInfo, methodSym) { Owner = obj };
                }
            }

            // Look up in interface implementations (VTables)
            if (typeNode is IVTableContainer vtableContainer)
            {
                foreach (var vtable in vtableContainer.VTables)
                {
                    var slot = vtable.Slots.FirstOrDefault(s => s.InterfaceSymbol.Name == methodName);
                    if (slot != null)
                    {
                        var implSym = slot.ImplementationSymbol;
                        return new FunctionRuntimeValue(implSym.TypeInfo, implSym) { Owner = obj };
                    }
                }
            }

            return null;
        }

        private void WriteField(IRuntimeValue obj, string fieldName, IRuntimeValue value)
        {
            if (obj is ReferenceRuntimeValue refVal)
            {
                refVal.Fields[fieldName] = value;
                return;
            }
            if (obj is EnumRuntimeValue enumVal)
            {
                if (fieldName == "_containing_value")
                {
                    enumVal.ContainingValue = value;
                    return;
                }
                enumVal.FieldsValue.Fields[fieldName] = value;
                return;
            }
            throw new BabyPenguinRuntimeException($"Cannot write field '{fieldName}' to {obj.GetType().Name}");
        }

        private bool ToBool(IRuntimeValue val)
        {
            if (val is BasicRuntimeValue bv)
                return bv.BoolValue;
            var currentInst = _ip < _function.Instructions.Count ? _function.Instructions[_ip] : null;
            throw new BabyPenguinRuntimeException(
                $"Cannot convert {val.GetType().Name} to bool in function {_function.Name} at ip={_ip}, inst={currentInst?.Display() ?? "none"}");
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

                // Add method references for virtual dispatch
                foreach (var method in cls.Symbols.Where(s => s.IsFunction && !s.IsVariable && !s.IsStatic))
                {
                    // Find the actual function symbol by resolving the full method name
                    var methodSym = Model.ResolveSymbol(method.FullName());
                    if (methodSym != null)
                    {
                        try
                        {
                            fields[method.Name] = new FunctionRuntimeValue(method.TypeInfo, methodSym);
                        }
                        catch { /* skip methods that can't be resolved */ }
                    }
                }
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
                var fieldsRef = new ReferenceRuntimeValue(type, [], Global);
                fieldsRef.Fields["_value"] = new BasicRuntimeValue(Model.BasicTypeNodes.I32.ToType(Mutability.Immutable));
                return new EnumRuntimeValue(type, fieldsRef, null);
            }
            return new NotInitializedRuntimeValue(type);
        }

        private IRuntimeValue CreateEnumValue(IType type, int variantIdx, IRuntimeValue? payload)
        {
            var fieldsRef = new ReferenceRuntimeValue(type, [], Global);
            fieldsRef.Fields["_value"] = new BasicRuntimeValue(Model.BasicTypeNodes.I32.ToType(Mutability.Immutable)) { I32Value = variantIdx };
            return new EnumRuntimeValue(type, fieldsRef, payload);
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
            throw new BabyPenguinRuntimeException("Cannot extract payload from non-enum value");
        }

        // === Helpers ===

        private ICodeContainer FindCodeContainer(string sanitizedFuncName)
        {
            // Try to find by matching sanitized full names
            foreach (var node in Model.FindAll(n => n is ICodeContainer))
            {
                var cc = (ICodeContainer)node;
                if (SanitizeName(cc.FullName()) == sanitizedFuncName)
                    return cc;
            }
            throw new BabyPenguinRuntimeException($"No code container found for function '{sanitizedFuncName}'");
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
