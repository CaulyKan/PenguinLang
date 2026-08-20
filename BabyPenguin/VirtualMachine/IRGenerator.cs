using BabyPenguin.SemanticInterface;
using PenguinLangParser;

namespace BabyPenguin.VirtualMachine
{
    /// <summary>
    /// Translates BabyPenguin's existing compiled IR (BabyPenguinIR) into the new
    /// EmperorPenguin-compatible IR format. This reuses the existing semantic analysis
    /// and code generation pipeline, only replacing the IR representation.
    /// </summary>
    public class IRGenerator
    {
        private readonly SemanticModel _model;
        private readonly IRModule _module = new();

        public IRGenerator(SemanticModel model)
        {
            _model = model;
        }

        public IRModule Generate()
        {
            GenerateGlobalVariables();
            GenerateAllFunctions();
            GenerateInitialRoutines();
            return _module;
        }

        private void GenerateGlobalVariables()
        {
            foreach (var symbol in _model.Symbols.Where(s => !s.IsEnum && !s.IsLocal && !s.IsClassMember && !s.IsFunction))
            {
                var irType = IRTypeClassifier.ToIrType(symbol.TypeInfo);
                _module.AddGlobalVariable(new IRGlobalVariable(symbol.FullName(), irType));
            }
        }

        private void GenerateAllFunctions()
        {
            // Find all code containers with instructions
            var codeContainers = _model.FindAll(node => node is ICodeContainer cc && cc.Instructions.Count > 0)
                .Cast<ICodeContainer>()
                .ToList();

            foreach (var cc in codeContainers)
            {
                TranslateCodeContainer(cc);
            }
        }

        private void GenerateInitialRoutines()
        {
            // Find initial routines - they become entry points
            var initialRoutines = _model.FindAll(node => node is IInitialRoutine)
                .Cast<IInitialRoutine>()
                .ToList();

            foreach (var init in initialRoutines)
            {
                var funcName = SanitizeName(init.FullName());
                _module.AddEntryFunction(funcName);
            }
        }

