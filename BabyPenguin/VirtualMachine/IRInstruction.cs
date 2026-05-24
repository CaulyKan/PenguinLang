namespace BabyPenguin.VirtualMachine
{
    public abstract class IRInstruction
    {
        public abstract string Display();
    }

    // 1. CONST - constant assignment
    public sealed class IRConstInst : IRInstruction
    {
        public IRValue Result { get; }
        public string Value { get; }
        public IRSourceLocation Location { get; }

        public IRConstInst(IRValue result, string value, IRSourceLocation location)
        {
            Result = result;
            Value = value;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{Result.GetIrType()} = CONST {Value}";
    }

    // 2. ARG - argument/parameter access
    public sealed class IRArgInst : IRInstruction
    {
        public IRValue Result { get; }
        public string ParamName { get; }
        public int ParamIndex { get; }
        public string IrType { get; }
        public IRSourceLocation Location { get; }

        public IRArgInst(IRValue result, string paramName, int paramIndex, string irType, IRSourceLocation location)
        {
            Result = result;
            ParamName = paramName;
            ParamIndex = paramIndex;
            IrType = irType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{IrType} = ARG {ParamName} {ParamIndex}";
    }

    // 3. ASSIGN - variable assignment
    public sealed class IRAssignInst : IRInstruction
    {
        public IRValue Dest { get; }
        public IRValue Src { get; }
        public IRSourceLocation Location { get; }

        public IRAssignInst(IRValue dest, IRValue src, IRSourceLocation location)
        {
            Dest = dest;
            Src = src;
            Location = location;
        }

        public override string Display() => $"{Dest.Display()}:{Dest.GetIrType()} = ASSIGN {Src.Display()}";
    }

    // 4. CAST - type conversion
    public sealed class IRCastInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue Operand { get; }
        public string FromType { get; }
        public string ToType { get; }
        public IRSourceLocation Location { get; }

        public IRCastInst(IRValue result, IRValue operand, string fromType, string toType, IRSourceLocation location)
        {
            Result = result;
            Operand = operand;
            FromType = fromType;
            ToType = toType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{ToType} = CAST {Operand.Display()} {FromType}->{ToType}";
    }

    // 5. BINOP - binary operations
    public sealed class IRBinOpInst : IRInstruction
    {
        public string Op { get; }
        public IRValue Left { get; }
        public IRValue Right { get; }
        public IRValue Result { get; }
        public string IrType { get; }
        public IRSourceLocation Location { get; }

        public IRBinOpInst(string op, IRValue left, IRValue right, IRValue result, string irType, IRSourceLocation location)
        {
            Op = op;
            Left = left;
            Right = right;
            Result = result;
            IrType = irType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{IrType} = BINOP {Op} {Left.Display()}, {Right.Display()}";
    }

    // 6. UNARYOP - unary operations
    public sealed class IRUnaryOpInst : IRInstruction
    {
        public string Op { get; }
        public IRValue Operand { get; }
        public IRValue Result { get; }
        public string IrType { get; }
        public IRSourceLocation Location { get; }

        public IRUnaryOpInst(string op, IRValue operand, IRValue result, string irType, IRSourceLocation location)
        {
            Op = op;
            Operand = operand;
            Result = result;
            IrType = irType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{IrType} = UNARYOP {Op} {Operand.Display()}";
    }

    // 7. RDMBR - read member
    public sealed class IRRdmbrInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue Obj { get; }
        public string FieldName { get; }
        public string IrType { get; }
        public IRSourceLocation Location { get; }

        public IRRdmbrInst(IRValue result, IRValue obj, string fieldName, string irType, IRSourceLocation location)
        {
            Result = result;
            Obj = obj;
            FieldName = fieldName;
            IrType = irType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{IrType} = RDMBR {Obj.Display()}, .{FieldName}";
    }

    // 8. WRMBR - write member
    public sealed class IRWrmbrInst : IRInstruction
    {
        public IRValue Obj { get; }
        public string FieldName { get; }
        public IRValue Value { get; }
        public IRSourceLocation Location { get; }

        public IRWrmbrInst(IRValue obj, string fieldName, IRValue value, IRSourceLocation location)
        {
            Obj = obj;
            FieldName = fieldName;
            Value = value;
            Location = location;
        }

        public override string Display() => $"WRMBR {Obj.Display()}, .{FieldName}, {Value.Display()}";
    }

    // 9. BR - unconditional branch
    public sealed class IRBrInst : IRInstruction
    {
        public IRLabelValue Target { get; }
        public IRSourceLocation Location { get; }

        public IRBrInst(IRLabelValue target, IRSourceLocation location)
        {
            Target = target;
            Location = location;
        }

        public override string Display() => $"BR {Target.Name}";
    }

    // 10. BR_COND - conditional branch
    public sealed class IRBrCondInst : IRInstruction
    {
        public IRValue Cond { get; }
        public IRLabelValue TrueLabel { get; }
        public IRLabelValue FalseLabel { get; }
        public IRSourceLocation Location { get; }

        public IRBrCondInst(IRValue cond, IRLabelValue trueLabel, IRLabelValue falseLabel, IRSourceLocation location)
        {
            Cond = cond;
            TrueLabel = trueLabel;
            FalseLabel = falseLabel;
            Location = location;
        }

        public override string Display() => $"BR_COND {Cond.Display()}, {TrueLabel.Name}, {FalseLabel.Name}";
    }

    // 11. RET - return with value
    public sealed class IRRetInst : IRInstruction
    {
        public IRValue Value { get; }
        public IRSourceLocation Location { get; }
        public int ReturnStatus { get; }

        public IRRetInst(IRValue value, IRSourceLocation location, int returnStatus = 3)
        {
            Value = value;
            Location = location;
            ReturnStatus = returnStatus;
        }

        public override string Display() => $"RET {Value.Display()}";
    }

    // 12. RET_VOID - return void
    public sealed class IRRetVoidInst : IRInstruction
    {
        public IRSourceLocation Location { get; }
        public int ReturnStatus { get; }

        public IRRetVoidInst(IRSourceLocation location, int returnStatus = 3)
        {
            Location = location;
            ReturnStatus = returnStatus;
        }

        public override string Display() => "RET_VOID";
    }

    // 13. CALL - function call with result
    public sealed class IRCallInst : IRInstruction
    {
        public string FuncName { get; }
        public List<IRValue> Args { get; }
        public IRValue ResultValue { get; }
        public string RetType { get; }
        public IRSourceLocation Location { get; }

        public IRCallInst(string funcName, List<IRValue> args, IRValue result, string retType, IRSourceLocation location)
        {
            FuncName = funcName;
            Args = args;
            ResultValue = result;
            RetType = retType;
            Location = location;
        }

        public override string Display()
        {
            var sb = new System.Text.StringBuilder();
            if (RetType != "void")
            {
                sb.Append($"{ResultValue.Display()}:{RetType} = ");
            }
            sb.Append($"CALL @{FuncName}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    // 14. CALL_VOID - void function call
    public sealed class IRCallVoidInst : IRInstruction
    {
        public string FuncName { get; }
        public List<IRValue> Args { get; }
        public IRSourceLocation Location { get; }

        public IRCallVoidInst(string funcName, List<IRValue> args, IRSourceLocation location)
        {
            FuncName = funcName;
            Args = args;
            Location = location;
        }

        public override string Display()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"CALL @{FuncName}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    // 15. CALL_VIRT - virtual call through interface_map
    public sealed class IRCallVirtInst : IRInstruction
    {
        public IRValue Obj { get; }
        public string InterfaceId { get; }
        public int VtableSlot { get; }
        public List<IRValue> Args { get; }
        public IRValue ResultValue { get; }
        public string RetType { get; }
        public IRSourceLocation Location { get; }

        public IRCallVirtInst(IRValue obj, string interfaceId, int vtableSlot, List<IRValue> args, IRValue result, string retType, IRSourceLocation location)
        {
            Obj = obj;
            InterfaceId = interfaceId;
            VtableSlot = vtableSlot;
            Args = args;
            ResultValue = result;
            RetType = retType;
            Location = location;
        }

        public override string Display()
        {
            var sb = new System.Text.StringBuilder();
            if (RetType != "void")
            {
                sb.Append($"{ResultValue.Display()}:{RetType} = ");
            }
            sb.Append($"CALL_VIRT {Obj.Display()}, interface={InterfaceId}, slot={VtableSlot}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    // 15b. CALL_FUNC_PTR - call through a function pointer register
    public sealed class IRCallFuncPtrInst : IRInstruction
    {
        public IRValue FuncPtr { get; }
        public List<IRValue> Args { get; }
        public IRValue ResultValue { get; }
        public string RetType { get; }
        public IRSourceLocation Location { get; }

        public IRCallFuncPtrInst(IRValue funcPtr, List<IRValue> args, IRValue result, string retType, IRSourceLocation location)
        {
            FuncPtr = funcPtr;
            Args = args;
            ResultValue = result;
            RetType = retType;
            Location = location;
        }

        public override string Display()
        {
            var sb = new StringBuilder();
            sb.Append($"{ResultValue.Display()}:{RetType} = CALL_FUNC_PTR {FuncPtr.Display()}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    public sealed class IRCallFuncPtrVoidInst : IRInstruction
    {
        public IRValue FuncPtr { get; }
        public List<IRValue> Args { get; }
        public IRSourceLocation Location { get; }

        public IRCallFuncPtrVoidInst(IRValue funcPtr, List<IRValue> args, IRSourceLocation location)
        {
            FuncPtr = funcPtr;
            Args = args;
            Location = location;
        }

        public override string Display()
        {
            var sb = new StringBuilder();
            sb.Append($"CALL_FUNC_PTR {FuncPtr.Display()}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    // 16. NEW - object creation
    public sealed class IRNewInst : IRInstruction
    {
        public string TypeName { get; }
        public List<IRValue> Args { get; }
        public IRValue Result { get; }
        public IRSourceLocation Location { get; }

        public IRNewInst(string typeName, List<IRValue> args, IRValue result, IRSourceLocation location)
        {
            TypeName = typeName;
            Args = args;
            Result = result;
            Location = location;
        }

        public override string Display()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Result.Display()}:ptr = NEW {TypeName}(");
            sb.Append(string.Join(", ", Args.Select(a => a.Display())));
            sb.Append(")");
            return sb.ToString();
        }
    }

    // 17. NEW_ENUM - enum creation
    public sealed class IRNewEnumInst : IRInstruction
    {
        public string TypeName { get; }
        public int VariantIdx { get; }
        public string VariantName { get; }
        public IRValue? Payload { get; }
        public IRValue Result { get; }
        public IRSourceLocation Location { get; }

        public IRNewEnumInst(string typeName, int variantIdx, string variantName, IRValue? payload, IRValue result, IRSourceLocation location)
        {
            TypeName = typeName;
            VariantIdx = variantIdx;
            VariantName = variantName;
            Payload = payload;
            Result = result;
            Location = location;
        }

        public override string Display()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Result.Display()} = NEW_ENUM {TypeName}.{VariantName}");
            if (Payload != null)
                sb.Append($"({Payload.Display()})");
            return sb.ToString();
        }
    }

    // 18. ISENUM - enum pattern matching
    public sealed class IRIsEnumInst : IRInstruction
    {
        public IRValue EnumValue { get; }
        public IRValue VariantIdx { get; }
        public IRValue Result { get; }
        public IRSourceLocation Location { get; }

        public IRIsEnumInst(IRValue enumValue, IRValue variantIdx, IRValue result, IRSourceLocation location)
        {
            EnumValue = enumValue;
            VariantIdx = variantIdx;
            Result = result;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:bool = ISENUM {EnumValue.Display()}, {VariantIdx.Display()}";
    }

    // 19. RDENUM - read enum payload
    public sealed class IRRdenumInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue EnumValue { get; }
        public string VariantName { get; }
        public string PayloadType { get; }
        public IRSourceLocation Location { get; }

        public IRRdenumInst(IRValue result, IRValue enumValue, string variantName, string payloadType, IRSourceLocation location)
        {
            Result = result;
            EnumValue = enumValue;
            VariantName = variantName;
            PayloadType = payloadType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{PayloadType} = RDENUM {EnumValue.Display()}, .{VariantName}";
    }

    // 20. ISINSTANCE - interface/class type check
    public sealed class IRIsInstanceInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue Obj { get; }
        public string TypeId { get; }
        public IRSourceLocation Location { get; }

        public IRIsInstanceInst(IRValue result, IRValue obj, string typeId, IRSourceLocation location)
        {
            Result = result;
            Obj = obj;
            TypeId = typeId;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:bool = ISINSTANCE {Obj.Display()}, {TypeId}";
    }

    // 21. BOX - heap-allocate value type for interface use
    public sealed class IRBoxInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue Operand { get; }
        public string SourceTypeName { get; }
        public IRSourceLocation Location { get; }

        public IRBoxInst(IRValue result, IRValue operand, string sourceTypeName, IRSourceLocation location)
        {
            Result = result;
            Operand = operand;
            SourceTypeName = sourceTypeName;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:ptr = BOX {Operand.Display()}, {SourceTypeName}";
    }

    // 22. UNBOX - extract value type from heap pointer
    public sealed class IRUnboxInst : IRInstruction
    {
        public IRValue Result { get; }
        public IRValue Operand { get; }
        public string TargetTypeName { get; }
        public string TargetIrType { get; }
        public IRSourceLocation Location { get; }

        public IRUnboxInst(IRValue result, IRValue operand, string targetTypeName, string targetIrType, IRSourceLocation location)
        {
            Result = result;
            Operand = operand;
            TargetTypeName = targetTypeName;
            TargetIrType = targetIrType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{TargetIrType} = UNBOX {Operand.Display()}, {TargetTypeName}";
    }

    // 23. LABEL - label marker
    public sealed class IRLabelInst : IRInstruction
    {
        public IRLabelValue Label { get; }

        public IRLabelInst(IRLabelValue label)
        {
            Label = label;
        }

        public override string Display() => $"{Label.Name}:";
    }

    // 24. GLOBAL_LOAD - load value from global variable
    public sealed class IRGlobalLoadInst : IRInstruction
    {
        public IRValue Result { get; }
        public string GlobalName { get; }
        public string IrType { get; }
        public IRSourceLocation Location { get; }

        public IRGlobalLoadInst(IRValue result, string globalName, string irType, IRSourceLocation location)
        {
            Result = result;
            GlobalName = globalName;
            IrType = irType;
            Location = location;
        }

        public override string Display() => $"{Result.Display()}:{IrType} = GLOBAL_LOAD @{GlobalName}";
    }

    // 25. GLOBAL_STORE - store value to global variable
    public sealed class IRGlobalStoreInst : IRInstruction
    {
        public string GlobalName { get; }
        public IRValue Value { get; }
        public IRSourceLocation Location { get; }

        public IRGlobalStoreInst(string globalName, IRValue value, IRSourceLocation location)
        {
            GlobalName = globalName;
            Value = value;
            Location = location;
        }

        public override string Display() => $"GLOBAL_STORE @{GlobalName}, {Value.Display()}";
    }
}
