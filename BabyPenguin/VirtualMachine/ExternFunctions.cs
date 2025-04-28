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
            vm.Global.ExternFunctions.Add("__builtin.print", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeVar>().StringValue;
                vm.Output.Append(s);
                Console.Write(s);
            });

            vm.Global.ExternFunctions.Add("__builtin.println", (result, args) =>
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
                vm.Global.ExternFunctions.Add(ICopy.FullName + ".copy", (result, args) =>
                {
                    var clone = args[0].Clone();
                    result!.AssignFrom(clone);
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.ExternFunctions.Add("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeVar>().ObjectFields["value"].As<BasicRuntimeVar>();
                var other_value = args[1].As<BasicRuntimeVar>().I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeVar>().I64Value = org;
            });

            vm.Global.ExternFunctions.Add("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ClassRuntimeVar>().ObjectFields["value"].As<BasicRuntimeVar>();
                var current_value = args[1].As<BasicRuntimeVar>().I64Value;
                var new_value = args[2].As<BasicRuntimeVar>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeVar>().I64Value = org;
            });

            vm.Global.ExternFunctions.Add("__builtin.AtomicI64.fetch_add", (result, args) =>
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
                vm.Global.ExternFunctions.Add(queue.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeVar>();
                });

                vm.Global.ExternFunctions.Add(queue.FullName + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.ExternFunctions.Add(queue.FullName + ".dequeue", (result, args) =>
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

                vm.Global.ExternFunctions.Add(queue.FullName + ".peek", (result, args) =>
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

                vm.Global.ExternFunctions.Add(queue.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeVar>;
                    result!.As<BasicRuntimeVar>().U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveType("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.ExternFunctions.Add(list.FullName + ".new", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    impl.ExternImplenmentationValue = new List<IRuntimeVar>();
                });

                vm.Global.ExternFunctions.Add(list.FullName + ".push", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    l!.Add(args[1]);
                });

                vm.Global.ExternFunctions.Add(list.FullName + ".pop", (result, args) =>
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

                vm.Global.ExternFunctions.Add(list.FullName + ".at", (result, args) =>
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

                vm.Global.ExternFunctions.Add(list.FullName + ".size", (result, args) =>
                {
                    var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeVar>;
                    result!.As<BasicRuntimeVar>().U64Value = (ulong)l!.Count;
                });
            }
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {
            vm.Global.ExternFunctions.Add("__builtin.RoutineContext.call", (result, args) =>
            {
                var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                var frame = impl.ExternImplenmentationValue as RuntimeFrame;
                frame!.Run();
            });

            vm.Global.ExternFunctions.Add("__builtin.RoutineContext.set_test_context", (result, args) =>
            {
                var container = new SemanticNode.InitialRoutine(vm.Model, "test_context");
                var func = vm.Model.ResolveSymbol("__builtin.hello_world");
                container.Instructions.Add(new FunctionCallInstruction(func!, [], null));
                var frame = new RuntimeFrame(container, vm.Global, []);
                var impl = args[0].As<ClassRuntimeVar>().ObjectFields["__impl"].As<BasicRuntimeVar>();
                impl.ExternImplenmentationValue = frame;
            });
        }
    }
}