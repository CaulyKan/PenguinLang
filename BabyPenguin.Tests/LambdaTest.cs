namespace BabyPenguin.Tests
{
    public class LambdaTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void FunctionVariableTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun x() { print(""hello""); }
                initial {
                    val y : fun<void> = x;
                    y();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello", vm.CollectOutput());
        }

        [Fact]
        public void WrongFunctionTypeTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun x() -> i32 { 
                }
                initial {
                    val y : fun<void> = x;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void AsyncFunctionVariableTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun x() -> i32 { 
                    wait; 
                    return 1;
                }
                initial {
                    val y : async_fun<i32> = x;
                    val z : i32 = y();
                    print(z as string);
                }
            ");
            var model = compiler.Compile();
            model.WriteReport("report.txt");
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        //         [Fact]
        //         public void LambdaBasicTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     val x : fun<void> = fun { print(""hello""); };
        //                     x();
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("hello", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LambdaBasicReturnTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     val x : fun<i32, i32, i32> = fun (val a : i32, val b: i32) => i32 { return a + b; };
        //                     print(x(1, 2) as string);
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("3", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LocalFunctionTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     fun x { print(""hello""); };
        //                     x();
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("hello", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LocalFunctionReturnTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     fun x(val a: i32) => i32 { return a+1; };
        //                     print(x(1) as string);
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("2", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LambdaClosureBeforeRewriteTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 namespace ns {
        //                     class Temp {
        //                         val a : i32;
        //                         fun new(var this: Temp, val a : i32) {
        //                             this.a = a;
        //                         }
        //                         fun call(var this: Temp) => i32 {
        //                             return this.a + 1;
        //                         }
        //                     }
        //                     initial {
        //                         val a : i32 = 1;
        //                         val x : fun<i32, i32> = (new Temp(a)).call();
        //                         print(x() as string);
        //                     }
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("1", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LambdaClosureTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     val a : i32 = 1;
        //                     val x : fun<i32, i32> = fun { return a + 1; };
        //                     print(x() as string);
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("1", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LambdaNonReferenceClosureTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     val a : i32 = 1;
        //                     val x : fun<i32, i32> = fun { a=a+1; return a; };
        //                     print(x() as string);
        //                     print(a as string);
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("21", vm.CollectOutput());
        //         }

        //         [Fact]
        //         public void LambdaReferenceClosureTest()
        //         {
        //             var compiler = new SemanticCompiler(new ErrorReporter(this));
        //             compiler.AddSource(@"
        //                 initial {
        //                     val a : Box<i32> = new Box<i32>(1);
        //                     val x : fun<i32, Box<i32>> = fun { a.value = a.value + 1; return a.value; };
        //                     print(x() as string);
        //                     print(a.value as string);
        //                 }
        //             ");
        //             var model = compiler.Compile();
        //             var vm = new BabyPenguinVM(model);
        //             vm.Run();
        //             Assert.Equal("22", vm.CollectOutput());
        //         }
    }
}