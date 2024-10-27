namespace BabyPenguin
{
    public partial class SemanticModel
    {
        private Namespace AddBuiltin()
        {
            var ns = new Namespace(this, "__builtin");

            AddPrint(ns);

            return ns;
        }

        private void AddPrint(Namespace ns)
        {
            var println = new Function(this, "println",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);
            var print = new Function(this, "print",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);

            (ns as IRoutineContainer).AddFunction(println);
            (ns as IRoutineContainer).AddFunction(print);
        }
    }
}