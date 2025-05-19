namespace BabyPenguin.Tests
{
    public class SchedulerTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void MultiInitialRoutinesTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello "");
                } 
                initial {
                    print(""world"");
                } 
                initial {
                    print(""!"");
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello world!", vm.CollectOutput());
        }

        [Fact]
        public void ReturnTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello"");
                    return;
                    print(""world"");
                } 
                initial {
                    print("" "");
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello ", vm.CollectOutput());
        }

        [Fact]
        public void WaitTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello"");
                    wait;
                    print(""world"");
                } 
                initial {
                    print("" "");
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello world", vm.CollectOutput());
        }

        [Fact]
        public void GeneratorFunctionIdentifyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns{
                    fun test1() -> IGenerator<void> {
                        yield;
                    }
                    fun test2() -> IGenerator<i32> {
                        yield 1;
                    }
                    fun test3() -> i64[] {
                        return range(1,1);
                    }
                }
            ");
            var model = compiler.Compile();
            Assert.True((model.ResolveSymbol("ns.test1") as FunctionSymbol)?.IsGenerator);
            Assert.False((model.ResolveSymbol("ns.test1") as FunctionSymbol)?.IsAsync);
            Assert.True((model.ResolveSymbol("ns.test2") as FunctionSymbol)?.IsGenerator);
            Assert.False((model.ResolveSymbol("ns.test2") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test3") as FunctionSymbol)?.IsGenerator);
            Assert.False((model.ResolveSymbol("ns.test3") as FunctionSymbol)?.IsAsync);
        }

        [Fact]
        public void AsyncFunctionIdentifyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns{
                    fun test1() {
                    }
                    fun test2() {
                        test1();
                    }
                    fun test3() {
                        wait;
                    }
                    fun test4() {
                        test3();
                    }
                }
            ");
            var model = compiler.Compile();
            Assert.False((model.ResolveSymbol("ns.test1") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test1") as FunctionSymbol)?.IsGenerator);
            Assert.False((model.ResolveSymbol("ns.test2") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test2") as FunctionSymbol)?.IsGenerator);
            Assert.True((model.ResolveSymbol("ns.test3") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test3") as FunctionSymbol)?.IsGenerator);
            Assert.True((model.ResolveSymbol("ns.test4") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test4") as FunctionSymbol)?.IsGenerator);
        }

        [Fact]
        public void ImplicitWaitTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    test();
                    print(""3"");
                } 
                fun test() {
                    print(""1"");
                    wait;
                    print(""2"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void WaitAllTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    wait test();
                    print(""3"");
                } 
                fun test() {
                    print(""1"");
                    wait;
                    print(""2"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void WaitWithResultTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : i32 = wait test();
                    print(a as string);
                } 
                fun test() -> i32{
                    print(""1"");
                    wait;
                    print(""2"");
                    return 3;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void ImplicitWaitWithResultTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : i32 = test();
                    print(a as string);
                } 
                fun test() -> i32{
                    print(""1"");
                    wait;
                    print(""2"");
                    return 3;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void YieldBeforeRewriteTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns{
                    initial {
                        val v : i32[] = test1();
                        for (var i : i32 in v) {
                            print(i as string);
                        }
                    } 
                    class _lambda {
                        fun call(var this: Self) -> i32 {
                            __yield_not_finished_return 1;
                            __yield_not_finished_return 2;
                        }
                    }
                    fun test1() -> i32[] {
                        var owner: _lambda = new _lambda();
                        return new _DefaultRoutine<i32>(owner.call, true) as i32[];
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void YieldIterateTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val v : IGenerator<i64> = test();
                    for (var i : i64 in v) {
                        print(i as string);
                    }
                } 
                fun test() -> IGenerator<i64> {
                    yield 1;
                    yield 2;
                    for (var i : i64 in range(0, 3)) 
                        yield i + 3;
                    yield 6;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123456", vm.CollectOutput());
        }

        [Fact]
        public void YieldBreakTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val v : IGenerator<i64> = test();
                    for (var i : i64 in v) {
                        print(i as string);
                    }
                } 
                fun test() -> IGenerator<i64> {
                    yield 1;
                    yield 2;
                    return;
                    yield 6;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void YieldReturnValueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val v : IGenerator<i64> = test();
                    for (var i : i64 in v) {
                        print(i as string);
                    }
                } 
                fun test() -> IGenerator<i64> {
                    yield 1;
                    yield 2;
                    return 3;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void YieldReturnErrorTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val v : IGenerator<i32> = test();
                    for (var i : i32 in v) {
                        print(i as string);
                    }
                } 
                fun test() -> IGenerator<i32> {
                    yield 1;
                    yield 2;
                    return range(3);  
                    yield 3;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void YieldVoidTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                   for (var i : void in test()) {} 
                } 
                fun test() -> IGenerator<void> {
                    print(""1"");
                    yield;
                    print(""2"");
                    yield;
                    print(""3"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void YieldNextTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : Option<i32> = (test()).next();
                    print(a.some as string);
                } 
                fun test() -> IGenerator<i32> {
                    yield 1;
                    yield 2;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void YieldError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    test();
                    print(""3"");
                } 
                fun test() -> i32 {
                    print(""1"");
                    yield 1;
                    print(""2"");
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void YieldWaitTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (var i : i32 in test()) {
                        print(i as string);
                    } 
                } 
                fun test() -> IGenerator<i32> {
                    yield 1;
                    wait;
                    yield 2;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void AsyncTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    async test();
                    print(""1"");
                } 
                fun test() {
                    print(""2"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void AsyncPollTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : IFuture<i32> = async test();
                    var poll1 : FutureState<i32> = a.poll();
                    println(poll1 as string);
                    wait;
                    var poll2 : FutureState<i32> = a.poll();
                    println(poll2 as string);
                } 
                fun test() -> i32 {
                    return 1;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"not_ready{EOL}ready_finished(1){EOL}", vm.CollectOutput());
        }

        [Fact]
        public void AsyncWaitTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var a : IFuture<i32> = async test();
                    println(""before"");
                    wait a;
                    println(""after"");
                } 
                fun test() -> i32 {
                    wait;
                    println(""test"");
                    return 1;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"before{EOL}test{EOL}after{EOL}", vm.CollectOutput());
        }

    }
}