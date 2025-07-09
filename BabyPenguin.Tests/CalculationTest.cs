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
                    let a : u8 = 1 + 2 - 4 * 3 / 2;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("253", vm.CollectOutput());
        }

        [Fact]
        public void AdditionTest2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let temp : i8 = 1;
                    let a : i8 = temp + 2 - 4 * 3 / 2;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("-3", vm.CollectOutput());
        }

        [Fact]
        public void ParenthesizedTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : u8 = (4 + (4 - 2) * 3) / 5;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest1()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : bool = true && false;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : bool = true && false || true;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("true", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest3()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : bool = true && false || true && (1>2);
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void BoolOperationTest4()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : bool = (1==1) && (4>=5) || (1<=1) && (1>2) || 1!=2 && 1<2;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("true", vm.CollectOutput());
        }

        [Fact]
        public void BitwiseOperationTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : u8 = 30 & 15 | 10 ^ 5;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal((30 & 15 | 10 ^ 5).ToString(), vm.CollectOutput());
        }

        [Fact]
        public void AssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : mut u8 = 1;
                    a += 2;
                    a -= 1;
                    a *= 3;
                    a /= 2;
                    let b : mut string = a as mut string;
                    b = b;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void ShiftTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : mut u8 = 1 << 2 >> 1;
                    a <<= 2;
                    a >>= 1;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("4", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest1()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : bool = !(1==1);
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("false", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : i16 = -(-1);
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void UnaryOperatorTest3()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : mut i8 = 1;
                    a = ~a;
                    let b : string = a as string;
                    print(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal((~1).ToString(), vm.CollectOutput());
        }

        [Fact]
        public void ShadowTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : u8 = 1;
                    {
                        let a : u8 = 2;
                        print(a as string);
                    }
                    print(a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("21", vm.CollectOutput());
        }

        [Fact]
        public void ShadowTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                let a : u8 = 1;
                initial {
                    let a : u8 = 2;
                    print(a as string);
                }

                initial {
                    print(a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("21", vm.CollectOutput());
        }


    }
}
