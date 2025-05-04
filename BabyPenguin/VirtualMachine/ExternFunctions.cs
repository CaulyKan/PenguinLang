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
                var s = args[0].As<BasicRuntimeVar>().StringValue;
                vm.Output.Append(s);
                Console.Write(s);
            });

            vm.Global.RegisterExternFunction("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeVar>().StringValue;
                vm.Output.AppendLine(s as string);
                Console.WriteLine(s);
            });
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveType("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName + ".copy", (result, args) =>
                {
                    var clone = args[0].Clone();
                    result!.AssignFrom(clone);
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeVar>().ObjectFields["value"].As<BasicRuntimeVar>();
                var other_value = args[1].As<BasicRuntimeVar>().I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeVar>().I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeVar>().ObjectFields["value"].As<BasicRuntimeVar>();
                var current_value = args[1].As<BasicRuntimeVar>().I64Value;
                var new_value = args[2].As<BasicRuntimeVar>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeVar>().I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeVar>().ObjectFields["value"].As<BasicRuntimeVar>();
                var add_value = (Int64)args[1].As<BasicRuntimeVar>().I64Value;
                var res = Interlocked.Add(ref atomic.I64Value, add_value);
                result!.As<BasicRuntimeVar>().I64Value = res;
            });
        }

        public static void AddList(BabyPenguinVM vm)
        {
            foreach (var queue in vm.Model.ResolveType("__builtin.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queue.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeVar>();
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = null;
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = q.Dequeue();
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".peek", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = null;
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = q.Peek();
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    result!.As<BasicRuntimeVar>().U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveType("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    impl.ExternImplenmentationValue = new List<IRuntimeVar>();
                });

                vm.Global.RegisterExternFunction(list.FullName + ".push", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    l!.Add(args[1]);
                });

                vm.Global.RegisterExternFunction(list.FullName + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    if (l!.Count == 0)
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = null;
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = l.Last();
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 0;
                        l.RemoveAt(l.Count - 1);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName + ".at", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var idx = args[1].As<BasicRuntimeVar>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    if ((ulong)l!.Count <= idx)
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = null;
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeVar>().EnumObject = l.ElementAt((int)idx);
                        result.As<EnumRuntimeVar>().ObjectFields["_value"].As<BasicRuntimeVar>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    result!.As<BasicRuntimeVar>().U64Value = (ulong)l!.Count;
                });
            }
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.RoutineContext.call", (frame, result, args) =>
            {
                var target = args[0].As<ClassRuntimeVar>().ObjectFields["target"].As<FunctionRuntimeVar>();
                var oldFrame = args[0].As<ClassRuntimeVar>().ObjectFields["frame"].As<BasicRuntimeVar>();
                RuntimeFrameResult frameResult;
                if (oldFrame.ExternImplenmentationValue is RuntimeFrame f)
                {
                    frameResult = f.Run();
                }
                else
                {
                    var codeContainer = (target.FunctionSymbol as FunctionSymbol)?.CodeContainer ?? throw new BabyPenguinRuntimeException($"calling non-function symbol {target}");
                    var newFrame = new RuntimeFrame(codeContainer, frame.Global, [], frame.FrameLevel + 1);
                    frameResult = newFrame!.Run();
                    oldFrame.ExternImplenmentationValue = newFrame;
                }
                result!.As<BasicRuntimeVar>().I64Value = (int)frameResult.ReturnStatus; // TODO: really finished?
            });

            vm.Global.RegisterExternFunction("__builtin.RoutineContext.new", (result, args) =>
            {
                var targetName = args[1].As<BasicRuntimeVar>().StringValue;
                var targetSymbol = vm.Model.ResolveSymbol(targetName);

                if (targetSymbol is FunctionSymbol functionSymbol)
                {
                    var target = args[0].As<ClassRuntimeVar>().ObjectFields["target"].As<FunctionRuntimeVar>();
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