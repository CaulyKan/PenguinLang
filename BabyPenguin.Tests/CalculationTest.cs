using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using Xunit.Abstractions;

namespace BabyPenguin.Tests
{
    public class CalculationTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void AdditionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
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

        [Fact]
        public void ShadowTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var a : u8 = 1;
                    {
                        var a : u8 = 2;
                        print(a as string);
                    }
                    print(a as string);
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("21", vm.CollectOutput());
        }

        [Fact]
        public void ShadowTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                var a : u8 = 1;
                initial {
                    var a : u8 = 2;
                    print(a as string);
                }

                initial {
                    print(a as string);
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("21", vm.CollectOutput());
        }

        [Fact]
        public void ClassMemberTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var test : Test = new Test();
                    test.a = 1;
                    test.b = 1;
                    test.b += 1;
                    print(test.a as string);
                    print(test.b as string);
                    print((test.a + test.b) as string);
                }

                class Test {
                    var a : u8;
                    var b : u8;
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }


        [Fact]
        public void ClassMemberCascadeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var test : Test2 = new Test2();
                    test.test1 = new Test1();
                    test.test1.a = 1;
                    test.test1.b = 1;
                    test.test1.b += 1;
                    print(test.test1.a as string);
                    print(test.test1.b as string);
                    print((test.test1.a + test.test1.b) as string);
                }

                class Test1 {
                    var a : u8;
                    var b : u8;
                }

                class Test2 {
                    var test1: Test1;
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        var test : Test = new Test();
                        test.a = 1;
                        test.b = 1;
                        test.print_sum();
                    }

                    class Test {
                        var a : u8;
                        var b : u8;

                        fun print_sum(val this: Test) {
                            print((this.a + this.b) as string);
                        }
                    }
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodCascadeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var test : Test2 = new Test2();
                    test.test1 = new Test1();
                    test.test1.a = 1;
                    test.test1.b = 1;
                    test.test1.print_sum();
                }

                class Test1 {
                    var a : u8;
                    var b : u8;
                    fun print_sum(val this: Test1) {
                        print((this.a + this.b) as string);
                    }
                }

                class Test2 {
                    var test1: Test1;
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodWithReturnedOwnerTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    (foo()).print_sum();
                }

                class Test {
                    var a : u8;
                    var b : u8;
                    fun print_sum(val this: Test) {
                        print((this.a + this.b) as string);
                    }
                }

                fun foo() -> Test {
                    var test : Test = new Test();
                    test.a = 1;
                    test.b = 1;
                    return test;
                }
            ");
            var model = compiler.Compile();
            var test = model.Reporter.GenerateReport();
            var vm = new VirtualMachine(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodWrongOwnerTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    var test : Test2 = new Test2();
                    test.print_sum();
                }

                class Test1 {
                    var a : u8;
                    var b : u8;
                    fun print_sum(val this: Test1) {
                        print((this.a + this.b) as string);
                    }
                }

                class Test2 {
                }
            ");
            Assert.Throws<PenguinLangException>(compiler.Compile);
        }

    }
}
