using System.Runtime.InteropServices;

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
                var s = args[0].As<BasicRuntimeSymbol>().BasicValue.StringValue;
                vm.Global.Print(s);
            });

            vm.Global.RegisterExternFunction("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeSymbol>().BasicValue.StringValue;
                vm.Global.Print(s, true);
            });
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveType("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName + ".copy", (result, args) =>
                {
                    var v = args[0].Value;
                    if (v is BasicRuntimeValue)
                        result!.AssignFrom(v.Clone());
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["value"].As<BasicRuntimeValue>();
                var other_value = args[1].As<BasicRuntimeSymbol>().BasicValue.I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["value"].As<BasicRuntimeValue>();
                var current_value = args[1].As<BasicRuntimeSymbol>().BasicValue.I64Value;
                var new_value = args[2].As<BasicRuntimeSymbol>().BasicValue.I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["value"].As<BasicRuntimeValue>();
                var add_value = (Int64)args[1].As<BasicRuntimeSymbol>().BasicValue.I64Value;
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
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    q!.Enqueue(args[1].Value);
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
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
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
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
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveType("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    impl.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(list.FullName + ".push", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    l!.Add(args[1].Value);
                });

                vm.Global.RegisterExternFunction(list.FullName + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
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
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeSymbol>().BasicValue.U64Value;
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
                    var impl = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["__impl"].As<BasicRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)l!.Count;
                });
            }
        }

        private static IEnumerable<RuntimeBreak> RoutineContextCall(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeSymbol> args)
        {
            var target = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["target"].As<FunctionRuntimeValue>();
            var frameRuntimeVar = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["frame"].As<BasicRuntimeValue>();
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
                var codeContainer = (target.FunctionSymbol as FunctionSymbol)?.CodeContainer ?? throw new BabyPenguinRuntimeException($"calling non-function symbol {target}");
                var newFrame = new RuntimeFrame(codeContainer, frame.Global, [], frame);
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
            resultVar!.As<BasicRuntimeSymbol>().BasicValue.I64Value = (int)frameResult!.ReturnStatus;
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.RoutineContext.call", RoutineContextCall);

            vm.Global.RegisterExternFunction("__builtin.RoutineContext.new", (result, args) =>
            {
                var targetName = args[1].As<BasicRuntimeSymbol>().BasicValue.StringValue;
                var targetSymbol = vm.Model.ResolveSymbol(targetName);

                if (targetSymbol is FunctionSymbol functionSymbol)
                {
                    var target = args[0].As<ClassRuntimeSymbol>().ReferenceValue.Fields["target"].As<FunctionRuntimeValue>();
                    target.FunctionSymbol = functionSymbol;
                }
                else
                {
                    throw new BabyPenguinRuntimeException($"Cannot build context on non-function symbol {targetName}");
                }
            });
        }
    }
}