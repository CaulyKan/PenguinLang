namespace BabyPenguin.VirtualMachine
{
    class ExternFunctions
    {
        public static void Build(BabyPenguinVM vm)
        {
            AddPrint(vm);
            AddCopy(vm);
            AddAtmoic(vm);
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
    }
}