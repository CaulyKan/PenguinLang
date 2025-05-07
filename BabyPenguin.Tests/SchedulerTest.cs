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
        public void AsyncFunctionIdentifyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns{
                    fun test1() {
                        yield;
                    }
                    fun test2() {
                        test1();
                    }
                    fun test3() {
                    }
                    fun test4() {
                        test3();
                    }
                    fun test5() {
                        wait test3();
                    }
                }
            ");
            var model = compiler.Compile();
            Assert.True((model.ResolveSymbol("ns.test1") as FunctionSymbol)?.IsAsync);
            Assert.True((model.ResolveSymbol("ns.test2") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test3") as FunctionSymbol)?.IsAsync);
            Assert.False((model.ResolveSymbol("ns.test4") as FunctionSymbol)?.IsAsync);
            Assert.True((model.ResolveSymbol("ns.test5") as FunctionSymbol)?.IsAsync);
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
                    yield;
                    print(""2"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void WaitAnyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    wait_any test();
                    print(""3"");
                } 
                fun test() {
                    print(""1"");
                    yield;
                    print(""2"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("13", vm.CollectOutput());
        }
    }
}