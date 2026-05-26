namespace BabyPenguin.Tests
{
    public class BuiltinTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void VoidTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let v: void = void;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("", vm.CollectOutput());
        }

        [Fact]
        public void PrintTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello, "");
                    println(""world!"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello, world!" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void ExitTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello"");
                    exit(1);
                    print(""world"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            var code = vm.Run();
            Assert.Equal("hello", vm.CollectOutput());
            Assert.Equal(1, code);
        }

        [Fact]
        public void ProgramExitTest()
        {
            var code = Program.Main(new[] { "TestFiles/HelloWorld.penguin" });
            Assert.Equal(0, code);
        }

        [Fact]
        public void OptionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : Option<u32> = new Option<u32>.some(10);
                    println(cast<string>(a.is_some()));
                    println(cast<string>(a.is_none()));
                    println(cast<string>(a.value_or(9)));

                    let b : Option<u32> = new Option<u32>.none();
                    println(cast<string>(b.is_some()));
                    println(cast<string>(b.is_none()));
                    println(cast<string>(b.value_or(9)));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("true" + EOL + "false" + EOL + "10" + EOL + "false" + EOL + "true" + EOL + "9" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void RangeTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let rg : mut IIterator<i64> = range(0, 5);
                    while(true) {
                        let n : Option<i64> = rg.next();
                        if (n.is_none())
                            break;
                        else 
                            print(cast<string>(n.some));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("01234", vm.CollectOutput());
        }

        [Fact]
        public void RangeTest2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let rg : i64[] = range(0, 5);
                    while(true) {
                        let n : Option<i64> = rg.next();
                        if (n.is_none())
                            break;
                        else
                            print(cast<string>(n.some));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("01234", vm.CollectOutput());
        }

        [Fact]
        public void CopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Foo {
                    x : i64;
                    y : i64;

                    impl ICopy<Self>;
                }
                initial {
                    let a : mut Foo = new Foo();
                    a.x = 1;
                    a.y = 2;
                    let b : mut Foo = a.copy();
                    b.x = 3;
                    b.y = 4;
                    print(cast<string>(a.x));
                    print(cast<string>(a.y));
                    print(cast<string>(b.x));
                    print(cast<string>(b.y));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1234", vm.CollectOutput());
        }

        [Fact]
        public void BasicTypeCopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut u8 = 1;
                    let b : mut u8 = a.copy();
                    b = 2;
                    print(cast<string>(a));
                    print(cast<string>(b));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void ResultTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : Result<u32,string> = new Result<u32,string>.ok(10);
                    println(cast<string>(a.is_ok()));
                    println(cast<string>(a.is_error()));
                    println(cast<string>(a.value_or(9)));

                    let b : Result<u32,string> = new Result<u32,string>.error(""err"");
                    println(b.error);
                    println(cast<string>(b.is_ok()));
                    println(cast<string>(b.is_error()));
                    println(cast<string>(b.value_or(9)));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("true" + EOL + "false" + EOL + "10" + EOL + "err" + EOL + "false" + EOL + "true" + EOL + "9" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void AtomicTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut AtomicI64 = new AtomicI64(1);
                    println(cast<string>(a.load()));
                    a.store(2);
                    println(cast<string>(a.load()));
                    let res1: i64 = a.compare_exchange(2, 3);
                    println(cast<string>(res1));
                    println(cast<string>(a.load()));
                    let res2: i64 = a.compare_exchange(8888, 4);
                    println(cast<string>(res2));
                    println(cast<string>(a.load()));
                    let res3 : i64 = a.fetch_add(1);
                    println(cast<string>(res3));
                    let res4 : i64 = a.swap(5);
                    println(cast<string>(res4));
                    println(cast<string>(a.load()));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1" + EOL + "2" + EOL + "2" + EOL + "3" + EOL + "3" + EOL + "3" + EOL + "4" + EOL + "4" + EOL + "5" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void ListTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut _utils.List<i64> = new _utils.List<i64>();
                    a.push(1);
                    a.push(2);
                    a.push(3);
                    println(cast<string>(a.size()));
                    let res1 : Option<i64> = a.at(0);
                    println(cast<string>(res1.some));
                    let res2 : Option<i64> = a.at(2);
                    println(cast<string>(res2.some));
                    a.pop();
                    println(cast<string>(a.size()));
                    let res3 : Option<i64> = a.at(1);
                    println(cast<string>(res3.some));
                    let res4 : Option<i64> = a.at(2);
                    println(cast<string>(res4.is_none()));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3" + EOL + "1" + EOL + "3" + EOL + "2" + EOL + "2" + EOL + "true" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void ListTest2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun test(a: mut _utils.List<string>) {
                    for (let i : i64 in range(0, 2)) {
                        let s = cast<string>(i);
                        a.push(s);
                    }
                }
                initial {
                    let a : mut _utils.List<string> = new _utils.List<string>();
                    test(a);
                    println(cast<string>(a.size()));
                    let i: mut i64 = 0;
                    while (i < cast<i64>(a.size())) {{
                        let op = a.at(cast<u64>(i));
                        if (op.is_some()) {{
                            print(op.some);
                        }
                        }
                        i = i + 1;
                    }}
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2" + EOL + "01", vm.CollectOutput());
        }

        [Fact]
        public void ListForEachTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut _utils.List<i64> = new _utils.List<i64>();
                    a.push(1);
                    a.push(2);
                    a.push(3);
                    for (let x : i64 in a.iter()) {
                        print(cast<string>(x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }

        [Fact]
        public void QueueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut _utils.Queue<i64> = new _utils.Queue<i64>();
                    a.enqueue(1);
                    a.enqueue(2);
                    println(cast<string>(a.size()));
                    let res1 : Option<i64> = a.peek();
                    println(cast<string>(res1.some));
                    a.enqueue(3);
                    let res2 : Option<i64> = a.peek();
                    println(cast<string>(res2.some));
                    a.dequeue();
                    println(cast<string>(a.size()));
                    let res3 : Option<i64> = a.peek();
                    println(cast<string>(res3.some));
                    a.dequeue();
                    let res4 : Option<i64> = a.dequeue();
                    println(cast<string>(res4.some));
                    let res5 : Option<i64> = a.peek();
                    println(cast<string>(res5.is_none()));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;
            vm.Global.DebugFunc = (s) => testOutputHelper.WriteLine(s.TrimEnd('\n'));
            try { vm.Run(); } catch (Exception ex) { testOutputHelper.WriteLine($"EXCEPTION: {ex.Message}\n{ex.StackTrace}"); }
            Assert.Equal("2" + EOL + "1" + EOL + "1" + EOL + "2" + EOL + "2" + EOL + "3" + EOL + "true" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void TestDefaultRoutine()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let routine : mut _DefaultRoutine<void> = new _DefaultRoutine<void>(__builtin.hello_world, false);
                    println(cast<string>(routine.start()));
                    println(cast<string>(routine.routine_state()));

                    let state1 : FutureState<void>  = routine.poll();
                    println(cast<string>(state1));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("hello world!" + EOL + "true" + EOL + "finished" + EOL + "finished" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void FileIO_Test()
        {
            var script = @"
                initial {
                    let content = _utils.file_read_text(""test.tmp"");
                    println(content);
                    _utils.file_write_text(""test.tmp"", ""hello file"");
                }
            ";

            File.WriteAllText("test.tmp", "hello world");
            var (code, output) = RunScript(script);
            Assert.Equal(0, code);
            Assert.Equal("hello world" + EOL, output);

            var f = File.ReadAllText("test.tmp");
            Assert.Equal("hello file", f);
            File.Delete("test.tmp");
        }

        [Fact]
        public void Stderr_Test()
        {
            var script = @"initial { eprintln(""hello error""); }";

            // For now, we don't have stderr capture, just run to ensure no crash
            var (code, output) = RunScript(script);
            Assert.Equal(0, code);
        }

        [Fact]
        public void StringBuilder_Test()
        {
            var script = @"
                initial {
                    let mut sb = new StringBuilder();
                    sb.append(""hello"");
                    sb.append("" world"");
                    println(sb.to_string());
                }
            ";
            var (code, output) = RunScript(script);
            Assert.Equal(0, code);
            Assert.Equal("hello world" + EOL, output);
        }

        [Fact]
        public void Args_Test()
        {
            var script = @"
                initial {
                    let myargs : _utils.List<string> = args();
                    println(cast<string>(myargs.size()));
                    println(cast<string>(myargs.at(0)));
                    println(cast<string>(myargs.at(1)));
                    println(cast<string>(myargs.at(2)));
                }
            ";
            var (code, output) = RunScript(script, new[] { "arg1", "arg2" });
            Assert.Equal(0, code);
            Assert.Equal($"2{EOL}some(\"arg1\"){EOL}some(\"arg2\"){EOL}none{EOL}", output);
        }

        private (int, string) RunScript(string script, string[]? args = null)
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(script);
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            if (args != null)
            {
                vm.Global.CommandLineArgs = args;
            }
            var code = vm.Run();
            return (code, vm.CollectOutput());
        }
    }
}
