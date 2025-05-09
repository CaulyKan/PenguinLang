namespace BabyPenguin.Tests
{
    public class BuiltinTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void VoidTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val v: void = void;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"", vm.CollectOutput());
        }

        [Fact]
        public void PrintTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello, "");
                    println(""world!"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"hello, world!{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void OptionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : Option<u32> = new Option<u32>.some(10);
                    println(a.is_some() as string);
                    println(a.is_none() as string);
                    println(a.value_or(9) as string);

                    val b : Option<u32> = new Option<u32>.none();
                    println(b.is_some() as string);
                    println(b.is_none() as string);
                    println(b.value_or(9) as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"true{EOL}false{EOL}10{EOL}false{EOL}true{EOL}9{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void RangeTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val rg : IIterator<i64> = range(0, 5);
                    while(true) {
                        val n : Option<i64> = rg.next();
                        if (n.is_none())
                            break;
                        else 
                            print(n.some as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"01234", vm.CollectOutput());
        }

        [Fact]
        public void RangeTest2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val rg : i64[] = range(0, 5);
                    while(true) {
                        val n : Option<i64> = rg.next();
                        if (n.is_none())
                            break;
                        else 
                            print(n.some as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"01234", vm.CollectOutput());
        }

        [Fact]
        public void CopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Foo {
                    var x : i64;
                    var y : i64;

                    impl ICopy<Self>;
                }
                initial {
                    var a : Foo = new Foo();
                    a.x = 1;
                    a.y = 2;
                    var b : Foo = a.copy();
                    b.x = 3;
                    b.y = 4;
                    print(a.x as string);
                    print(a.y as string);
                    print(b.x as string);
                    print(b.y as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"1234", vm.CollectOutput());
        }

        [Fact]
        public void BasicTypeCopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : u8 = 1;
                    var b : u8 = a.copy();
                    b = 2;
                    print(a as string);
                    print(b as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"12", vm.CollectOutput());
        }

        [Fact]
        public void ResultTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : Result<u32,string> = new Result<u32,string>.ok(10);
                    println(a.is_ok() as string);
                    println(a.is_error() as string);
                    println(a.value_or(9) as string);

                    val b : Result<u32,string> = new Result<u32,string>.error(""err"");
                    println(b.error);
                    println(b.is_ok() as string);
                    println(b.is_error() as string);
                    println(b.value_or(9) as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"true{EOL}false{EOL}10{EOL}err{EOL}false{EOL}true{EOL}9{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void AtomicTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : AtomicI64 = new AtomicI64(1);
                    println(a.load() as string);
                    a.store(2);
                    println(a.load() as string);
                    val res1: i64 = a.compare_exchange(2, 3);
                    println(res1 as string);
                    println(a.load() as string);
                    val res2: i64 = a.compare_exchange(8888, 4);
                    println(res2 as string);
                    println(a.load() as string);
                    val res3 : i64 = a.fetch_add(1);
                    println(res3 as string);
                    val res4 : i64 = a.swap(5);
                    println(res4 as string);
                    println(a.load() as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"1{EOL}2{EOL}2{EOL}3{EOL}3{EOL}3{EOL}4{EOL}4{EOL}5{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void ListTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : List<i64> = new List<i64>();
                    a.push(1);
                    a.push(2);
                    a.push(3);
                    println(a.size() as string);
                    val res1 : Option<i64> = a.at(0);
                    println(res1.some as string);
                    val res2 : Option<i64> = a.at(2);
                    println(res2.some as string);
                    a.pop();
                    println(a.size() as string);
                    val res3 : Option<i64> = a.at(1);
                    println(res3.some as string);
                    val res4 : Option<i64> = a.at(2);
                    println(res4.is_none() as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"3{EOL}1{EOL}3{EOL}2{EOL}2{EOL}true{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void ListForEachTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : List<i64> = new List<i64>();
                    a.push(1);
                    a.push(2);
                    a.push(3);
                    for (val x : i64 in a.iter()) {
                        print(x as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"123", vm.CollectOutput());
        }

        [Fact]
        public void QueueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : Queue<i64> = new Queue<i64>();
                    a.enqueue(1);
                    a.enqueue(2);
                    println(a.size() as string);
                    val res1 : Option<i64> = a.peek();
                    println(res1.some as string);
                    a.enqueue(3);
                    val res2 : Option<i64> = a.peek();
                    println(res2.some as string);
                    a.dequeue();
                    println(a.size() as string);
                    val res3 : Option<i64> = a.peek();
                    println(res3.some as string);
                    a.dequeue();
                    val res4 : Option<i64> = a.dequeue();
                    println(res4.some as string);
                    val res5 : Option<i64> = a.peek();
                    println(res5.is_none() as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"2{EOL}1{EOL}1{EOL}2{EOL}2{EOL}3{EOL}true{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void TestDefaultRoutine()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var routine : _DefaultRoutine<void> = new _DefaultRoutine<void>(""__builtin.hello_world"", false);
                    println(routine.start() as string);
                    println(routine.routine_state() as string);
                    
                    val state1 : FutureState<void>  = routine.poll();
                    println(state1 as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"hello world!{EOL}true{EOL}finished{EOL}finished{EOL}", vm.CollectOutput());
        }
    }
}