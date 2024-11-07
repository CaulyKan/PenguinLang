namespace BabyPenguin
{
    public class Builtin
    {
        public static void Build(SemanticModel model)
        {
            AddPrint(model);
            AddOption(model);
            AddIterators(model);
            AddCopy(model);
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

                        impl ICopiable<Option<T>> where T: ICopiable<T>;
                    }
                }
            ";

            model.AddSource(source, "__builtin");
        }

        public static void AddCopy(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    interface ICopiable<T> {
                        extern fun copy(val this: T) -> T;
                    }
                }
            ";

            model.AddSource(source, "__builtin");
        }

        public static void AddIterators(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    interface IIterator<T> {
                        fun next(var this: IIterator<T>) -> Option<T>;
                    }

                    interface IIterable<T> {
                        fun iter(var this: IIterable<T>) -> IIterator<T>;
                    }

                    class RangeIterator {
                        val start: i64;
                        val end: i64;
                        var current: i64;

                        fun new(var this: RangeIterator, val start: i64, val end: i64) {
                            this.start = start;
                            this.end = end;
                            this.current = start;
                        }

                        impl IIterator<i64> {
                            fun next(var this: IIterator<i64>) -> Option<i64> {
                                var self : RangeIterator = this as RangeIterator;
                                if (self.current < self.end) {
                                    val res : Option<i64> =new Option<i64>.some(self.current);
                                    self.current += 1;
                                    return res;
                                } else {
                                    return new Option<i64>.none();
                                }
                            }
                        }
                    }

                    fun range(val start: i64, val end: i64) -> IIterator<i64> {
                        return new RangeIterator(start, end) as IIterator<i64>;
                    }
                }
            ";

            model.AddSource(source, "__builtin");
        }
    }
}