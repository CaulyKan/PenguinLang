using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using Mono.Cecil;
using BabyPenguin.Type;
using BabyPenguin.SemanticInterface;
using System.Collections;
using PenguinLangParser;
using PenguinLangParser.SyntaxNodes;

namespace BabyPenguin.VirtualMachine
{
    class ExternFunctions
    {
        public static void Build(BabyPenguinVM vm)
        {
            AddPrint(vm);
            AddCopy(vm);
            AddAtmoic(vm);
            AddList(vm);
            AddRoutineContext(vm);

            AddFile(vm);
            AddArgs(vm);
            AddStringBuilder(vm);
            // AddMap(vm);
        }

        public static void AddPrint(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.print", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s);
            });

            vm.Global.RegisterExternFunction("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s, true);
            });

            vm.Global.RegisterExternFunction("__builtin.eprint", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.Write(s);
            });

            vm.Global.RegisterExternFunction("__builtin.eprintln", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.WriteLine(s);
            });

            vm.Global.RegisterExternFunction("__builtin.exit", Exit);
        }

        private static IEnumerable<RuntimeBreak> Exit(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            frame.Global.ExitCode = args[0].As<BasicRuntimeValue>().I32Value;
            yield return new RuntimeBreak(RuntimeBreakReason.Exited, frame);
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveTypeNode("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName() + ".copy", (result, args) =>
                {
                    var v = args[0];
                    if (v is BasicRuntimeValue)
                        result!.AssignFrom(v.Clone());
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var other_value = args[1].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var current_value = args[1].As<BasicRuntimeValue>().I64Value;
                var new_value = args[2].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var add_value = (Int64)args[1].As<BasicRuntimeValue>().I64Value;
                var res = Interlocked.Add(ref atomic.I64Value, add_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = res;
            });
        }

        public static void AddList(BabyPenguinVM vm)
        {
            foreach (var queue in vm.Model.ResolveTypeNode("__builtin.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queue.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Dequeue();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".peek", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Peek();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveTypeNode("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".push", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    l!.Add(args[1]);
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if (l!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.Last();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                        l.RemoveAt(l.Count - 1);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".at", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.ElementAt((int)idx);
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".remove", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        // remove out of range
                    }
                    else
                    {
                        l.RemoveAt((int)idx);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)l!.Count;
                });
            }
        }

        private static IEnumerable<RuntimeBreak> RoutineContextCall(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            var target = args[0].As<ReferenceRuntimeValue>().Fields["target"].As<FunctionRuntimeValue>() ?? throw new NotImplementedException();
            var frameRuntimeVar = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
            RuntimeFrameResult? frameResult = null;
            if (frameRuntimeVar.ExternImplenmentationValue is RuntimeFrame f)
            {
                foreach (var res in f.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }
            else
            {
                // target is a async_fun<T>
                var funcSymbol = target?.FunctionSymbol as FunctionSymbol ?? throw new BabyPenguinRuntimeException("cant find function symbol on " + target!.ToString());
                List<IRuntimeValue> funcArguments = [];
                if (target!.TypeInfo.GenericArguments.Count > 1 && target.Owner is not NotInitializedRuntimeValue)
                {
                    funcArguments.Add(target.Owner);
                }
                else if (target.TypeInfo.GenericArguments.Count == 1 && target.Owner is NotInitializedRuntimeValue)
                {
                    // no arguments
                }

                var newFrame = new RuntimeFrame(funcSymbol.CodeContainer, frame.Global, funcArguments, frame);
                frameRuntimeVar.ExternImplenmentationValue = newFrame;
                foreach (var res in newFrame.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }

            if (frameResult!.ReturnStatus == ReturnStatus.YieldFinished || frameResult!.ReturnStatus == ReturnStatus.Finished)
            {
                frame.ChildFrame = null;
                frameRuntimeVar.ExternImplenmentationValue = null;
            }

            var routineContextResult = args[0].As<ReferenceRuntimeValue>().Fields["result"].As<EnumRuntimeValue>();
            if (frameResult.ReturnValue != null)
            {
                routineContextResult.ContainingValue = frameResult.ReturnValue.Value;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
            }
            else
            {
                routineContextResult.ContainingValue = null;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
            }

            resultVar!.As<BasicRuntimeSymbol>().BasicValue.I64Value = (int)frameResult!.ReturnStatus;
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {

            foreach (var routineContext in vm.Model.ResolveTypeNode("__builtin.RoutineContext<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(routineContext.FullName() + ".call", RoutineContextCall);
            }
        }

        private static void AddFile(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.file_read_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = System.IO.File.ReadAllText(path);
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = content;
            });

            vm.Global.RegisterExternFunction("__builtin.file_write_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = args[1].As<BasicRuntimeValue>().StringValue;
                System.IO.File.WriteAllText(path, content);
            });
        }
        private static void AddArgs(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.__args_init", (result, args) =>
            {
                var commandLineArgs = vm.Global.CommandLineArgs;
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var list = self.ExternImplenmentationValue as List<IRuntimeValue>;
                var stringType = vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable);

                foreach (var arg in commandLineArgs)
                {
                    var basicStringValue = new BasicRuntimeValue(stringType);
                    basicStringValue.StringValue = arg;
                    list!.Add(basicStringValue);
                }
            });
        }

        private static void AddStringBuilder(BabyPenguinVM vm)
        {
            var stringBuilderType = vm.Model.ResolveTypeNode("__builtin.StringBuilder");
            if (stringBuilderType == null) return; // 如果代码中没用到，就跳过

            // 注册 StringBuilder.new()
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".new", (result, args) =>
            {
                // 1. 获取 'this' 实例
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                // 2. 创建一个 C# StringBuilder 并附加
                self.ExternImplenmentationValue = new System.Text.StringBuilder();
            });

            // 注册 StringBuilder.append(text: string)
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".append", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 获取要追加的文本
                var textToAppend = args[1].As<BasicRuntimeValue>().StringValue;

                // 3. 执行 append 操作
                sb!.Append(textToAppend);
            });

            // 注册 StringBuilder.to_string() -> string
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".to_string", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 将结果赋给返回值
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = sb!.ToString();
            });
        }
    }

    // 需要一个辅助类来比较 IRuntimeValue
    public class RuntimeValueComparer : IEqualityComparer<IRuntimeValue>
    {
        public bool Equals(IRuntimeValue? x, IRuntimeValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // 对于 BasicRuntimeValue，比较其内部值
            if (x is BasicRuntimeValue bx && y is BasicRuntimeValue by)
            {
                if (bx.TypeInfo.Type != by.TypeInfo.Type) return false;
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue == by.BoolValue,
                    TypeEnum.U8 => bx.U8Value == by.U8Value,
                    TypeEnum.U16 => bx.U16Value == by.U16Value,
                    TypeEnum.U32 => bx.U32Value == by.U32Value,
                    TypeEnum.U64 => bx.U64Value == by.U64Value,
                    TypeEnum.I8 => bx.I8Value == by.I8Value,
                    TypeEnum.I16 => bx.I16Value == by.I16Value,
                    TypeEnum.I32 => bx.I32Value == by.I32Value,
                    TypeEnum.I64 => bx.I64Value == by.I64Value,
                    TypeEnum.Float => bx.FloatValue == by.FloatValue,
                    TypeEnum.Double => bx.DoubleValue == by.DoubleValue,
                    TypeEnum.String => bx.StringValue == by.StringValue,
                    TypeEnum.Char => bx.CharValue == by.CharValue,
                    _ => false // 其他类型不应作为基本值进行比较
                };
            }
            // 对于 ReferenceRuntimeValue，比较其引用 ID
            else if (x is ReferenceRuntimeValue rx && y is ReferenceRuntimeValue ry)
            {
                return rx.RefId == ry.RefId;
            }
            // 对于 FunctionRuntimeValue，比较其函数符号
            else if (x is FunctionRuntimeValue fx && y is FunctionRuntimeValue fy)
            {
                return fx.FunctionSymbol.FullName() == fy.FunctionSymbol.FullName();
            }
            // 对于 EnumRuntimeValue，比较其值和类型
            else if (x is EnumRuntimeValue ex && y is EnumRuntimeValue ey)
            {
                return ex.TypeInfo.FullName() == ey.TypeInfo.FullName() &&
                       ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value == ey.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value &&
                       Equals(ex.ContainingValue, ey.ContainingValue);
            }

            return false;
        }

        public int GetHashCode(IRuntimeValue obj)
        {
            if (obj is null) return 0;

            if (obj is BasicRuntimeValue bx)
            {
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue.GetHashCode(),
                    TypeEnum.U8 => bx.U8Value.GetHashCode(),
                    TypeEnum.U16 => bx.U16Value.GetHashCode(),
                    TypeEnum.U32 => bx.U32Value.GetHashCode(),
                    TypeEnum.U64 => bx.U64Value.GetHashCode(),
                    TypeEnum.I8 => bx.I8Value.GetHashCode(),
                    TypeEnum.I16 => bx.I16Value.GetHashCode(),
                    TypeEnum.I32 => bx.I32Value.GetHashCode(),
                    TypeEnum.I64 => bx.I64Value.GetHashCode(),
                    TypeEnum.Float => bx.FloatValue.GetHashCode(),
                    TypeEnum.Double => bx.DoubleValue.GetHashCode(),
                    TypeEnum.String => bx.StringValue.GetHashCode(),
                    TypeEnum.Char => bx.CharValue.GetHashCode(),
                    _ => obj.GetHashCode() // Fallback
                };
            }
            else if (obj is ReferenceRuntimeValue rx)
            {
                return rx.RefId.GetHashCode();
            }
            else if (obj is FunctionRuntimeValue fx)
            {
                return fx.FunctionSymbol.FullName().GetHashCode();
            }
            else if (obj is EnumRuntimeValue ex)
            {
                return HashCode.Combine(ex.TypeInfo.FullName().GetHashCode(),
                                        ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value.GetHashCode(),
                                        ex.ContainingValue?.GetHashCode() ?? 0);
            }

            return obj.GetHashCode();
        }
    }
}
