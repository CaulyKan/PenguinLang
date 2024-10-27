namespace BabyPenguin
{
    public class Builtin
    {
        public static void Build(SemanticModel model)
        {
            AddPrint(model);
            AddOption(model);
            AddIterators(model);
        }

        public static void AddPrint(SemanticModel model)
        {
            var ns = new Namespace(model, "__builtin");
            var println = new Function(model, "println",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);
            var print = new Function(model, "print",
                [new FunctionParameter("text", BasicType.String, true, 0)], BasicType.Void, true);

            (ns as IRoutineContainer).AddFunction(println);
            (ns as IRoutineContainer).AddFunction(print);

            model.AddNamespace(ns);
        }

        public static void AddOption(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    enum Option<T> {
                        some: T;
                        none;
                    
                        fun is_some(val this: Option<T>) -> bool {
                            return this is Option<T>.some;
                        }

                        fun is_none(val this: Option<T>) -> bool {
                            return this is Option<T>.none;
                        }
                    
                        fun value_or(val this: Option<T>, val default_val: T) -> T {
                            if (this is Option<T>.some) {
                                return this.some;
                            } else {
                                return default_val;
                            }
                        }
                    }
                }
            ";

            model.AddSource(source, "__builtin");
        }

        public static void AddIterators(SemanticModel model)
        {
            var source = @"
                interface IIterator<T> {
                    fun next(var this: IIterator<T>) -> Option<T>;
                }
            ";
        }
    }
}