        private void TranslateCodeContainer(ICodeContainer cc)
        {
            var containerName = SanitizeName(cc.FullName());
            var returnType = cc.ReturnTypeInfo != null ? IRTypeClassifier.ToIrType(cc.ReturnTypeInfo) : "void";
            var irFunc = new IRFunction(containerName, returnType)
            {
                DisplayName = cc.FullName(),
                IsExtern = false
            };

            _module.AddFunction(irFunc);

            var builder = new IRBuilder(irFunc);
            var symbolRegs = new Dictionary<string, IRValue>();
            var globalRegs = new Dictionary<string, string>(); // symbol.FullName() -> global name for GLOBAL_STORE
            var labelMap = new Dictionary<string, int>();

            // Allocate registers for all symbols (parameters and locals) via builder for proper index assignment
            foreach (var symbol in cc.Symbols)
            {
                var irType = IRTypeClassifier.ToIrType(symbol.TypeInfo);
                var reg = builder.AllocNamedReg(symbol.FullName(), irType, symbol.SourceLocation.RowStart, symbol.SourceLocation.ColStart);
                symbolRegs[symbol.FullName()] = reg;

                if (symbol.IsParameter)
                {
                    var loc = MakeLoc(symbol.SourceLocation);
                    builder.EmitArg(reg, symbol.Name, symbol.ParameterIndex, irType, loc);
                }
            }

            // Map label positions (first pass)
            for (int i = 0; i < cc.Instructions.Count; i++)
            {
                foreach (var label in cc.Instructions[i].Labels)
                {
                    labelMap[label] = i;
                }
            }

            // Translate instructions
            for (int ip = 0; ip < cc.Instructions.Count; ip++)
            {
                var inst = cc.Instructions[ip];
                var loc = MakeLoc(inst.SourceLocation);

                switch (inst)
                {
                    case AssignLiteralToSymbolInstruction cmd:
                        {
                            var reg = ResolveReg(cmd.Target);
                            var irType = IRTypeClassifier.ToIrType(cmd.Type);
                            builder.EmitConst(reg, cmd.LiteralValue, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), reg, loc);
                        }
                        break;

                    case AssignmentInstruction cmd:
                        {
                            var dest = ResolveReg(cmd.LeftHandSymbol);
                            var src = ResolveReg(cmd.RightHandSymbol);
                            builder.EmitAssign(dest, src, loc);
                            SyncGlobalIfNeeded(cmd.LeftHandSymbol.FullName(), dest, loc);
                        }
                        break;

                    case BinaryOperationInstruction cmd:
                        {
                            var left = ResolveReg(cmd.LeftSymbol);
                            var right = ResolveReg(cmd.RightSymbol);
                            var result = ResolveReg(cmd.Target);
                            var irType = IRTypeClassifier.ToIrType(cmd.Target.TypeInfo);
                            var opStr = TranslateBinaryOp(cmd.Operator);
                            builder.EmitBinOp(opStr, left, right, result, irType, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case UnaryOperationInstruction cmd:
                        {
                            var operand = ResolveReg(cmd.Operand);
                            var result = ResolveReg(cmd.Target);
                            var irType = IRTypeClassifier.ToIrType(cmd.Target.TypeInfo);
                            var opStr = TranslateUnaryOp(cmd.Operator);
                            builder.EmitUnaryOp(opStr, operand, result, irType, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case FunctionCallInstruction cmd:
                        {
                            var args = cmd.Arguments.Select(a => ResolveReg(a)).ToList();
                            var retType = cmd.Target != null
                                ? IRTypeClassifier.ToIrType(cmd.Target.TypeInfo)
                                : "void";

                            // Check if functionSymbol is a local/temp variable (function pointer for virtual dispatch)
                            if (cmd.FunctionSymbol.IsLocal || cmd.FunctionSymbol.IsTemp)
                            {
                                // Function pointer call: resolve the register at runtime
                                var funcPtr = ResolveReg(cmd.FunctionSymbol);
                                if (cmd.Target != null)
                                {
                                    var result = ResolveReg(cmd.Target);
                                    builder.EmitCallFuncPtr(funcPtr, args, result, retType, loc);
                                    SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                                }
                                else
                                {
                                    builder.EmitCallFuncPtrVoid(funcPtr, args, loc);
                                }
                            }
                            else
                            {
                                var funcName = SanitizeName(cmd.FunctionSymbol.FullName());
                                if (cmd.Target != null)
                                {
                                    var result = ResolveReg(cmd.Target);
                                    builder.EmitCall(funcName, args, result, retType, loc);
                                    SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                                }
                                else
                                {
                                    builder.EmitCallVoid(funcName, args, loc);
                                }
                            }
                        }
                        break;

                    case NewInstanceInstruction cmd:
                        {
                            var result = ResolveReg(cmd.Target);
                            var typeName = cmd.Target.TypeInfo.TypeNode?.FullName() ?? cmd.Target.TypeInfo.FullName();
                            builder.EmitNew(typeName, [], result, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case ReadMemberInstruction cmd:
                        {
                            var obj = ResolveReg(cmd.MemberOwnerSymbol);
                            var result = ResolveReg(cmd.Target);
                            var irType = IRTypeClassifier.ToIrType(cmd.Target.TypeInfo);
                            builder.EmitRdmbr(result, obj, cmd.Member.Name, irType, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case WriteMemberInstruction cmd:
                        {
                            var obj = ResolveReg(cmd.MemberOwnerSymbol);
                            var value = ResolveReg(cmd.Value);
                            builder.EmitWrmbr(obj, cmd.Member.Name, value, loc);
                        }
                        break;

                    case CastInstruction cmd:
                        {
                            var operand = ResolveReg(cmd.Operand);
                            var result = ResolveReg(cmd.Target);
                            var fromType = IRTypeClassifier.ToIrType(cmd.Operand.TypeInfo);
                            var toType = IRTypeClassifier.ToIrType(cmd.TypeInfo);
                            builder.EmitCast(result, operand, fromType, toType, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case WriteEnumInstruction cmd:
                        {
                            var enumVar = ResolveReg(cmd.TargetEnum);
                            var value = ResolveReg(cmd.Value);
                            builder.EmitWrmbr(enumVar, "_containing_value", value, loc);
                        }
                        break;

                    case ReadEnumInstruction cmd:
                        {
                            var enumVar = ResolveReg(cmd.Enum);
                            var result = ResolveReg(cmd.TargetValue);
                            var payloadType = IRTypeClassifier.ToIrType(cmd.TargetValue.TypeInfo);
                            builder.EmitRdenum(result, enumVar, "_payload", payloadType, loc);
                            SyncGlobalIfNeeded(cmd.TargetValue.FullName(), result, loc);
                        }
                        break;

                    case TypeCheckInstruction cmd:
                        {
                            var operand = ResolveReg(cmd.Operand);
                            var result = ResolveReg(cmd.Target);
                            var typeId = cmd.CheckType.FullName();
                            // Strip mutability prefixes (!mut / mut) for type comparison
                            if (typeId.StartsWith("!mut "))
                                typeId = typeId[5..];
                            else if (typeId.StartsWith("mut "))
                                typeId = typeId[4..];
                            builder.EmitIsInstance(result, operand, typeId, loc);
                            SyncGlobalIfNeeded(cmd.Target.FullName(), result, loc);
                        }
                        break;

                    case GotoInstruction cmd:
                        {
                            if (cmd.Condition != null)
                            {
                                var cond = ResolveReg(cmd.Condition);
                                var targetLabel = cmd.TargetLabel;

                                // Create labels for branch targets
                                var trueLabel = new IRLabelValue(cmd.JumpOnCondition ? $"L_{targetLabel}" : $"L_skip_{ip}");
                                var falseLabel = new IRLabelValue(cmd.JumpOnCondition ? $"L_skip_{ip}" : $"L_{targetLabel}");

                                builder.EmitBrCond(cond, trueLabel, falseLabel, loc);

                                // Emit the branch-not-taken path label
                                builder.EmitLabel(cmd.JumpOnCondition ? falseLabel : trueLabel);

                                // Schedule the target label to be emitted when we reach it
                                // For unconditional jumps, emit the label here
                                if (!cmd.JumpOnCondition)
                                {
                                    // We need to emit a BR to target and a label for the fall-through
                                }
                            }
                            else
                            {
                                var targetLabel = new IRLabelValue($"L_{cmd.TargetLabel}");
                                builder.EmitBr(targetLabel, loc);
                            }
                        }
                        break;

                    case ReturnInstruction cmd:
                        {
                            var returnStatus = (int)cmd.ReturnStatus;
                            if (cmd.RetValue != null)
                            {
                                var retVal = ResolveReg(cmd.RetValue);
                                builder.EmitRet(retVal, loc, returnStatus);
                            }
                            else
                            {
                                builder.EmitRetVoid(loc, returnStatus);
                            }
                        }
                        break;

                    case SignalInstruction cmd:
                        // Signals are VM-specific (breakpoints) - skip in new IR
                        break;

                    case NopInstuction:
                        // NOPs carry labels - emit label markers
                        foreach (var label in inst.Labels)
                        {
                            builder.EmitLabel(new IRLabelValue($"L_{label}"));
                        }
                        break;
                }
            }

            IRValue ResolveReg(ISymbol symbol)
            {
                if (symbolRegs.TryGetValue(symbol.FullName(), out var reg))
                    return reg;

                var irType = IRTypeClassifier.ToIrType(symbol.TypeInfo);

                // Function reference: use a constant with the sanitized function name
                if (symbol.IsFunction)
                {
                    var funcConst = new IRConstant(SanitizeName(symbol.FullName()), irType);
                    symbolRegs[symbol.FullName()] = funcConst;
                    return funcConst;
                }

                // Global variable: emit a GLOBAL_LOAD instruction
                if (!symbol.IsLocal && !symbol.IsParameter && !symbol.IsClassMember && !symbol.IsEnum)
                {
                    var globalReg = builder.AllocTemp(irType);
                    builder.EmitGlobalLoad(globalReg, symbol.FullName(), irType, MakeLoc(symbol.SourceLocation));
                    symbolRegs[symbol.FullName()] = globalReg;
                    globalRegs[symbol.FullName()] = symbol.FullName();
                    return globalReg;
                }

                // For temps or other symbols not in the current scope, allocate a new register
                var newReg = builder.AllocTemp(irType);
                symbolRegs[symbol.FullName()] = newReg;
                return newReg;
            }

            void SyncGlobalIfNeeded(string symbolFullName, IRValue reg, IRSourceLocation loc)
            {
                if (globalRegs.TryGetValue(symbolFullName, out var globalName))
                {
                    builder.EmitGlobalStore(globalName, reg, loc);
                }
            }

            MarkWriteChains(irFunc, cc.Symbols.Where(s => s.IsTemp).Select(s => s.FullName()).ToHashSet());
        }

        /// <summary>
        /// Value-copy semantics: RDMBR/RDENUM results deep-copy value types
        /// (binding / extraction copies). When the result register is instead
        /// consumed as the OWNER of a WRMBR (`w.p.x = 9`, `e.a.b.x += 1`) or as
        /// a method-call receiver (`e.a.increment()` with mut this), it must
        /// ALIAS the slot (lvalue addressing, no copy). Scan backwards from
        /// every WRMBR owner / call-funcptr receiver: mark the defining
        /// RDMBR/RDENUM as a write chain and continue through its object
        /// register so nested chains alias at every level. Only
        /// single-definition TEMP registers qualify — named registers are
        /// bindings (copies by construction). GLOBAL_LOAD never copies, so it
        /// terminates a chain without marking.
        /// </summary>
        private static void MarkWriteChains(IRFunction func, HashSet<string> tempNames)
        {
            // Temps materialize as named registers (allocated in the cc.Symbols
            // loop) — identify them by their symbol name, not the C# type.
            bool IsTempReg(IRValue v) =>
                v is IRTempRegister || (v is IRNamedRegister nr && tempNames.Contains(nr.Name));

            static int RegIndex(IRValue v) => v switch
            {
                IRTempRegister t => t.Index,
                IRNamedRegister n => n.Index,
                _ => -1,
            };

            var defs = new Dictionary<int, IRInstruction>();
            var multiDef = new HashSet<int>();
            foreach (var inst in func.Instructions)
            {
                var def = inst switch
                {
                    IRConstInst ci => ci.Result,
                    IRArgInst ai => ai.Result,
                    IRAssignInst ai => ai.Dest,
                    IRCastInst ci => ci.Result,
                    IRBinOpInst bi => bi.Result,
                    IRUnaryOpInst ui => ui.Result,
                    IRRdmbrInst ri => ri.Result,
                    IRNewInst ni => ni.Result,
                    IRNewEnumInst nei => nei.Result,
                    IRIsEnumInst isi => isi.Result,
                    IRRdenumInst ri => ri.Result,
                    IRIsInstanceInst isi => isi.Result,
                    IRGlobalLoadInst gi => gi.Result,
                    IRCallInst ci => ci.ResultValue,
                    IRCallFuncPtrInst ci => ci.ResultValue,
                    _ => null,
                };
                if (def != null && IsTempReg(def))
                {
                    var idx = RegIndex(def);
                    if (!defs.TryAdd(idx, inst))
                        multiDef.Add(idx);
                }
            }

            // Unbox (ref -> struct) casts alias the boxed object — the
            // vtable-stub idiom `let self: mut Self = cast<mut Self>(this)`
            // writes through to the box. The ASSIGN materializing the cast
            // operand must not fork the box first.
            foreach (var inst in func.Instructions)
            {
                if (inst is IRCastInst c
                    && c.FromType.StartsWith("ref<") && c.ToType.StartsWith("struct<")
                    && IsTempReg(c.Operand)
                    && RegIndex(c.Operand) is var oi && !multiDef.Contains(oi)
                    && defs.TryGetValue(oi, out var d) && d is IRAssignInst a)
                {
                    a.IsAliasChain = true;
                }
            }

            var marked = new HashSet<int>();
            var work = new Stack<IRValue>();

            IRValue? ReceiverSeed(IRValue funcPtr) =>
                IsTempReg(funcPtr)
                    && defs.TryGetValue(RegIndex(funcPtr), out var d)
                    && d is IRRdmbrInst r
                    ? r.Obj
                    : null;

            foreach (var inst in func.Instructions)
            {
                IRValue? seed = inst switch
                {
                    IRWrmbrInst w => w.Obj,
                    // Method receiver: the fat-pointer RDMBR reads the method
                    // off the receiver object; that object must alias its slot.
                    IRCallFuncPtrInst c => ReceiverSeed(c.FuncPtr),
                    IRCallFuncPtrVoidInst c => ReceiverSeed(c.FuncPtr),
                    IRCallVirtInst v => v.Obj,
                    _ => null,
                };
                if (seed != null)
                    work.Push(seed);
            }

            while (work.Count > 0)
            {
                var val = work.Pop();
                if (!IsTempReg(val))
                    continue;
                var idx = RegIndex(val);
                if (multiDef.Contains(idx) || !defs.TryGetValue(idx, out var def))
                    continue;
                if (!marked.Add(idx))
                    continue;
                switch (def)
                {
                    case IRRdmbrInst r:
                        r.IsWriteChain = true;
                        work.Push(r.Obj);
                        break;
                    case IRRdenumInst e:
                        e.IsWriteChain = true;
                        work.Push(e.EnumValue);
                        break;
                    case IRAssignInst a:
                        // Receiver materialization (`c.increment()` evaluates
                        // its receiver through ASSIGN): alias instead of copy
                        // and continue the chain at the source register.
                        a.IsAliasChain = true;
                        work.Push(a.Src);
                        break;
                }
            }
        }

        private static string TranslateBinaryOp(BinaryOperatorEnum op) => op switch
        {
            BinaryOperatorEnum.Add => "add",
            BinaryOperatorEnum.Subtract => "sub",
            BinaryOperatorEnum.Multiply => "mul",
            BinaryOperatorEnum.Divide => "div",
            BinaryOperatorEnum.Modulo => "mod",
            BinaryOperatorEnum.BitwiseAnd => "band",
            BinaryOperatorEnum.BitwiseOr => "bor",
            BinaryOperatorEnum.BitwiseXor => "bxor",
            BinaryOperatorEnum.LogicalAnd => "land",
            BinaryOperatorEnum.LogicalOr => "lor",
            BinaryOperatorEnum.Equal => "eq",
            BinaryOperatorEnum.NotEqual => "ne",
            BinaryOperatorEnum.LessThan => "lt",
            BinaryOperatorEnum.GreaterThan => "gt",
            BinaryOperatorEnum.LessThanOrEqual => "le",
            BinaryOperatorEnum.GreaterThanOrEqual => "ge",
            _ => throw new NotImplementedException($"Binary op {op} not supported")
        };

        private static string TranslateUnaryOp(UnaryOperatorEnum op) => op switch
        {
            UnaryOperatorEnum.Minus => "neg",
            UnaryOperatorEnum.Plus => "plus",
            UnaryOperatorEnum.BitwiseNot => "bnot",
            UnaryOperatorEnum.LogicalNot => "lnot",
            _ => throw new NotImplementedException($"Unary op {op} not supported")
        };

        private static string SanitizeName(string name) => name.Replace(".", "_");

        private static IRSourceLocation MakeLoc(SourceLocation sl) =>
            new(sl.FileName, sl.RowStart, sl.ColStart);
    }
}
