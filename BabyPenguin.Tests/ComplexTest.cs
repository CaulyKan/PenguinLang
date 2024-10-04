using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using Xunit.Abstractions;

namespace BabyPenguin.Tests
{
    public class ComplexTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void HelloWorldTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddFile("TestFiles/HelloWorld.penguin");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("Hello, World!\n", vm.CollectOutput());
        }

        //[Fact]
        public void LinkedListTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddFile("TestFiles/LinkedList.penguin");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("1, 2, 3\n", vm.CollectOutput());
        }
    }
}
