using System.Runtime.InteropServices;
using Mono.Cecil;

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
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveType("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName + ".copy", (result, args) =>
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
            foreach (var queue in vm.Model.ResolveType("__builtin.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queue.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
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

                vm.Global.RegisterExternFunction(queue.FullName + ".peek", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
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

                vm.Global.RegisterExternFunction(queue.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveType("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    impl.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(list.FullName + ".push", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    l!.Add(args[1]);
                });

                vm.Global.RegisterExternFunction(list.FullName + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
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

                vm.Global.RegisterExternFunction(list.FullName + ".at", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
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

                vm.Global.RegisterExternFunction(list.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<BasicRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)l!.Count;
                });
            }
        }

        private static IEnumerable<RuntimeBreak> RoutineContextCall(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            var target = args[0].As<ReferenceRuntimeValue>().Fields["target"].As<FunctionRuntimeValue>() ?? throw new NotImplementedException();
            var frameRuntimeVar = args[0].As<ReferenceRuntimeValue>().Fields["frame"].As<BasicRuntimeValue>();
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

            foreach (var routineContext in vm.Model.ResolveType("__builtin.RoutineContext<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(routineContext.FullName + ".call", RoutineContextCall);
            }
        }
    }
}