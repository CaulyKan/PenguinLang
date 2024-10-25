namespace BabyPenguin.VirtualMachine
{

    public class RuntimeFrame
    {
        public RuntimeFrame(ICodeContainer container, RuntimeGlobal runtimeGlobal, List<RuntimeVar> parameters)
        {
            CodeContainer = container;
            Global = runtimeGlobal;
            foreach (var local in container.Symbols)
            {
                if (local.IsParameter)
                {
                    LocalVariables.Add(local.FullName, parameters[local.ParameterIndex]);
                }
                else
                {
                    LocalVariables.Add(local.FullName, new RuntimeVar(container.Model, local.TypeInfo, local));
                }
            }
        }

        public Dictionary<string, RuntimeVar> LocalVariables { get; } = [];
        public RuntimeGlobal Global { get; }
        public ICodeContainer CodeContainer { get; }

        private void DebugPrint(BabyPenguinIR inst, string? op1 = "", string? op2 = "", string? result = "")
        {
            if (Global.EnableDebugPrint)
            {
                Global.DebugWriter.WriteLine(inst.ToDebugString(op1, op2, result));
            }
        }

        public RuntimeVar Run()
        {
            RuntimeVar resolveVariable(ISymbol symbol)
            {
                RuntimeVar? result;
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
                            RuntimeVar rightVar = resolveVariable(cmd.RightHandSymbol);
                            RuntimeVar resultVar = resolveVariable(cmd.LeftHandSymbol);
                            resultVar.AssignFrom(rightVar);
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case AssignLiteralToSymbolInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            switch (resultVar.Type)
                            {
                                case TypeEnum.Bool:
                                    resultVar.Value = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    resultVar.Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.Value = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.Value = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    resultVar.Value = null;
                                    break;
                                case TypeEnum.String:
                                    resultVar.Value = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    resultVar.Value = cmd.LiteralValue[0];
                                    break;
                                case TypeEnum.Fun:
                                    throw new BabyPenguinRuntimeException("A function cannot be a literal value");
                                case TypeEnum.Class:
                                    throw new BabyPenguinRuntimeException("A complex typecannot be a literal value");
                            }
                            DebugPrint(cmd, result: resultVar.ToDebugString());
                            break;
                        }
                    case FunctionCallInstruction cmd:
                        {
                            FunctionSymbol funVar = resolveVariable(cmd.FunctionSymbol).FunctionSymbol ??
                                throw new BabyPenguinRuntimeException("The symbol is not a function: " + cmd.FunctionSymbol.FullName);
                            RuntimeVar? retVar = cmd.Target == null ? null : resolveVariable(cmd.Target);
                            List<RuntimeVar> args = cmd.Arguments.Select(arg => arg.IsReadonly ? resolveVariable(arg).Clone() : resolveVariable(arg)).ToList();
                            DebugPrint(cmd, op1: funVar.ToString(), op2: string.Join(", ", args.Select(arg => arg.ToDebugString())), result: retVar?.ToString());
                            if (!funVar.SemanticFunction.IsExtern)
                            {
                                var newFrame = new RuntimeFrame(funVar.SemanticFunction, Global, args);
                                var resTemp = newFrame.Run();
                                retVar?.AssignFrom(resTemp);
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(funVar.FullName, out Action<RuntimeVar?, List<RuntimeVar>>? action))
                                {
                                    action(retVar, args);
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException("Cannot find external function " + funVar.FullName);
                                }
                            }
                            break;
                        }
                    // case SlicingInstruction cmd:
                    //     throw new NotImplementedException();
                    case CastInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            RuntimeVar rightVar = resolveVariable(cmd.Operand);
                            if (resultVar.TypeInfo != cmd.TypeInfo)
                            {
                                throw new BabyPenguinRuntimeException($"Cannot assign type {cmd.TypeInfo} to type {resultVar.TypeInfo}");
                            }
                            switch (cmd.TypeInfo.Type)
                            {
                                case TypeEnum.Void:
                                    resultVar = RuntimeVar.Void();
                                    break;
                                case TypeEnum.U8:
                                    resultVar.Value = (byte)rightVar.Value!;
                                    break;
                                case TypeEnum.U16:
                                    resultVar.Value = (ushort)rightVar.Value!;
                                    break;
                                case TypeEnum.U32:
                                    resultVar.Value = (uint)rightVar.Value!;
                                    break;
                                case TypeEnum.U64:
                                    resultVar.Value = (ulong)rightVar.Value!;
                                    break;
                                case TypeEnum.I8:
                                    resultVar.Value = (sbyte)rightVar.Value!;
                                    break;
                                case TypeEnum.I16:
                                    resultVar.Value = (short)rightVar.Value!;
                                    break;
                                case TypeEnum.I32:
                                    resultVar.Value = (int)rightVar.Value!;
                                    break;
                                case TypeEnum.I64:
                                    resultVar.Value = (long)rightVar.Value!;
                                    break;
                                case TypeEnum.Float:
                                    resultVar.Value = (float)rightVar.Value!;
                                    break;
                                case TypeEnum.Double:
                                    resultVar.Value = (double)rightVar.Value!;
                                    break;
                                case TypeEnum.String:
                                    if (rightVar.TypeInfo.IsBoolType)
                                    {
                                        resultVar.Value = (bool)rightVar.Value! ? "true" : "false";
                                    }
                                    else
                                    {
                                        resultVar.Value = rightVar.Value!.ToString();
                                    }
                                    break;
                                case TypeEnum.Char:
                                    resultVar.Value = (char)rightVar.Value!;
                                    break;
                                case TypeEnum.Bool:
                                    resultVar.Value = (bool)rightVar.Value!;
                                    break;
                                case TypeEnum.Fun:
                                case TypeEnum.Class:
                                    throw new NotImplementedException();
                                case TypeEnum.Interface:
                                    throw new BabyPenguinRuntimeException("CAST command is not supported for interface type");
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case CastInterfaceInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            RuntimeVar rightVar = resolveVariable(cmd.Operand);
                            resultVar.VTable = cmd.VTable;
                            break;
                        }
                    case UnaryOperationInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            RuntimeVar rightVar = resolveVariable(cmd.Operand);
                            if (!rightVar.TypeInfo.CanImplicitlyCastTo(resultVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case UnaryOperatorEnum.Ref:
                                case UnaryOperatorEnum.Deref:
                                    throw new NotImplementedException();
                                case UnaryOperatorEnum.Plus:
                                    resultVar.Value = rightVar.Value;
                                    break;
                                case UnaryOperatorEnum.Minus:
                                    resultVar.Value = -(dynamic)rightVar.Value!;
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    resultVar.Value = ~(dynamic)rightVar.Value!;
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    resultVar.Value = !(dynamic)rightVar.Value!;
                                    break;
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case BinaryOperationInstruction cmd:
                        {
                            RuntimeVar leftVar = resolveVariable(cmd.LeftSymbol);
                            RuntimeVar rightVar = resolveVariable(cmd.RightSymbol);
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            if (!leftVar.TypeInfo.CanImplicitlyCastTo(rightVar.TypeInfo)
                                && !rightVar.TypeInfo.CanImplicitlyCastTo(leftVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Type {rightVar.TypeInfo} is not equal to type {leftVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case BinaryOperatorEnum.Add:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! + (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! - (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! * (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! / (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! % (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! & (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! | (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! ^ (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! && (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! || (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! == (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! != (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! > (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! >= (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! < (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! <= (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! << (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! >> (dynamic)rightVar.Value!;
                                    break;
                            }
                            DebugPrint(cmd, op1: leftVar.ToDebugString(), op2: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case GotoInstruction cmd:
                        {
                            if (cmd.Condition != null)
                            {
                                RuntimeVar condVar = resolveVariable(cmd.Condition);
                                if (!condVar.TypeInfo.IsBoolType)
                                    throw new BabyPenguinRuntimeException($"Cannot use type {condVar.TypeInfo} as a condition");
                                if (cmd.JumpOnCondition != (bool)condVar.Value!)
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
                                RuntimeVar retVar = resolveVariable(cmd.RetValue);
                                DebugPrint(cmd, op1: retVar.ToDebugString());
                                return retVar;
                            }
                            break;
                        }
                    case NewInstanceInstruction cmd:
                        // do nothing
                        DebugPrint(cmd);
                        break;
                    case ReadMemberInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);
                            if (owner.Type == TypeEnum.Interface &&
                                owner.VTable!.Slots.Find(slot => slot.InterfaceSymbol.Name == cmd.Member.Name) is VTableSlot vtableSlot)
                            {
                                var funVar = new RuntimeVar(owner.Model, vtableSlot.ImplementationSymbol.TypeInfo, vtableSlot.ImplementationSymbol);
                                resultVar.AssignFrom(funVar);
                                DebugPrint(cmd, op1: funVar.ToDebugString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                            }
                            else
                            {
                                var members = (owner.Value as Dictionary<string, RuntimeVar>)!;
                                var memberVar = members[cmd.Member.Name]!;
                                resultVar.AssignFrom(memberVar);
                                DebugPrint(cmd, op1: memberVar.ToDebugString(), op2: owner.ToDebugString(), result: resultVar.ToDebugString());
                            }
                        }
                        break;
                    case WriteMemberInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);
                            var members = (owner.Value as Dictionary<string, RuntimeVar>)!;
                            members[cmd.Member.Name]!.AssignFrom(rightVar);
                            DebugPrint(cmd, op1: members[cmd.Member.Name].ToDebugString(), op2: rightVar.ToDebugString(), result: owner.ToDebugString());
                        }
                        break;
                    case WriteEnumInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var enumVar = resolveVariable(cmd.TargetEnum);
                            enumVar.EnumValue = rightVar;
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: enumVar.ToDebugString());
                            break;
                        }
                    case ReadEnumInstruction cmd:
                        {
                            var resultVar = resolveVariable(cmd.TargetValue);
                            var enumVar = resolveVariable(cmd.Enum);
                            if (enumVar.EnumValue == null)
                                throw new BabyPenguinRuntimeException($"Enum {enumVar.TypeInfo} has no value");
                            resultVar.AssignFrom(enumVar.EnumValue);
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
            if (CodeContainer.ReturnTypeInfo.Type == TypeEnum.Void)
            {
                return RuntimeVar.Void();
            }
            else
            {
                throw new BabyPenguinRuntimeException("Function does not return a value");
            }
        }
    }
}