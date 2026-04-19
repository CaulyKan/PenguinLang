namespace BabyPenguin.Tests
{
    public class CodeBlockExpressionTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void BasicCodeBlockExpression()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = {
                        let a: i64 = 1;
                        let b: i64 = 2;
                        a + b
                    };
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3" + EOL, vm.CollectOutput());
        }
        [Fact]
        public void IfElseExpression()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = if (true) {1} else {2};
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void WhileExpression()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = while (true) { break 1; };
                    println(cast<string>(x));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void SimpleCodeBlockExpression()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = { 42 };
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("42" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void NestedCodeBlockExpression()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = {
                        let outer: i64 = {
                            let inner: i64 = 5;
                            inner * 2
                        };
                        outer + 3
                    };
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("13" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void FunctionWithCodeBlockExpressionBody()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun add(a: i64, b: i64) -> i64 {
                    a + b
                }

                initial {
                    let result: i64 = add(10, 20);
                    println(result);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("30" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void FunctionWithCodeBlockExpressionBodyAndLocalVars()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun complex_calc(x: i64) -> i64 {
                    let doubled: i64 = x * 2;
                    let squared: i64 = doubled * doubled;
                    squared + x
                }

                initial {
                    let result: i64 = complex_calc(3);
                    println(result);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            // doubled = 6, squared = 36, result = 36 + 3 = 39
            Assert.Equal("39" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void CodeBlockExpressionWithDifferentTypes()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let s: string = { ""hello"" };
                    let b: bool = { true };
                    print(s);
                    println(b);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hellotrue" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void ChainedCodeBlockExpressions()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = { 1 } + { 2 } + { 3 };
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("6" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void CodeBlockExpressionWithFunctionCallAsTrailing()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun get_value() -> i64 {
                    42
                }

                initial {
                    let x: i64 = {
                        let dummy: i64 = 0;
                        get_value()
                    };
                    println(x);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("42" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void CodeBlockExpressionWithVariableShadowing()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = 10;
                    let result: i64 = {
                        let x: i64 = 5;
                        x * 2
                    };
                    print(x);
                    println(result);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1010" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void MultipleCodeBlockExpressionsInSequence()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a: i64 = { 1 };
                    let b: i64 = { 2 };
                    let c: i64 = { 3 };
                    println(a + b + c);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("6" + EOL, vm.CollectOutput());
        }
    }
}
