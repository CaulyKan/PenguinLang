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
        public void YieldTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello"");
                    yield;
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
    }
}