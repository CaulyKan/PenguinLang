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
            var vm = new BabyPenguinVM(model);
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
            var vm = new BabyPenguinVM(model);
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
            var vm = new BabyPenguinVM(model);
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
            var vm = new BabyPenguinVM(model);
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
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("0011", vm.CollectOutput());
        }

        [Fact]
        public void WhileBreakTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var i : u8 = 0;
                    while (i < 3) {
                        print(i as string);
                        if (i == 1) break;
                        i += 1;
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("01", vm.CollectOutput());
        }

        [Fact]
        public void WhileContinueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var i : u8 = 0;
                    while (i < 3) {
                        i += 1;
                        if (i == 2) continue;
                        print(i as string);
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("13", vm.CollectOutput());
        }

        [Fact]
        public void WhileCascadeBreakContinueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var i : u8 = 0;
                    var j : u8 = 0;
                    while (i < 3) {
                        i += 1;
                        j = 0;
                        while (j < 5) {
                            j += 1;
                            if (j == 2) continue;
                            if (j == 4) break;
                            print(j as string);
                        }
                        if (i == 2) break;
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1313", vm.CollectOutput());
        }

        [Fact]
        public void FunctionBasicTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    add(1,2);
                } 

                fun add(val a : u8, val b : u8) {
                    print((a + b) as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void FunctionReturnTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u8 = add(1,2);
                    print(res as string);
                } 

                fun add(val a : u8, val b : u8) -> u8 {
                    return a + b;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }


        // [Fact]
        public void FunctionNotAllPathReturnErrorTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u8 = add(1,2);
                    print(res as string);
                } 

                fun add(val a : u8, val b : u8) -> u8 {
                    if (false) {
                        return 0;
                    } else 
                    {
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }


        [Fact]
        public void FunctionRecursionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u32 = fib(10);
                    print(res as string);
                } 

                fun fib(val n: u32) -> u32 {
                    if (n == 0) {
                        return 0;
                    } else if (n == 1) {
                        return 1;
                    } else {
                        return fib(n-1) + fib(n-2);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("55", vm.CollectOutput());
        }

        [Fact]
        public void FunctionWrongArgument1()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u8 = add(1);
                    print(res as string);
                } 

                fun add(val a : u8, val b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void FunctionWrongArgument2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u8 = add(1,2,3);
                    print(res as string);
                } 

                fun add(val a : u8, val b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void FunctionWrongArgument3()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val res : u8 = add(1, 2 as string);
                    print(res as string);
                } 

                fun add(val a : u8, val b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void ForTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (var i : i64 in range(0, 3)) 
                        print(i as string);
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void ForBreakTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (var i : i64 in range(0, 10)) {
                        if (i == 3) break;
                        print(i as string); 
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void ForContinueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (var i : i64 in range(0, 10)) { 
                        if (i % 2 == 0) continue;
                        print(i as string);
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("13579", vm.CollectOutput());
        }

        [Fact]
        public void CascadeForTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (var i : i64 in range(0, 3))  
                        for (var j : i64 in range(0, 3)) 
                            print(j as string);
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012012012", vm.CollectOutput());
        }
    }
}
