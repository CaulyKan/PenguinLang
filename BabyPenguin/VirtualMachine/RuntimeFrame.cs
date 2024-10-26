namespace BabyPenguin.VirtualMachine
{

    public class RuntimeFrame
    {
        public RuntimeFrame(ICodeContainer container, RuntimeGlobal runtimeGlobal, List<IRuntimeVar> parameters)
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
                    LocalVariables.Add(local.FullName, IRuntimeVar.FromSymbol(container.Model, local));
                }
            }
        }

        public Dictionary<string, IRuntimeVar> LocalVariables { get; } = [];
        public RuntimeGlobal Global { get; }
        public ICodeContainer CodeContainer { get; }

        private void DebugPrint(BabyPenguinIR inst, string? op1 = "", string? op2 = "", string? result = "")
        {
            if (Global.EnableDebugPrint)
            {
                Global.DebugWriter.WriteLine(inst.ToDebugString(op1, op2, result));
            }
        }

        public IRuntimeVar? Run()
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
                                    resultVar.As<BasicRuntimeVar>().Value = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeVar>().Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeVar>().Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeVar>().Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeVar>().Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeVar>().Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeVar>().Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeVar>().Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeVar>().Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeVar>().Value = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeVar>().Value = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    resultVar.As<BasicRuntimeVar>().Value = 0;
                                    break;
                                case TypeEnum.String:
                                    resultVar.As<BasicRuntimeVar>().Value = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeVar>().Value = cmd.LiteralValue[0];
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
                            DebugPrint(cmd, op1: funSymbol.ToString(), op2: string.Join(", ", args.Select(arg => arg.ToDebugString())), result: retVar?.ToString());
                            if (!funSymbol.SemanticFunction.IsExtern)
                            {
                                var newFrame = new RuntimeFrame(funSymbol.SemanticFunction, Global, args);
                                var resTemp = newFrame.Run();
                                if (resTemp != null)
                                    retVar?.AssignFrom(resTemp);
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(funSymbol.FullName, out Action<IRuntimeVar?, List<IRuntimeVar>>? action))
                                {
                                    action(retVar, args);
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
                                    resultVar.As<BasicRuntimeVar>().Value = 0;
                                    break;
                                case TypeEnum.U8:
                                    resultVar.As<BasicRuntimeVar>().Value = (byte)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.U16:
                                    resultVar.As<BasicRuntimeVar>().Value = (ushort)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.U32:
                                    resultVar.As<BasicRuntimeVar>().Value = (uint)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.U64:
                                    resultVar.As<BasicRuntimeVar>().Value = (ulong)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.I8:
                                    resultVar.As<BasicRuntimeVar>().Value = (sbyte)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.I16:
                                    resultVar.As<BasicRuntimeVar>().Value = (short)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.I32:
                                    resultVar.As<BasicRuntimeVar>().Value = (int)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.I64:
                                    resultVar.As<BasicRuntimeVar>().Value = (long)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.Float:
                                    resultVar.As<BasicRuntimeVar>().Value = (float)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.Double:
                                    resultVar.As<BasicRuntimeVar>().Value = (double)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.String:
                                    if (rightVar.TypeInfo.IsBoolType)
                                    {
                                        resultVar.As<BasicRuntimeVar>().Value = (bool)rightVar.As<BasicRuntimeVar>().Value! ? "true" : "false";
                                    }
                                    else
                                    {
                                        resultVar.As<BasicRuntimeVar>().Value = rightVar.As<BasicRuntimeVar>().Value.ToString() ?? "";
                                    }
                                    break;
                                case TypeEnum.Char:
                                    resultVar.As<BasicRuntimeVar>().Value = (char)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case TypeEnum.Bool:
                                    resultVar.As<BasicRuntimeVar>().Value = (bool)rightVar.As<BasicRuntimeVar>().Value!;
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
                                    throw new BabyPenguinRuntimeException("CAST command is not supported for interface type");
                            }
                            DebugPrint(cmd, op1: rightVar.ToDebugString(), result: resultVar.ToDebugString());
                            break;
                        }
                    case CastInterfaceInstruction cmd:
                        {
                            IRuntimeVar resultVar = resolveVariable(cmd.Target);
                            IRuntimeVar rightVar = resolveVariable(cmd.Operand);
                            resultVar.As<InterfaceRuntimeVar>().VTable = cmd.VTable;
                            resultVar.As<InterfaceRuntimeVar>().Object = rightVar.As<ClassRuntimeVar>();
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
                                    resultVar.As<BasicRuntimeVar>().Value = rightVar.As<BasicRuntimeVar>().Value;
                                    break;
                                case UnaryOperatorEnum.Minus:
                                    resultVar.As<BasicRuntimeVar>().Value = -(dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    resultVar.As<BasicRuntimeVar>().Value = ~(dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    resultVar.As<BasicRuntimeVar>().Value = !(dynamic)rightVar.As<BasicRuntimeVar>().Value!;
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
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! + (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! - (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! * (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! / (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! % (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! & (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! | (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! ^ (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! && (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! || (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! == (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! != (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! > (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! >= (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! < (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! <= (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! << (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (IType.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.As<BasicRuntimeVar>().Value = (dynamic)leftVar.As<BasicRuntimeVar>().Value! >> (dynamic)rightVar.As<BasicRuntimeVar>().Value!;
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
                                if (cmd.JumpOnCondition != (bool)condVar.As<BasicRuntimeVar>().Value!)
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
            if (CodeContainer.ReturnTypeInfo.Type == TypeEnum.Void)
            {
                return null;
            }
            else
            {
                throw new BabyPenguinRuntimeException("Function does not return a value");
            }
        }
    }
}