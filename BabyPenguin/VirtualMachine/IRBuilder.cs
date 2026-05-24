namespace BabyPenguin.VirtualMachine
{
    public class IRBuilder
    {
        private readonly IRFunction _function;

        public IRBuilder(IRFunction function)
        {
            _function = function;
        }

        public IRValue AllocTemp(string irType) => _function.AllocTemp(irType);
        public IRLabelValue AllocLabel(string prefix) => _function.AllocLabel(prefix);
        public IRValue AllocNamedReg(string name, string irType, int line = 0, int col = 0) => _function.AllocNamedReg(name, irType, line, col);
        public IRValue AllocParam(string name, string irType, int line = 0, int col = 0) => _function.AllocParam(name, irType, line, col);

        public void EmitConst(IRValue result, string value, IRSourceLocation loc) =>
            _function.AddInst(new IRConstInst(result, value, loc));

        public void EmitArg(IRValue result, string paramName, int paramIndex, string irType, IRSourceLocation loc) =>
            _function.AddInst(new IRArgInst(result, paramName, paramIndex, irType, loc));

        public void EmitAssign(IRValue dest, IRValue src, IRSourceLocation loc) =>
            _function.AddInst(new IRAssignInst(dest, src, loc));

        public void EmitCast(IRValue result, IRValue operand, string fromType, string toType, IRSourceLocation loc) =>
            _function.AddInst(new IRCastInst(result, operand, fromType, toType, loc));

        public void EmitBinOp(string op, IRValue left, IRValue right, IRValue result, string irType, IRSourceLocation loc) =>
            _function.AddInst(new IRBinOpInst(op, left, right, result, irType, loc));

        public void EmitUnaryOp(string op, IRValue operand, IRValue result, string irType, IRSourceLocation loc) =>
            _function.AddInst(new IRUnaryOpInst(op, operand, result, irType, loc));

        public void EmitRdmbr(IRValue result, IRValue obj, string fieldName, string irType, IRSourceLocation loc) =>
            _function.AddInst(new IRRdmbrInst(result, obj, fieldName, irType, loc));

        public void EmitWrmbr(IRValue obj, string fieldName, IRValue value, IRSourceLocation loc) =>
            _function.AddInst(new IRWrmbrInst(obj, fieldName, value, loc));

        public void EmitBr(IRLabelValue target, IRSourceLocation loc) =>
            _function.AddInst(new IRBrInst(target, loc));

        public void EmitBrCond(IRValue cond, IRLabelValue trueLabel, IRLabelValue falseLabel, IRSourceLocation loc) =>
            _function.AddInst(new IRBrCondInst(cond, trueLabel, falseLabel, loc));

        public void EmitRet(IRValue value, IRSourceLocation loc, int returnStatus = 3) =>
            _function.AddInst(new IRRetInst(value, loc, returnStatus));

        public void EmitRetVoid(IRSourceLocation loc, int returnStatus = 3) =>
            _function.AddInst(new IRRetVoidInst(loc, returnStatus));

        public void EmitCall(string funcName, List<IRValue> args, IRValue result, string retType, IRSourceLocation loc) =>
            _function.AddInst(new IRCallInst(funcName, args, result, retType, loc));

        public void EmitCallVoid(string funcName, List<IRValue> args, IRSourceLocation loc) =>
            _function.AddInst(new IRCallVoidInst(funcName, args, loc));

        public void EmitCallVirt(IRValue obj, string interfaceId, int vtableSlot, List<IRValue> args, IRValue result, string retType, IRSourceLocation loc) =>
            _function.AddInst(new IRCallVirtInst(obj, interfaceId, vtableSlot, args, result, retType, loc));

        public void EmitCallFuncPtr(IRValue funcPtr, List<IRValue> args, IRValue result, string retType, IRSourceLocation loc) =>
            _function.AddInst(new IRCallFuncPtrInst(funcPtr, args, result, retType, loc));

        public void EmitCallFuncPtrVoid(IRValue funcPtr, List<IRValue> args, IRSourceLocation loc) =>
            _function.AddInst(new IRCallFuncPtrVoidInst(funcPtr, args, loc));

        public void EmitNew(string typeName, List<IRValue> args, IRValue result, IRSourceLocation loc) =>
            _function.AddInst(new IRNewInst(typeName, args, result, loc));

        public void EmitNewEnum(string typeName, int variantIdx, string variantName, IRValue? payload, IRValue result, IRSourceLocation loc) =>
            _function.AddInst(new IRNewEnumInst(typeName, variantIdx, variantName, payload, result, loc));

        public void EmitIsEnum(IRValue enumValue, IRValue variantIdx, IRValue result, IRSourceLocation loc) =>
            _function.AddInst(new IRIsEnumInst(enumValue, variantIdx, result, loc));

        public void EmitRdenum(IRValue result, IRValue enumValue, string variantName, string payloadType, IRSourceLocation loc) =>
            _function.AddInst(new IRRdenumInst(result, enumValue, variantName, payloadType, loc));

        public void EmitIsInstance(IRValue result, IRValue obj, string typeId, IRSourceLocation loc) =>
            _function.AddInst(new IRIsInstanceInst(result, obj, typeId, loc));

        public void EmitBox(IRValue result, IRValue operand, string sourceTypeName, IRSourceLocation loc) =>
            _function.AddInst(new IRBoxInst(result, operand, sourceTypeName, loc));

        public void EmitUnbox(IRValue result, IRValue operand, string targetTypeName, string targetIrType, IRSourceLocation loc) =>
            _function.AddInst(new IRUnboxInst(result, operand, targetTypeName, targetIrType, loc));

        public void EmitLabel(IRLabelValue label) =>
            _function.AddInst(new IRLabelInst(label));

        public void EmitGlobalLoad(IRValue result, string globalName, string irType, IRSourceLocation loc) =>
            _function.AddInst(new IRGlobalLoadInst(result, globalName, irType, loc));

        public void EmitGlobalStore(string globalName, IRValue value, IRSourceLocation loc) =>
            _function.AddInst(new IRGlobalStoreInst(globalName, value, loc));
    }
}
