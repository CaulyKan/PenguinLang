using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using Xunit.Abstractions;

namespace BabyPenguin.Tests
{
    public class FlowControlTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void IfTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    if (true) {
                        print(""a"");
                    }
                    if (1 == (1-1)) {
                        print(""b"");
                    }
                    if (1==1) print(""c"");
                } 
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("ac", vm.CollectOutput());
        }

        [Fact]
        public void IfElseTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    if (false) {
                        print(""a"");
                    }
                    else if (1 == (1-1)) {
                        print(""b"");
                    }   
                    else {
                        print(""c"");
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("c", vm.CollectOutput());
        }

        [Fact]
        public void CascadeIfTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    if (false) {
                        print(""a"");
                    }
                    else if (1 == (1-1)) {
                        print(""b"");
                    }   
                    else {
                        if (true) if (false) print(""e""); else print(""f"");
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("f", vm.CollectOutput());
        }


        [Fact]
        public void WhileTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var i : u8 = 0;
                    while (i < 3) {
                        print(i as string);
                        i += 1;
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void CascadeWhileTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var i : u8 = 0;
                    var j : u8 = 0;
                    while (i < 2) 
                        while (i < 2) {
                            j = 0;
                            while (j < 2) {
                                print(i as string);
                                j += 1;
                            }
                            i += 1;
                        }
                } 
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Global.EnableDebugPrint = true;
            vm.Global.DebugWriter = this;
            vm.Run();
            Assert.Equal("0011", vm.CollectOutput());
        }
    }
}
