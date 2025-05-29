namespace BabyPenguin.VirtualMachine
{
    public record RuntimeFrameResult(IRuntimeSymbol? ReturnValue, ReturnStatus ReturnStatus);
    public enum RuntimeBreakReason { Step, Breakpoint, Exception, Exited }
    public record RuntimeBreak(RuntimeBreakReason Reason, RuntimeFrame CurrentFrame);

    public class RuntimeFrame
    {
        public RuntimeFrame(ICodeContainer container, RuntimeGlobal runtimeGlobal, List<IRuntimeValue> parameters, RuntimeFrame? parentFrame)
        {
            CodeContainer = container;
            Global = runtimeGlobal;
            FrameLevel = parentFrame?.FrameLevel + 1 ?? 0;
            ParentFrame = parentFrame;
            Model = container.Model;
            if (ParentFrame != null)
                ParentFrame.ChildFrame = this;
            foreach (var local in container.Symbols)
            {
                if (local.IsParameter)
                {
                    var symbol = IRuntimeSymbol.FromSymbol(container.Model, local);
                    symbol.AssignFrom(parameters[local.ParameterIndex]);
                    LocalVariables.Add(local.FullName, symbol);
                }
                else
                {
                    LocalVariables.Add(local.FullName, IRuntimeSymbol.FromSymbol(container.Model, local));
                }
            }
        }

        public SemanticModel Model { get; }

        public Dictionary<string, IRuntimeSymbol> LocalVariables { get; } = [];

        public RuntimeGlobal Global { get; }

        public ICodeContainer CodeContainer { get; }

        public override string ToString()
        {
            return $"[RuntimeFrame: {CodeContainer.FullName}]";
        }

        public int FrameLevel { get; set; } = 0;

        public int InstructionPointer { get; set; } = 0;

        public RuntimeFrame? ParentFrame { get; set; }

        public RuntimeFrame? ChildFrame { get; set; }

        public SourceLocation CurrentSourceLocation => InstructionPointer >= CodeContainer.Instructions.Count ? CodeContainer.SourceLocation.EndLocation : CodeContainer.Instructions[InstructionPointer].SourceLocation;

        public IRuntimeSymbol? LastReturnVar { get; private set; }


        private void DebugPrint(BabyPenguinIR inst, string? op1 = "", string? op2 = "", string? result = "")
        {
            if (Global.EnableDebugPrint)
            {
                Global.DebugFunc(new string('|', FrameLevel) + inst.ToDebugString(op1, op2, result) + " @" + ConsoleColor.UNDERLINE + inst.SourceLocation + ConsoleColor.NOUNDERLINE + "\n");
            }
        }

        private IRuntimeSymbol resolveVariable(ISymbol symbol)
        {
            IRuntimeSymbol? result;
            if (symbol.IsLocal)
            {
                if (!LocalVariables.TryGetValue(symbol.FullName, out result))
                {
                    throw new BabyPenguinRuntimeException("Cannot find local variable " + symbol.FullName);
                }
            }
            else
            {
                if (!Global.GlobalVariables.TryGetValue(symbol.FullName, out result))
                {
                    throw new BabyPenguinRuntimeException("Cannot find global variable " + symbol.FullName);
                }
            }
            return result!;
        }

        private int findLabel(string label)
        {
            for (int i = 0; i < CodeContainer.Instructions.Count; i++)
            {
                if (CodeContainer.Instructions[i].Labels.Contains(label))
                {
                    return i;
                }
            }
            throw new BabyPenguinRuntimeException("Cannot find label " + label);
        }

        public IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>> Run()
        {
            // continue from child frame yield/async stop point
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
                            LastReturnVar?.AssignFrom(resTemp.Right!.ReturnValue);

                        if (resTemp.Right!.ReturnStatus == ReturnStatus.Finished || resTemp.Right!.ReturnStatus == ReturnStatus.YieldFinished)
                        {
                            ChildFrame = null;
                        }
                    }
                }
            }

            RuntimeFrameResult? result = null;
            while (InstructionPointer < CodeContainer.Instructions.Count)
            {
                if (InstructionPointer > 0 && (Global.StepMode == RuntimeGlobal.StepModeEnum.StepIn || Global.StepMode == RuntimeGlobal.StepModeEnum.StepOver))
                {
                    Global.StepMode = RuntimeGlobal.StepModeEnum.Run;
                    yield return new RuntimeBreak(RuntimeBreakReason.Step, this);
                }

                var command = CodeContainer.Instructions[InstructionPointer];
                switch (command)
                {
                    case AssignmentInstruction cmd:
                        {
                            IRuntimeSymbol rightVar = resolveVariable(cmd.RightHandSymbol);
                            IRuntimeSymbol resultVar = resolveVariable(cmd.LeftHandSymbol);
                            resultVar.AssignFrom(rightVar);
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case AssignLiteralToSymbolInstruction cmd:
                        {
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            switch (resultVar.Type)
                            {
                                case TypeEnum.Bool:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U8Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U16Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U32Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U64Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I8Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I16Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I32Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I64Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.FloatValue = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DoubleValue = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    break;
                                case TypeEnum.String:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.StringValue = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.CharValue = cmd.LiteralValue[0];
                                    break;
                                default:
                                    throw new BabyPenguinRuntimeException("A complex type cannot be a literal value");
                            }
                            DebugPrint(cmd, result: resultVar.ToDebugString());
                            break;
                        }
                    case FunctionCallInstruction cmd:
                        {
                            IRuntimeSymbol? retVar = cmd.Target == null ? null : resolveVariable(cmd.Target);
                            IRuntimeSymbol funVar = resolveVariable(cmd.FunctionSymbol);
                            FunctionSymbol funSymbol = funVar.As<FunctionRuntimeSymbol>().FunctionValue?.FunctionSymbol as FunctionSymbol ??
                                throw new BabyPenguinRuntimeException("The symbol is not a function: " + cmd.FunctionSymbol.FullName);
                            IRuntimeValue? funOwner = funVar.As<FunctionRuntimeSymbol>().FunctionValue?.Owner;
                            if (funOwner is NotInitializedRuntimeValue) funOwner = null;

                            var args = cmd.Arguments.Select(resolveVariable).Select(i => i.Value).ToList();
                            if (funOwner != null) args.Insert(0, funOwner);

                            DebugPrint(cmd, op1: funSymbol.FullName, op2: string.Join(", ", args.Select(arg => arg.ToString())), result: retVar?.Symbol.FullName);
                            if (!funSymbol.IsExtern)
                            {
                                LastReturnVar = retVar;
                                var newFrame = new RuntimeFrame(funSymbol.CodeContainer, Global, args, this);
                                var isStepOver = Global.StepMode == RuntimeGlobal.StepModeEnum.StepOver || Global.StepMode == RuntimeGlobal.StepModeEnum.StepOut;
                                if (isStepOver)
                                    Global.StepMode = RuntimeGlobal.StepModeEnum.Run;
                                foreach (var resTemp in newFrame.Run())
                                {
                                    if (resTemp.IsLeft)
                                    {
                                        yield return resTemp;
                                    }
                                    else
                                    {
                                        if (resTemp.Right!.ReturnStatus == ReturnStatus.Blocked)
                                        {
                                            result = new RuntimeFrameResult(null, ReturnStatus.Blocked);

                                            break;
                                        }

                                        if (resTemp.Right!.ReturnValue != null)
                                            retVar?.AssignFrom(resTemp.Right!.ReturnValue);

                                        if (resTemp.Right!.ReturnStatus == ReturnStatus.Finished || resTemp.Right!.ReturnStatus == ReturnStatus.YieldFinished)
                                        {
                                            ChildFrame = null;
                                        }
                                    }
                                }
                                if (isStepOver)
                                    yield return new RuntimeBreak(RuntimeBreakReason.Step, this);
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(funSymbol.FullName, out var action))
                                {
                                    foreach (var resTemp in action(this, retVar, args))
                                    {
                                        yield return resTemp;
                                    }
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException("Cannot find external function " + funSymbol.FullName);
                                }
                            }
                            break;
                        }
                    // case SlicingInstruction cmd:
                    //     throw new NotImplementedException();
                    case CastInstruction cmd:
                        {
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            IRuntimeSymbol rightVar = resolveVariable(cmd.Operand);
                            if (resultVar.TypeInfo != cmd.TypeInfo)
                            {
                                throw new BabyPenguinRuntimeException($"Cannot assign type {cmd.TypeInfo} to type {resultVar.TypeInfo}");
                            }
                            switch (cmd.TypeInfo.Type)
                            {
                                case TypeEnum.Void:
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U8Value = Convert.ToByte(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U16Value = Convert.ToUInt16(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U32Value = Convert.ToUInt32(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.U64Value = Convert.ToUInt64(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I8Value = Convert.ToSByte(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I16Value = Convert.ToInt16(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I32Value = Convert.ToInt32(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.I64Value = Convert.ToInt64(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.FloatValue = Convert.ToSingle(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DoubleValue = Convert.ToDouble(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.CharValue = rightVar.As<BasicRuntimeSymbol>().BasicValue.CharValue;
                                    break;
                                case TypeEnum.Bool:
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue = bool.Parse(rightVar.As<BasicRuntimeSymbol>().ValueToString!);
                                    break;
                                case TypeEnum.String:
                                    if (rightVar.TypeInfo.IsBoolType)
                                    {
                                        resultVar.As<BasicRuntimeSymbol>().BasicValue.StringValue = rightVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue ? "true" : "false";
                                    }
                                    else if (rightVar.TypeInfo.IsStringType)
                                    {
                                        resultVar.As<BasicRuntimeSymbol>().BasicValue.StringValue = rightVar.As<BasicRuntimeSymbol>().BasicValue.StringValue ?? "";
                                    }
                                    else if (rightVar.TypeInfo.IsEnumType)
                                    {
                                        var enumInt = rightVar.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value;
                                        var name = (rightVar.As<EnumRuntimeSymbol>().TypeInfo as IEnum)?.EnumDeclarations.Find(i => i.Value == enumInt)?.Name ??
                                            throw new BabyPenguinRuntimeException($"Converting unknown enum value '{enumInt}' for '{rightVar.As<EnumRuntimeSymbol>().TypeInfo.FullName}' to string.");
                                        resultVar.As<BasicRuntimeSymbol>().BasicValue.StringValue = rightVar.As<EnumRuntimeSymbol>().EnumValue.ContainingValue == null ? name : $"{name}({rightVar.As<EnumRuntimeSymbol>().EnumValue.ContainingValue})";
                                    }
                                    else
                                    {
                                        resultVar.As<BasicRuntimeSymbol>().BasicValue.StringValue = rightVar.As<BasicRuntimeSymbol>().ValueToString ?? "";
                                    }
                                    break;
                                case TypeEnum.Fun:
                                    throw new NotImplementedException();
                                case TypeEnum.Class:
                                    if (rightVar is ClassRuntimeSymbol)
                                        resultVar.As<ClassRuntimeSymbol>().AssignFrom(rightVar);
                                    else if (rightVar is InterfaceRuntimeSymbol)
                                    {
                                        if (rightVar.As<InterfaceRuntimeSymbol>().Value is ReferenceRuntimeValue rv)
                                            resultVar.As<ClassRuntimeSymbol>().ReferenceValue = rv;
                                        else throw new BabyPenguinRuntimeException($"Cannot assign interface {rightVar} to class {resultVar} because it does not contain a class value");
                                    }
                                    break;
                                case TypeEnum.Interface:
                                    if (rightVar is InterfaceRuntimeSymbol)
                                    {
                                        resultVar.As<InterfaceRuntimeSymbol>().AssignFrom(rightVar);
                                    }
                                    else
                                    {
                                        var cls = (cmd.Operand.TypeInfo as IVTableContainer) ?? throw new InvalidOperationException("Operand is not a class");
                                        var intf = (cmd.TypeInfo as IInterface) ?? throw new InvalidOperationException("TypeInfo is not an interface");
                                        var vtable = cls.VTables.FirstOrDefault(v => v.Interface.FullName == intf.FullName) ?? throw new BabyPenguinRuntimeException($"Class {cls.FullName} does not implement interface {intf.FullName}");
                                        resultVar.As<InterfaceRuntimeSymbol>().VTable = vtable;
                                        resultVar.As<InterfaceRuntimeSymbol>().Value = rightVar.Value;
                                    }
                                    break;
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case UnaryOperationInstruction cmd:
                        {
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            IRuntimeSymbol rightVar = resolveVariable(cmd.Operand);
                            if (!rightVar.TypeInfo.CanImplicitlyCastTo(resultVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case UnaryOperatorEnum.Ref:
                                case UnaryOperatorEnum.Deref:
                                    throw new NotImplementedException();
                                case UnaryOperatorEnum.Plus:
                                    resultVar.AssignFrom(rightVar);
                                    break;
                                case UnaryOperatorEnum.Minus:
                                    switch (rightVar.Type)
                                    {
                                        case TypeEnum.U8:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I8Value = (sbyte)(-rightVar.As<BasicRuntimeSymbol>().BasicValue.U8Value);
                                            break;
                                        case TypeEnum.U16:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I16Value = (short)(-rightVar.As<BasicRuntimeSymbol>().BasicValue.U16Value);
                                            break;
                                        case TypeEnum.U32:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I32Value = (int)(-rightVar.As<BasicRuntimeSymbol>().BasicValue.U32Value);
                                            break;
                                        // case TypeEnum.U64:
                                        // resultVar.As<BasicRuntimeVar>().U64Value = (ulong)(-rightVar.As<BasicRuntimeVar>().U64Value);
                                        // break;
                                        case TypeEnum.I8:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I8Value = (sbyte)(-rightVar.As<BasicRuntimeSymbol>().BasicValue.I8Value);
                                            break;
                                        case TypeEnum.I16:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I16Value = (short)(-rightVar.As<BasicRuntimeSymbol>().BasicValue.I16Value);
                                            break;
                                        case TypeEnum.I32:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I32Value = -rightVar.As<BasicRuntimeSymbol>().BasicValue.I32Value;
                                            break;
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I64Value = -rightVar.As<BasicRuntimeSymbol>().BasicValue.I64Value;
                                            break;
                                        case TypeEnum.Float:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.FloatValue = -rightVar.As<BasicRuntimeSymbol>().BasicValue.FloatValue;
                                            break;
                                        case TypeEnum.Double:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.DoubleValue = -rightVar.As<BasicRuntimeSymbol>().BasicValue.DoubleValue;
                                            break;
                                        default:
                                            throw new BabyPenguinRuntimeException($"Cannot negate type {rightVar.Type}");
                                    }
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    switch (rightVar.Type)
                                    {
                                        case TypeEnum.U8:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.U8Value = (byte)~rightVar.As<BasicRuntimeSymbol>().BasicValue.U8Value;
                                            break;
                                        case TypeEnum.U16:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.U16Value = (ushort)~rightVar.As<BasicRuntimeSymbol>().BasicValue.U16Value;
                                            break;
                                        case TypeEnum.U32:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.U32Value = ~rightVar.As<BasicRuntimeSymbol>().BasicValue.U32Value;
                                            break;
                                        case TypeEnum.U64:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.U64Value = ~rightVar.As<BasicRuntimeSymbol>().BasicValue.U64Value;
                                            break;
                                        case TypeEnum.I8:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I8Value = (sbyte)~rightVar.As<BasicRuntimeSymbol>().BasicValue.I8Value;
                                            break;
                                        case TypeEnum.I16:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I16Value = (short)~rightVar.As<BasicRuntimeSymbol>().BasicValue.I16Value;
                                            break;
                                        case TypeEnum.I32:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I32Value = ~rightVar.As<BasicRuntimeSymbol>().BasicValue.I32Value;
                                            break;
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.I64Value = ~rightVar.As<BasicRuntimeSymbol>().BasicValue.I64Value;
                                            break;
                                        case TypeEnum.Bool:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue = !rightVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue;
                                            break;
                                        default:
                                            throw new BabyPenguinRuntimeException($"Cannot bitwise not type {rightVar.Type}");
                                    }
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    switch (rightVar.Type)
                                    {
                                        case TypeEnum.Bool:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue = !rightVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue;
                                            break;
                                        case TypeEnum.U8:
                                        case TypeEnum.U16:
                                        case TypeEnum.U32:
                                        case TypeEnum.U64:
                                        case TypeEnum.I8:
                                        case TypeEnum.I16:
                                        case TypeEnum.I32:
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue = rightVar.As<BasicRuntimeSymbol>().ValueToString != "0";
                                            break;
                                        default:
                                            throw new BabyPenguinRuntimeException($"Cannot logical not type {rightVar.Type}");
                                    }
                                    break;
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case BinaryOperationInstruction cmd:
                        {
                            IRuntimeSymbol leftVar = resolveVariable(cmd.LeftSymbol);
                            IRuntimeSymbol rightVar = resolveVariable(cmd.RightSymbol);
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            if (!leftVar.TypeInfo.CanImplicitlyCastTo(rightVar.TypeInfo)
                                && !rightVar.TypeInfo.CanImplicitlyCastTo(leftVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Type {rightVar.TypeInfo} is not equal to type {leftVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case BinaryOperatorEnum.Add:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! + rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! - rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! * rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! / rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! % rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! & rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! | rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! ^ rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! && rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! || rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! == rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! != rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! > rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! >= rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! < rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! <= rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! << rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue = leftVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue! >> rightVar.As<BasicRuntimeSymbol>().BasicValue.DynamicValue!;
                                    break;
                            }
                            DebugPrint(cmd, op1: leftVar.ToDebugString(), op2: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case GotoInstruction cmd:
                        {
                            if (cmd.Condition != null)
                            {
                                IRuntimeSymbol condVar = resolveVariable(cmd.Condition);
                                if (!condVar.TypeInfo.IsBoolType)
                                    throw new BabyPenguinRuntimeException($"Cannot use type {condVar.TypeInfo} as a condition");
                                if (cmd.JumpOnCondition != condVar.As<BasicRuntimeSymbol>().BasicValue.BoolValue!)
                                {
                                    DebugPrint(cmd, op1: condVar.ToDebugString(), result: "NOT JUMP");
                                    break;
                                }
                                else
                                {
                                    InstructionPointer = findLabel(cmd.TargetLabel);
                                    DebugPrint(cmd, op1: condVar.ToDebugString(), result: $"JUMP {cmd.TargetLabel}");
                                }
                            }
                            else
                            {
                                InstructionPointer = findLabel(cmd.TargetLabel);
                                DebugPrint(cmd, result: $"JUMP {cmd.TargetLabel}");
                            }
                            break;
                        }
                    case ReturnInstruction cmd:
                        {
                            if (cmd.RetValue != null)
                            {
                                IRuntimeSymbol retVar = resolveVariable(cmd.RetValue);
                                DebugPrint(cmd, op1: retVar.ToDebugString());
                                result = new RuntimeFrameResult(retVar, cmd.ReturnStatus);
                            }
                            else
                            {
                                result = new RuntimeFrameResult(null, cmd.ReturnStatus);
                            }
                            break;
                        }
                    case NewInstanceInstruction cmd:
                        {
                            // do nothing
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            DebugPrint(cmd, result: resultVar.ToDebugString());
                            break;
                        }
                    case ReadMemberInstruction cmd:
                        {
                            IRuntimeSymbol resultVar = resolveVariable(cmd.Target);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);

                            IRuntimeValue readMember(IRuntimeValue ownerVar)
                            {
                                Dictionary<string, IRuntimeValue> fields;
                                if (ownerVar is ReferenceRuntimeValue refVal)
                                    fields = refVal.Fields;
                                else if (ownerVar is EnumRuntimeValue enumVal)
                                    fields = enumVal.FieldsValue.Fields;
                                else if (ownerVar is NotInitializedRuntimeValue)
                                    throw new BabyPenguinRuntimeException($"Cannot read member {cmd.Member.Name} from uninitialized value");
                                else
                                    throw new BabyPenguinRuntimeException($"Cannot read member {cmd.Member.Name} from type {ownerVar.TypeInfo}");

                                IRuntimeValue memberVar;
                                if (!fields.ContainsKey(cmd.Member.Name))
                                    throw new BabyPenguinRuntimeException($"Type {owner.TypeInfo} does not have member {cmd.Member.Name}");
                                memberVar = fields[cmd.Member.Name]!;
                                resultVar.AssignFrom(memberVar);

                                if (cmd.IsFatPointer)
                                {
                                    if (resultVar.Value is FunctionRuntimeValue functionRuntimeValue)
                                        functionRuntimeValue.Owner = ownerVar;
                                    else
                                        throw new NotImplementedException();
                                }
                                return memberVar;
                            }


                            if (owner.Type == TypeEnum.Interface)
                            {
                                if (owner.As<InterfaceRuntimeSymbol>().VTable!.Slots.Find(slot => slot.InterfaceSymbol.Name == cmd.Member.Name) is VTableSlot vtableSlot)
                                {
                                    var funVar = IRuntimeSymbol.FromSymbol(owner.Model, vtableSlot.ImplementationSymbol);
                                    if (cmd.IsFatPointer)
                                    {
                                        (resultVar as FunctionRuntimeSymbol)!.FunctionValue = (funVar.Value as FunctionRuntimeValue)!;
                                        (resultVar as FunctionRuntimeSymbol)!.FunctionValue!.Owner = owner.Value;
                                    }
                                    else
                                    {
                                        resultVar.AssignFrom(funVar);
                                    }
                                    DebugPrint(cmd, op1: funVar.ToDebugString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                                }
                                else
                                {
                                    IRuntimeValue memberVar = readMember(owner.As<InterfaceRuntimeSymbol>().Value);
                                    DebugPrint(cmd, op1: memberVar.ToString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                                }
                            }
                            else
                            {
                                IRuntimeValue ownerVar;
                                if (owner is ClassRuntimeSymbol clsVar)
                                    ownerVar = clsVar.ReferenceValue;
                                else if (owner is EnumRuntimeSymbol enumVar)
                                    ownerVar = enumVar.EnumValue;
                                else throw new BabyPenguinRuntimeException($"Cannot read member {cmd.Member.Name} from type {owner.TypeInfo}");

                                IRuntimeValue memberVar = readMember(ownerVar);
                                DebugPrint(cmd, op1: memberVar.ToString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                            }
                        }
                        break;
                    case WriteMemberInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);

                            Dictionary<string, IRuntimeValue> members;
                            if (owner is ClassRuntimeSymbol clsVar)
                                members = clsVar.ReferenceValue.Fields;
                            else if (owner is EnumRuntimeSymbol enumVar)
                                members = enumVar.EnumValue.FieldsValue.Fields;
                            else if (owner is InterfaceRuntimeSymbol intfVar)
                            {
                                if (intfVar.Value is ReferenceRuntimeValue refVal)
                                    members = refVal.Fields;
                                else if (intfVar.Value is EnumRuntimeValue enumVal)
                                    members = enumVal.FieldsValue.Fields;
                                else if (intfVar.Value is NotInitializedRuntimeValue)
                                    throw new BabyPenguinRuntimeException($"Cannot write member {cmd.Member.Name} to {owner.ToDebugString()} which is uninitialized");
                                else
                                    throw new BabyPenguinRuntimeException($"Cannot write member {cmd.Member.Name} to type {owner.TypeInfo}");
                            }
                            else
                                throw new BabyPenguinRuntimeException($"Cannot write member {cmd.Member.Name} to type {owner.TypeInfo}");

                            if (!members.ContainsKey(cmd.Member.Name))
                                throw new BabyPenguinRuntimeException($"Class {owner.TypeInfo} does not have member {cmd.Member.Name}");

                            if (rightVar.TypeInfo.FullName != members[cmd.Member.Name].TypeInfo.FullName)
                                throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {members[cmd.Member.Name].TypeInfo}");

                            members[cmd.Member.Name] = rightVar.Value;
                            DebugPrint(cmd, op1: cmd.Member.Name, op2: rightVar.ToDebugString(), result: owner.ToDebugString());
                        }
                        break;
                    case WriteEnumInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var enumVar = resolveVariable(cmd.TargetEnum);
                            enumVar.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = rightVar.Value;
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: enumVar.ToDebugString());
                            break;
                        }
                    case ReadEnumInstruction cmd:
                        {
                            var resultVar = resolveVariable(cmd.TargetValue);
                            var enumVar = resolveVariable(cmd.Enum);
                            if (enumVar.As<EnumRuntimeSymbol>().EnumValue.ContainingValue == null)
                                throw new BabyPenguinRuntimeException($"Enum {enumVar.TypeInfo} has no value");
                            resultVar.AssignFrom(enumVar.As<EnumRuntimeSymbol>().EnumValue.ContainingValue!);
                            DebugPrint(cmd, op1: enumVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case SignalInstruction cmd:
                        {
                            int value;
                            if (cmd.Code != null)
                            {
                                value = cmd.Code.Value;
                            }
                            else
                            {
                                var codeVar = resolveVariable(cmd.CodeSymbol!);
                                value = Convert.ToInt32(codeVar.As<BasicRuntimeSymbol>().ValueToString!);
                            }
                            DebugPrint(cmd, op1: value.ToString());
                            switch (value)
                            {
                                case (int)SignalCode.Breakpoint:
                                    yield return new RuntimeBreak(RuntimeBreakReason.Breakpoint, this);
                                    break;
                                default:
                                    throw new BabyPenguinRuntimeException("unknown signal: " + value);
                            }
                            break;
                        }
                    case NopInstuction cmd:
                        DebugPrint(cmd);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                InstructionPointer += 1;

                if (result != null)
                {
                    yield return result;
                    break;
                }
            }

            if (result == null && InstructionPointer >= CodeContainer.Instructions.Count)
                throw new BabyPenguinRuntimeException($"Function/Routine '{CodeContainer.FullName}' does not return a value");

        }
    }
}