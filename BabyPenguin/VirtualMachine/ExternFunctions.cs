namespace BabyPenguin.VirtualMachine
{
    class ExternFunctions
    {
        public static void Build(BabyPenguinVM vm)
        {
            AddPrint(vm);
            AddCopy(vm);
        }

        public static void AddPrint(BabyPenguinVM vm)
        {
            vm.Global.ExternFunctions.Add("__builtin.print", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeVar>().Value;
                vm.Output.Append(s);
                Console.Write(s);
            });

            vm.Global.ExternFunctions.Add("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeVar>().Value;
                vm.Output.AppendLine(s as string);
                Console.WriteLine(s);
            });
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var icopiable in vm.Model.ResolveType("__builtin.ICopiable<?>")!.GenericInstances)
            {
                vm.Global.ExternFunctions.Add(icopiable.FullName + ".copy", (result, args) =>
                {
                    var clone = args[0].Clone();
                    result!.AssignFrom(clone);
                });
            }
        }
    }
}