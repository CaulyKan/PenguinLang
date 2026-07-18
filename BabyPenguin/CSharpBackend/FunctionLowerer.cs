using System.Collections.Generic;
using System.Linq;
using System.Text;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>
    /// Lowers one IRFunction to a C# method (sync). Handles arithmetic/control/calls plus:
    /// NEW (object alloc), data-field RDMBR/WRMBR (direct field access), instance-method dispatch
    /// (RDMBR of a method → funptr provenance → CALL_FUNC_PTR emits a direct static call with the
    /// receiver prepended), and minimal enum ops. Unsupported instructions throw at runtime.
    /// </summary>
    public class FunctionLowerer
    {
        private readonly CSharpEmitter _emitter;
        private readonly StringBuilder _sb = new();
        private IRFunction _f = null!;
        private string _retCs = "void";
        private readonly Dictionary<int, FunptrSrc> _funptr = new();

        private abstract record FunptrSrc;
        private record MethodRef(IRValue Obj, string MethodCs) : FunptrSrc;
        private record FuncRef(string Name) : FunptrSrc;

        public FunctionLowerer(CSharpEmitter emitter) { _emitter = emitter; }

        public string Lower(IRFunction f)
        {
            _f = f;
            _sb.Clear();
            _funptr.Clear();

            _retCs = _emitter.CsType(string.IsNullOrEmpty(f.ReturnType) ? "void" : f.ReturnType);
            var retCs = _retCs;
            var name = _emitter.MangleName(f.Name);

            var paramByIndex = new SortedDictionary<int, (string Name, string IrType, int RegIndex)>();
            foreach (var ai in f.Instructions.OfType<IRArgInst>())
                paramByIndex[ai.ParamIndex] = (ai.ParamName, ai.IrType, ((IRNamedRegister)ai.Result).Index);
            var parms = string.Join(", ", paramByIndex.Select(kv => $"{_emitter.CsType(kv.Value.IrType)} p_{kv.Key}"));

            _sb.AppendLine($"// ===== IR: {f.DisplayName} =====");
            _sb.AppendLine($"public static {retCs} {name}({parms})");
            _sb.AppendLine("{");

            foreach (var (index, irType) in CollectRegisters(f).OrderBy(kv => kv.Key))
                _sb.AppendLine($"    {_emitter.CsType(irType)} r_{index} = default;");

            foreach (var kv in paramByIndex)
                _sb.AppendLine($"    r_{kv.Value.RegIndex} = p_{kv.Key};");

            foreach (var inst in f.Instructions)
                LowerInstruction(inst);

            if (retCs != "void")
                _sb.AppendLine("    return default;");
            _sb.AppendLine("}");
            return _sb.ToString();
        }

        private static Dictionary<int, string> CollectRegisters(IRFunction f)
        {
            var regs = new Dictionary<int, string>();
            foreach (var inst in f.Instructions)
                foreach (var v in OperandsOf(inst))
                    if (v is IRNamedRegister nr) regs[nr.Index] = nr.IrType;
                    else if (v is IRTempRegister tr) regs[tr.Index] = tr.IrType;
            // NEW results are typed "ptr" in IR; use the actual constructed type instead.
            foreach (var inst in f.Instructions)
            {
                switch (inst)
                {
                    case IRNewInst n: regs[((IRNamedRegister)n.Result).Index] = n.TypeName; break;
                    case IRNewEnumInst n: regs[((IRNamedRegister)n.Result).Index] = $"enum<{n.TypeName}>"; break;
                }
            }
            return regs;
        }

        private static IEnumerable<IRValue> OperandsOf(IRInstruction inst)
        {
            switch (inst)
            {
                case IRConstInst c: yield return c.Result; break;
                case IRArgInst a: yield return a.Result; break;
                case IRAssignInst a: yield return a.Dest; yield return a.Src; break;
                case IRCastInst c: yield return c.Result; yield return c.Operand; break;
                case IRBinOpInst b: yield return b.Result; yield return b.Left; yield return b.Right; break;
                case IRUnaryOpInst u: yield return u.Result; yield return u.Operand; break;
                case IRRdmbrInst r: yield return r.Result; yield return r.Obj; break;
                case IRWrmbrInst w: yield return w.Obj; yield return w.Value; break;
                case IRBrCondInst b: yield return b.Cond; break;
                case IRRetInst r: if (r.Value != null) yield return r.Value; break;
                case IRCallInst c: foreach (var a in c.Args) yield return a; if (c.RetType != "void") yield return c.ResultValue; break;
                case IRCallVoidInst c: foreach (var a in c.Args) yield return a; break;
                case IRCallFuncPtrInst c: yield return c.FuncPtr; foreach (var a in c.Args) yield return a; if (c.RetType != "void") yield return c.ResultValue; break;
                case IRCallFuncPtrVoidInst c: yield return c.FuncPtr; foreach (var a in c.Args) yield return a; break;
                case IRNewInst n: foreach (var a in n.Args) yield return a; yield return n.Result; break;
                case IRNewEnumInst n: if (n.Payload != null) yield return n.Payload!; yield return n.Result; break;
                case IRIsEnumInst i: yield return i.EnumValue; yield return i.VariantIdx; yield return i.Result; break;
                case IRRdenumInst r: yield return r.Result; yield return r.EnumValue; break;
                case IRGlobalLoadInst g: yield return g.Result; break;
                case IRGlobalStoreInst g: yield return g.Value; break;
                case IRIsInstanceInst i: yield return i.Result; yield return i.Obj; break;
                case IRBoxInst b: yield return b.Result; yield return b.Operand; break;
                case IRUnboxInst u: yield return u.Result; yield return u.Operand; break;
            }
        }

        private void LowerInstruction(IRInstruction inst)
        {
            void Line(string s) => _sb.AppendLine("    " + s);
            void Comment() => _sb.AppendLine($"    // {inst.Display()}");
            Comment();
            switch (inst)
            {
                case IRArgInst: break;
                case IRLabelInst l: _sb.AppendLine($"    L_{_emitter.Mangler.Mangle(l.Label.Name)}:;"); break;
                case IRConstInst c:
                    if (IsFunptrType(c.Result))
                    {
                        _funptr[Reg(c.Result)] = new FuncRef(c.Value);
                        break; // virtual: consumed by CALL_FUNC_PTR
                    }
                    Line($"r_{Reg(c.Result)} = {_emitter.Literal(c.Value, c.Result.GetIrType())};");
                    break;
                case IRAssignInst a:
                    if (a.Src is IRConstant kc && kc.IrType == "funptr")
                    {
                        _funptr[Reg(a.Dest)] = new FuncRef(kc.Value);
                        break;
                    }
                    Line($"r_{Reg(a.Dest)} = {_emitter.Operand(a.Src)};");
                    break;
                case IRCastInst c: Line($"r_{Reg(c.Result)} = {CastExpr(c.Operand, c.FromType, c.ToType)};"); break;
                case IRBinOpInst b: Line($"r_{Reg(b.Result)} = ({_emitter.CsType(b.IrType)})({_emitter.Operand(b.Left)} {CSharpEmitter.BinOp(b.Op)} {_emitter.Operand(b.Right)});"); break;
                case IRUnaryOpInst u: Line($"r_{Reg(u.Result)} = ({_emitter.CsType(u.IrType)})({CSharpEmitter.UnaryOp(u.Op)}{_emitter.Operand(u.Operand)});"); break;
                case IRRdmbrInst r:
                    if (IsFunptrType(r.Result))
                    {
                        _funptr[Reg(r.Result)] = new MethodRef(r.Obj, _emitter.MethodCsName(r.Obj.GetIrType(), r.FieldName));
                        break; // virtual method reference
                    }
                    Line($"r_{Reg(r.Result)} = {_emitter.Operand(r.Obj)}.{_emitter.Mangler.Mangle(r.FieldName)};");
                    break;
                case IRWrmbrInst w: Line($"{_emitter.Operand(w.Obj)}.{_emitter.Mangler.Mangle(w.FieldName)} = {_emitter.Operand(w.Value)};"); break;
                case IRBrInst b: Line($"goto L_{_emitter.Mangler.Mangle(b.Target.Name)};"); break;
                case IRBrCondInst b: Line($"if ({_emitter.Operand(b.Cond)}) goto L_{_emitter.Mangler.Mangle(b.TrueLabel.Name)}; else goto L_{_emitter.Mangler.Mangle(b.FalseLabel.Name)};"); break;
                case IRRetInst r: Line($"return {_emitter.Operand(r.Value)};"); break;
                case IRRetVoidInst: Line(_retCs == "void" ? "return;" : "return default;"); break;
                case IRCallInst c: EmitCall(c.FuncName, c.Args, c.RetType == "void" ? null : c.ResultValue, c.RetType, Line); break;
                case IRCallVoidInst c: EmitCall(c.FuncName, c.Args, null, "void", Line); break;
                case IRCallFuncPtrInst c: EmitFuncPtrCall(c.FuncPtr, c.Args, c.ResultValue, c.RetType, Line); break;
                case IRCallFuncPtrVoidInst c: EmitFuncPtrCall(c.FuncPtr, c.Args, null, "void", Line); break;
                case IRNewInst n:
                    Line($"r_{Reg(n.Result)} = new {_emitter.MangleName(n.TypeName)}();");
                    break;
                case IRNewEnumInst n:
                    Line($"r_{Reg(n.Result)}._value = {n.VariantIdx};");
                    if (n.Payload != null) Line($"r_{Reg(n.Result)}._containing_value = (object){_emitter.Operand(n.Payload)};");
                    break;
                case IRIsEnumInst i: Line($"r_{Reg(i.Result)} = ({_emitter.Operand(i.EnumValue)}._value == {_emitter.Operand(i.VariantIdx)});"); break;
                case IRRdenumInst r: Line($"r_{Reg(r.Result)} = ({_emitter.CsType(r.Result.GetIrType())})(object){_emitter.Operand(r.EnumValue)}._containing_value;"); break;
                case IRGlobalLoadInst g: Line($"r_{Reg(g.Result)} = G.{_emitter.Mangler.Mangle(g.GlobalName)};"); break;
                case IRGlobalStoreInst g: Line($"G.{_emitter.Mangler.Mangle(g.GlobalName)} = {_emitter.Operand(g.Value)};"); break;
                default:
                    Line($"throw new System.NotImplementedException(\"cs-lower: {inst.GetType().Name}\");");
                    break;
            }
        }

        private void EmitCall(string funcName, List<IRValue> args, IRValue? result, string retType, Action<string> Line)
        {
            var csArgs = string.Join(", ", args.Select(a => _emitter.Operand(a)));
            var fn = _emitter.MangleName(funcName);
            // Cast the result to its declared type: externs like ICopy<T>.copy return object, so the
            // (concrete-typed) result register needs an explicit downcast/unbox.
            if (retType != "void" && result != null)
                Line($"r_{Reg(result)} = ({_emitter.CsType(result.GetIrType())}){fn}({csArgs});");
            else if (retType != "void") Line($"return {fn}({csArgs});");
            else Line($"{fn}({csArgs});");
        }

        private void EmitFuncPtrCall(IRValue funcPtr, List<IRValue> args, IRValue? result, string retType, Action<string> Line)
        {
            if (_funptr.TryGetValue(Reg(funcPtr), out var src))
            {
                if (src is MethodRef mr)
                {
                    // The for-loop/iterator pattern passes the receiver explicitly as arg[0]
                    // (CALL_FUNC_PTR %fp(iter)); a direct method call (c.inc()) passes 0 args and relies
                    // on the bound receiver. Prepend the receiver only when it isn't already arg[0].
                    bool receiverExplicit = args.Count > 0 && ReferenceEquals(args[0], mr.Obj);
                    var csArgs = receiverExplicit
                        ? string.Join(", ", args.Select(a => _emitter.Operand(a)))
                        : string.Join(", ", new[] { _emitter.Operand(mr.Obj) }.Concat(args.Select(a => _emitter.Operand(a))));
                    if (result != null && retType != "void")
                        // Cast the result: interface-method externs (e.g. ICopy.copy) return object.
                        Line($"r_{Reg(result)} = ({_emitter.CsType(result.GetIrType())}){mr.MethodCs}({csArgs});");
                    else if (retType != "void") Line($"return {mr.MethodCs}({csArgs});");
                    else Line($"{mr.MethodCs}({csArgs});");
                    return;
                }
                if (src is FuncRef fr)
                {
                    var csArgs = string.Join(", ", args.Select(a => _emitter.Operand(a)));
                    var fn = _emitter.MangleName(fr.Name);
                    if (retType != "void" && result != null)
                        Line($"r_{Reg(result)} = ({_emitter.CsType(result.GetIrType())}){fn}({csArgs});");
                    else if (retType != "void") Line($"return {fn}({csArgs});");
                    else Line($"{fn}({csArgs});");
                    return;
                }
            }
            Line($"throw new System.NotImplementedException(\"cs-lower: CALL_FUNC_PTR without provenance\");");
        }

        private static bool IsFunptrType(IRValue v) => v.GetIrType() == "funptr";

        private string CastExpr(IRValue operand, string fromType, string toType)
        {
            var op = _emitter.Operand(operand);
            if (fromType == toType) return op;
            bool toIsString = toType == "string" || toType == "ref<string>";
            bool fromIsString = fromType == "string" || fromType == "ref<string>";
            if (toIsString)
            {
                if (fromIsString) return op;
                if (fromType.StartsWith("enum<")) return $"{_emitter.CsType(fromType)}.__ToName({op})";
                if (fromType == "bool") return $"({op} ? \"true\" : \"false\")";
                return $"{op}.ToString()";
            }
            var fromCs = _emitter.CsType(fromType);
            var toCs = _emitter.CsType(toType);
            if (fromCs == toCs) return op;
            // Interface(object) -> concrete class/struct needs an explicit downcast; concrete -> interface
            // (object) is an implicit no-op; same-object ref/struct/enum casts are no-ops.
            if (fromCs == "object" && toCs != "object") return $"({toCs}){op}";
            if ((fromType.StartsWith("ref<") || fromType.StartsWith("struct<") || fromType.StartsWith("enum<")) &&
                (toType.StartsWith("ref<") || toType.StartsWith("struct<") || toType.StartsWith("enum<")))
                return op;
            return $"({toCs}){op}";
        }

        private static int Reg(IRValue v) => v switch
        {
            IRNamedRegister nr => nr.Index,
            IRTempRegister tr => tr.Index,
            _ => throw new System.InvalidOperationException($"register operand expected, got {v?.GetType().Name}")
        };
    }
}
