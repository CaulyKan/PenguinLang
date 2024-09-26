using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;
using PenguinLangAntlr;

namespace BabyPenguin.Tests
{
    public class CalculationTest
    {
        [Fact]
        public void AdditionTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = 1 + 2 - 4 * 3 / 2;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("-3", vm.CollectOutput());
        }

        [Fact]
        public void ParenthesizedTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = (1 + (2 - 4) * 3) / 5;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("-1", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest1()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : bool = true && false;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : bool = true && false || true;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("true", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest3()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : bool = true && false || true && (1>2);
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest4()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : bool = (1==1) && (4>=5) || (1<=1) && (1>2) || 1!=2 && 1<2;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("true", vm.CollectOutput());
        }

        [Fact]
        public void BitwiseOperationTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = 30 & 15 | 10 ^ 5;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal((30 & 15 | 10 ^ 5).ToString(), vm.CollectOutput());
        }

        [Fact]
        public void AssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = 1;
                    a += 2;
                    a -= 1;
                    a *= 3;
                    a /= 2;
                    var b : string = a as string;
                    b = b;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void ShiftTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = 1 << 2 >> 1;
                    a <<= 2;
                    a >>= 1;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("4", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest1()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : bool = !(1==1);
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : i16 = -(-1);
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest3()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = ~1;
                    var b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal((~1).ToString(), vm.CollectOutput());
        }
    }
}
