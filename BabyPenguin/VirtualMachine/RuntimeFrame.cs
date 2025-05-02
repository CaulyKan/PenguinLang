namespace BabyPenguin.VirtualMachine
{
    public record RuntimeFrameResult(IRuntimeVar? ReturnValue, ReturnStatus ReturnStatus);

    public class RuntimeFrame
    {
        public RuntimeFrame(ICodeContainer container, RuntimeGlobal runtimeGlobal, List<IRuntimeVar> parameters, int frameLevel)
        {
            CodeContainer = container;
            Global = runtimeGlobal;
            FrameLevel = frameLevel;
            foreach (var local in container.Symbols)
            {
                if (local.IsParameter)
                {
                    LocalVariables.Add(local.FullName, parameters[local.ParameterIndex]);
                }
                else
                {
                    LocalVariables.Add(local.FullName, IRuntimeVar.FromSymbol(container.Model, local));
                }
            }
        }

        public Dictionary<string, IRuntimeVar> LocalVariables { get; } = [];
        public RuntimeGlobal Global { get; }
        public ICodeContainer CodeContainer { get; }
        public int FrameLevel { get; set; } = 0;

        private void DebugPrint(BabyPenguinIR inst, string? op1 = "", string? op2 = "", string? result = "")
        {
            if (Global.EnableDebugPrint)
            {
                Global.DebugWriter.WriteLine(new string('|', FrameLevel) + inst.ToDebugString(op1, op2, result));
            }
        }

        public RuntimeFrameResult Run()
        {
            IRuntimeVar resolveVariable(ISymbol symbol)
            {
                IRuntimeVar? result;
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

            int findLabel(string label)
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

            for (int i = 0; i < CodeContainer.Instructions.Count; i++)
            {
                var command = CodeContainer.Instructions[i];
                switch (command)
                {
                    case AssignmentInstruction cmd:
                        {
                            IRuntimeVar rightVar = resolveVariable(cmd.RightHandSymbol);
                            IRuntimeVar resultVar = resolveVariable(cmd.LeftHandSymbol);
                            resultVar.AssignFrom(rightVar);
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case AssignLiteralToSymbolInstruction cmd:
                        {
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            switch (resultVar.Type)
                            {
                                case TypeEnum.Bool:
                                    resultVar.As<BasicRuntimeVar>().BoolValue = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeVar>().U8Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeVar>().U16Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeVar>().U32Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeVar>().U64Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeVar>().I8Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeVar>().I16Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeVar>().I32Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeVar>().I64Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeVar>().FloatValue = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeVar>().DoubleValue = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    break;
                                case TypeEnum.String:
                                    resultVar.As<BasicRuntimeVar>().StringValue = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeVar>().CharValue = cmd.LiteralValue[0];
                                    break;
                                default:
                                    throw new BabyPenguinRuntimeException("A complex type cannot be a literal value");
                            }
                            DebugPrint(cmd, result: resultVar.ToDebugString());
                            break;
                        }
                    case FunctionCallInstruction cmd:
                        {
                            IRuntimeVar funVar = resolveVariable(cmd.FunctionSymbol);
                            FunctionSymbol funSymbol = funVar.As<FunctionRuntimeVar>().FunctionSymbol as FunctionSymbol ??
                                throw new BabyPenguinRuntimeException("The symbol is not a function: " + cmd.FunctionSymbol.FullName);
                            IRuntimeVar? retVar = cmd.Target == null ? null : resolveVariable(cmd.Target);
                            List<IRuntimeVar> args = cmd.Arguments.Select(resolveVariable).ToList();
                            DebugPrint(cmd, op1: funSymbol.FullName, op2: string.Join(", ", args.Select(arg => arg.ToDebugString())), result: retVar?.Symbol.FullName);
                            if (!funSymbol.IsExtern)
                            {
                                var newFrame = new RuntimeFrame(funSymbol.CodeContainer, Global, args, this.FrameLevel + 1);
                                var resTemp = newFrame.Run();
                                if (resTemp.ReturnValue != null)
                                    retVar?.AssignFrom(resTemp.ReturnValue);
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(funSymbol.FullName, out Action<RuntimeFrame, IRuntimeVar?, List<IRuntimeVar>>? action))
                                {
                                    action(this, retVar, args);
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
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            IRuntimeVar rightVar = resolveVariable(cmd.Operand);
                            if (resultVar.TypeInfo != cmd.TypeInfo)
                            {
                                throw new BabyPenguinRuntimeException($"Cannot assign type {cmd.TypeInfo} to type {resultVar.TypeInfo}");
                            }
                            switch (cmd.TypeInfo.Type)
                            {
                                case TypeEnum.Void:
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeVar>().U8Value = Convert.ToByte(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeVar>().U16Value = Convert.ToUInt16(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeVar>().U32Value = Convert.ToUInt32(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeVar>().U64Value = Convert.ToUInt64(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeVar>().I8Value = Convert.ToSByte(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeVar>().I16Value = Convert.ToInt16(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeVar>().I32Value = Convert.ToInt32(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeVar>().I64Value = Convert.ToInt64(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeVar>().FloatValue = Convert.ToSingle(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeVar>().DoubleValue = Convert.ToDouble(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeVar>().CharValue = rightVar.As<BasicRuntimeVar>().CharValue;
                                    break;
                                case TypeEnum.Bool:
                                    resultVar.As<BasicRuntimeVar>().BoolValue = bool.Parse(rightVar.As<BasicRuntimeVar>().ValueToString!);
                                    break;
                                case TypeEnum.String:
                                    if (rightVar.TypeInfo.IsBoolType)
                                    {
                                        resultVar.As<BasicRuntimeVar>().StringValue = rightVar.As<BasicRuntimeVar>().BoolValue ? "true" : "false";
                                    }
                                    else if (rightVar.TypeInfo.IsStringType)
                                    {
                                        resultVar.As<BasicRuntimeVar>().StringValue = rightVar.As<BasicRuntimeVar>().StringValue ?? "";
                                    }
                                    else if (rightVar.TypeInfo.IsEnumType)
                                    {
                                        var enumInt = rightVar.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value;
                                        resultVar.As<BasicRuntimeVar>().StringValue = (rightVar.As<EnumRuntimeVar>().TypeInfo as IEnum)?.EnumDeclarations.Find(i => i.Value == enumInt)?.Name ??
                                        throw new BabyPenguinRuntimeException($"Converting unknown enum value '{enumInt}' for '{rightVar.As<EnumRuntimeVar>().TypeInfo.FullName}' to string.");
                                    }
                                    else
                                    {
                                        resultVar.As<BasicRuntimeVar>().StringValue = rightVar.As<BasicRuntimeVar>().ValueToString ?? "";
                                    }
                                    break;
                                case TypeEnum.Fun:
                                    throw new NotImplementedException();
                                case TypeEnum.Class:
                                    if (rightVar is ClassRuntimeVar)
                                        resultVar.As<ClassRuntimeVar>().AssignFrom(rightVar);
                                    else if (rightVar is InterfaceRuntimeVar)
                                        resultVar.As<ClassRuntimeVar>().AssignFrom(rightVar.As<InterfaceRuntimeVar>().Object!);
                                    break;
                                case TypeEnum.Interface:
                                    if (rightVar is ClassRuntimeVar || rightVar is BasicRuntimeVar)
                                    {
                                        var cls = (cmd.Operand.TypeInfo as IVTableContainer) ?? throw new InvalidOperationException("Operand is not a class");
                                        var intf = (cmd.TypeInfo as IInterface) ?? throw new InvalidOperationException("TypeInfo is not an interface");
                                        var vtable = cls.VTables.FirstOrDefault(v => v.Interface.FullName == intf.FullName) ?? throw new BabyPenguinRuntimeException($"Class {cls.FullName} does not implement interface {intf.FullName}");
                                        resultVar.As<InterfaceRuntimeVar>().VTable = vtable;
                                        resultVar.As<InterfaceRuntimeVar>().Object = rightVar;
                                    }
                                    else if (rightVar is InterfaceRuntimeVar)
                                    {
                                        resultVar.As<InterfaceRuntimeVar>().AssignFrom(rightVar);
                                    }
                                    break;
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case UnaryOperationInstruction cmd:
                        {
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            IRuntimeVar rightVar = resolveVariable(cmd.Operand);
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
                                            resultVar.As<BasicRuntimeVar>().I8Value = (sbyte)(-rightVar.As<BasicRuntimeVar>().U8Value);
                                            break;
                                        case TypeEnum.U16:
                                            resultVar.As<BasicRuntimeVar>().I16Value = (short)(-rightVar.As<BasicRuntimeVar>().U16Value);
                                            break;
                                        case TypeEnum.U32:
                                            resultVar.As<BasicRuntimeVar>().I32Value = (int)(-rightVar.As<BasicRuntimeVar>().U32Value);
                                            break;
                                        // case TypeEnum.U64:
                                        // resultVar.As<BasicRuntimeVar>().U64Value = (ulong)(-rightVar.As<BasicRuntimeVar>().U64Value);
                                        // break;
                                        case TypeEnum.I8:
                                            resultVar.As<BasicRuntimeVar>().I8Value = (sbyte)(-rightVar.As<BasicRuntimeVar>().I8Value);
                                            break;
                                        case TypeEnum.I16:
                                            resultVar.As<BasicRuntimeVar>().I16Value = (short)(-rightVar.As<BasicRuntimeVar>().I16Value);
                                            break;
                                        case TypeEnum.I32:
                                            resultVar.As<BasicRuntimeVar>().I32Value = -rightVar.As<BasicRuntimeVar>().I32Value;
                                            break;
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeVar>().I64Value = -rightVar.As<BasicRuntimeVar>().I64Value;
                                            break;
                                        case TypeEnum.Float:
                                            resultVar.As<BasicRuntimeVar>().FloatValue = -rightVar.As<BasicRuntimeVar>().FloatValue;
                                            break;
                                        case TypeEnum.Double:
                                            resultVar.As<BasicRuntimeVar>().DoubleValue = -rightVar.As<BasicRuntimeVar>().DoubleValue;
                                            break;
                                        default:
                                            throw new BabyPenguinRuntimeException($"Cannot negate type {rightVar.Type}");
                                    }
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    switch (rightVar.Type)
                                    {
                                        case TypeEnum.U8:
                                            resultVar.As<BasicRuntimeVar>().U8Value = (byte)~rightVar.As<BasicRuntimeVar>().U8Value;
                                            break;
                                        case TypeEnum.U16:
                                            resultVar.As<BasicRuntimeVar>().U16Value = (ushort)~rightVar.As<BasicRuntimeVar>().U16Value;
                                            break;
                                        case TypeEnum.U32:
                                            resultVar.As<BasicRuntimeVar>().U32Value = ~rightVar.As<BasicRuntimeVar>().U32Value;
                                            break;
                                        case TypeEnum.U64:
                                            resultVar.As<BasicRuntimeVar>().U64Value = ~rightVar.As<BasicRuntimeVar>().U64Value;
                                            break;
                                        case TypeEnum.I8:
                                            resultVar.As<BasicRuntimeVar>().I8Value = (sbyte)~rightVar.As<BasicRuntimeVar>().I8Value;
                                            break;
                                        case TypeEnum.I16:
                                            resultVar.As<BasicRuntimeVar>().I16Value = (short)~rightVar.As<BasicRuntimeVar>().I16Value;
                                            break;
                                        case TypeEnum.I32:
                                            resultVar.As<BasicRuntimeVar>().I32Value = ~rightVar.As<BasicRuntimeVar>().I32Value;
                                            break;
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeVar>().I64Value = ~rightVar.As<BasicRuntimeVar>().I64Value;
                                            break;
                                        case TypeEnum.Bool:
                                            resultVar.As<BasicRuntimeVar>().BoolValue = !rightVar.As<BasicRuntimeVar>().BoolValue;
                                            break;
                                        default:
                                            throw new BabyPenguinRuntimeException($"Cannot bitwise not type {rightVar.Type}");
                                    }
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    switch (rightVar.Type)
                                    {
                                        case TypeEnum.Bool:
                                            resultVar.As<BasicRuntimeVar>().BoolValue = !rightVar.As<BasicRuntimeVar>().BoolValue;
                                            break;
                                        case TypeEnum.U8:
                                        case TypeEnum.U16:
                                        case TypeEnum.U32:
                                        case TypeEnum.U64:
                                        case TypeEnum.I8:
                                        case TypeEnum.I16:
                                        case TypeEnum.I32:
                                        case TypeEnum.I64:
                                            resultVar.As<BasicRuntimeVar>().BoolValue = rightVar.As<BasicRuntimeVar>().ValueToString != "0";
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
                            IRuntimeVar leftVar = resolveVariable(cmd.LeftSymbol);
                            IRuntimeVar rightVar = resolveVariable(cmd.RightSymbol);
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            if (!leftVar.TypeInfo.CanImplicitlyCastTo(rightVar.TypeInfo)
                                && !rightVar.TypeInfo.CanImplicitlyCastTo(leftVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Type {rightVar.TypeInfo} is not equal to type {leftVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case BinaryOperatorEnum.Add:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! + rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! - rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! * rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! / rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! % rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! & rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! | rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! ^ rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! && rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! || rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! == rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! != rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! > rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! >= rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! < rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! <= rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! << rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().DynamicValue = leftVar.As<BasicRuntimeVar>().DynamicValue! >> rightVar.As<BasicRuntimeVar>().DynamicValue!;
                                    break;
                            }
                            DebugPrint(cmd, op1: leftVar.ToDebugString(), op2: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case GotoInstruction cmd:
                        {
                            if (cmd.Condition != null)
                            {
                                IRuntimeVar condVar = resolveVariable(cmd.Condition);
                                if (!condVar.TypeInfo.IsBoolType)
                                    throw new BabyPenguinRuntimeException($"Cannot use type {condVar.TypeInfo} as a condition");
                                if (cmd.JumpOnCondition != condVar.As<BasicRuntimeVar>().BoolValue!)
                                {
                                    DebugPrint(cmd, op1: condVar.ToDebugString(), result: "NOT JUMP");
                                    break;
                                }
                                else
                                {
                                    i = findLabel(cmd.TargetLabel);
                                    DebugPrint(cmd, op1: condVar.ToDebugString(), result: $"JUMP {cmd.TargetLabel}");
                                }
                            }
                            else
                            {
                                i = findLabel(cmd.TargetLabel);
                                DebugPrint(cmd, result: $"JUMP {cmd.TargetLabel}");
                            }
                            break;
                        }
                    case ReturnInstruction cmd:
                        {
                            if (cmd.RetValue != null)
                            {
                                IRuntimeVar retVar = resolveVariable(cmd.RetValue);
                                DebugPrint(cmd, op1: retVar.ToDebugString());
                                return new RuntimeFrameResult(retVar, cmd.ReturnStatus);
                            }
                            else
                            {
                                return new RuntimeFrameResult(null, cmd.ReturnStatus);
                            }
                        }
                    case NewInstanceInstruction cmd:
                        // do nothing
                        DebugPrint(cmd, result: cmd.Target.FullName);
                        break;
                    case ReadMemberInstruction cmd:
                        {
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);
                            if (owner.Type == TypeEnum.Interface)
                            {
                                if (owner.As<InterfaceRuntimeVar>().VTable!.Slots.Find(slot => slot.InterfaceSymbol.Name == cmd.Member.Name) is VTableSlot vtableSlot)
                                {
                                    var funVar = IRuntimeVar.FromSymbol(owner.Model, vtableSlot.ImplementationSymbol);
                                    resultVar.AssignFrom(funVar);
                                    DebugPrint(cmd, op1: funVar.ToDebugString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                                }
                                else throw new BabyPenguinRuntimeException($"Interface {owner.TypeInfo} does not have member {cmd.Member.Name}");
                            }
                            else
                            {
                                IRuntimeVar memberVar;
                                if (owner is ClassRuntimeVar clsVar)
                                {
                                    var members = clsVar.ObjectFields;
                                    if (!members.ContainsKey(cmd.Member.Name))
                                        throw new BabyPenguinRuntimeException($"Class {owner.TypeInfo} does not have member {cmd.Member.Name}");
                                    memberVar = members[cmd.Member.Name]!;
                                }
                                else if (owner is EnumRuntimeVar enumVar)
                                {
                                    var members = enumVar.ObjectFields;
                                    if (!members.ContainsKey(cmd.Member.Name))
                                        throw new BabyPenguinRuntimeException($"Class {owner.TypeInfo} does not have member {cmd.Member.Name}");
                                    memberVar = members[cmd.Member.Name]!;
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException($"Cannot read member {cmd.Member.Name} from type {owner.TypeInfo}");
                                }
                                resultVar.AssignFrom(memberVar);
                                DebugPrint(cmd, op1: memberVar.ToDebugString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                            }
                        }
                        break;
                    case WriteMemberInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);

                            Dictionary<string, IRuntimeVar> members;
                            if (owner is ClassRuntimeVar clsVar)
                                members = clsVar.ObjectFields;
                            else if (owner is EnumRuntimeVar enumVar)
                                members = enumVar.ObjectFields;
                            else
                                throw new BabyPenguinRuntimeException($"Cannot write member {cmd.Member.Name} to type {owner.TypeInfo}");

                            if (!members.ContainsKey(cmd.Member.Name))
                                throw new BabyPenguinRuntimeException($"Class {owner.TypeInfo} does not have member {cmd.Member.Name}");
                            members[cmd.Member.Name]!.AssignFrom(rightVar);
                            DebugPrint(cmd, op1: members[cmd.Member.Name].ToDebugString(), op2: rightVar.ToDebugString(), result: owner.ToDebugString());
                        }
                        break;
                    case WriteEnumInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var enumVar = resolveVariable(cmd.TargetEnum);
                            enumVar.As<EnumRuntimeVar>().EnumObject = rightVar;
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: enumVar.ToDebugString());
                            break;
                        }
                    case ReadEnumInstruction cmd:
                        {
                            var resultVar = resolveVariable(cmd.TargetValue);
                            var enumVar = resolveVariable(cmd.Enum);
                            if (enumVar.As<EnumRuntimeVar>().EnumObject == null)
                                throw new BabyPenguinRuntimeException($"Enum {enumVar.TypeInfo} has no value");
                            resultVar.AssignFrom(enumVar.As<EnumRuntimeVar>().EnumObject!);
                            DebugPrint(cmd, op1: enumVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case NopInstuction cmd:
                        DebugPrint(cmd);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }


            throw new BabyPenguinRuntimeException($"Function {CodeContainer.FullName} does not return a value");
        }
    }
}