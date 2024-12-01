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
            AddResult(model);
            AddAtomic(model);
            AddList(model);
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

                        impl __builtin.ICopy<Option<T>> where T: ICopy<T>;
                    }
                }
            ";

            model.AddSource(source, "__builtin");
        }

        public static void AddCopy(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    interface ICopy<T> {
                        extern fun copy(val this: T) -> T;
                    }
                        
                    impl __builtin.ICopy<i64> for i64;
                    impl __builtin.ICopy<u64> for u64;
                    impl __builtin.ICopy<i32> for i32;
                    impl __builtin.ICopy<u32> for u32;
                    impl __builtin.ICopy<i16> for i16;
                    impl __builtin.ICopy<u16> for u16;
                    impl __builtin.ICopy<i8> for i8;
                    impl __builtin.ICopy<u8> for u8;
                    impl __builtin.ICopy<bool> for bool;
                    impl __builtin.ICopy<char> for char;
                    impl __builtin.ICopy<string> for string;
                    impl __builtin.ICopy<float> for float;
                    impl __builtin.ICopy<double> for double;
                }
            ";

            model.AddSource(source, "__builtin");
        }

        public static void AddResult(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    enum Result<T, E> {
                        ok: T;
                        error: E;
                    
                        fun is_ok(val this: Result<T, E>) -> bool {
                            return this is Result<T, E>.ok;
                        }

                        fun is_error(val this: Result<T, E>) -> bool {
                            return this is Result<T, E>.error;
                        }
                    
                        fun value_or(val this: Result<T, E>, val default_val: T) -> T {
                            if (this is Result<T, E>.ok) {
                                return this.ok;
                            } else {
                                return default_val;
                            }
                        }

                        impl __builtin.ICopy<Result<T, E>> where T: ICopy<T>, E: ICopy<E>;
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

        public static void AddAtomic(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    class AtomicI64 {
                        var value: i64;
                        fun new(var this: AtomicI64, val value: i64) {
                            this.value = value;
                        }
                        fun load(val this: AtomicI64) -> i64 {
                            return this.value;
                        }
                        fun store(var this: AtomicI64, val value: i64) {
                            this.value = value;
                        }
                        extern fun swap(var this: AtomicI64, val value: i64) -> i64;
                        extern fun compare_exchange(var this: AtomicI64, var current_val: i64, val new_val: i64) -> i64;
                        extern fun fetch_add(var this: AtomicI64, val value: i64) -> i64;
                    }
                }";

            model.AddSource(source, "__builtin");
        }

        public static void AddList(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    class List<T> {
                        var __impl: u64;
                        extern fun new(var this: List<T>);
                        extern fun at(val this: List<T>, val index: u64) -> Option<T>;
                        extern fun push(var this: List<T>, val value: T);
                        extern fun pop(var this: List<T>) -> Option<T>;
                        extern fun size(val this: List<T>) -> u64;
                    }

                    class Queue<T> {
                        var __impl: u64;
                        extern fun new(var this: Queue<T>);
                        extern fun enqueue(var this: Queue<T>, val value: T);
                        extern fun dequeue(var this: Queue<T>) -> Option<T>;
                        extern fun size(val this: Queue<T>) -> u64;
                    }
                }";

            model.AddSource(source, "__builtin");
        }

        public static void AddFuture(SemanticModel model)
        {
            var source = @"
                namespace __builtin {
                    enum FutureState {
                        pending,
                        ready,
                        finished
                    }

                    enum FutureResult<T, E> {
                        not_ready,
                        ok_finished: T,
                        ok_not_finished: T,
                        error: E
                    }

                    interface IFutureBase {
                        fun state(val this: IFutureBase) -> FutureState;
                    }

                    interface IFuture<T, E> : IFutureBase {
                        fun poll(val this: IFuture<T, E>) -> FutureResult<T, E>;
                        fun set_result(var this: IFuture<T, E>, val result: Result<T, E>, val is_finished: bool);
                    }

                    class Routine<T, E> {
                        var state: AtomicI64;
                        var result: IFuture<T, E>;

                    }
                }
            ";

            // model.AddSource(source, "__builtin");
        }
    }
}