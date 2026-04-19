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
        public void IfAndMutate()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut u8 = 0;
                    if (true) {
                        a = 2;
                    }
                    print(cast<string>(a));
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }


        [Fact]
        public void WhileTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let i : mut u8 = 0;
                    while (i < 3) {
                        print(cast<string>(i));
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
                    let i : mut u8 = 0;
                    let j : mut u8 = 0;
                    while (i < 2) 
                        while (i < 2) {
                            j = 0;
                            while (j < 2) {
                                print(cast<string>(i));
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
        public void WhileAndMutate()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut u8 = 0;
                    let i : mut u8 = 0;
                    while (i < 3) {
                        a += 1;
                        i += 1;
                    }
                    print(cast<string>(a));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }


        [Fact]
        public void WhileBreakTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let i : mut u8 = 0;
                    while (i < 3) {
                        print(cast<string>(i));
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
                    let i : mut u8 = 0;
                    while (i < 3) {
                        i += 1;
                        if (i == 2) continue;
                        print(cast<string>(i));
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
                    let i : mut u8 = 0;
                    let j : mut u8 = 0;
                    while (i < 3) {
                        i += 1;
                        j = 0;
                        while (j < 5) {
                            j += 1;
                            if (j == 2) continue;
                            if (j == 4) break;
                            print(cast<string>(j));
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
        public void WhileTrueBreakTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let i : mut u8 = 0;
                    while (true) {
                        if (i == 3) break;
                        i+=1;
                    }
                    print(cast<string>(i));
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void FunctionBasicTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    add(1,2);
                } 

                fun add(a : u8, b : u8) {
                    print(cast<string>(a + b));
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
                    let res : u8 = add(1,2);
                    print(cast<string>(res));
                }

                fun add(a : u8, b : u8) -> u8 {
                    return a + b;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }


        // [Fact]
        // public void FunctionNotAllPathReturnErrorTest()
        // {
        //     var compiler = new SemanticCompiler(new ErrorReporter(this));
        //     compiler.AddSource(@"
        //         initial {
        //             let res : u8 = add(1,2);
        //             print(res as string);
        //         } 

        //         fun add(a : u8, b : u8) -> u8 {
        //             if (false) {
        //                 return 0;
        //             } else 
        //             {
        //             }
        //         }
        //     ");
        //     Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        // }


        [Fact]
        public void FunctionRecursionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let res : u32 = fib(10);
                    print(cast<string>(res));
                } 

                fun fib(n: u32) -> u32 {
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
                    let res : u8 = add(1);
                    print(cast<string>(res));
                } 

                fun add(a : u8, b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void FunctionWrongArgument2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let res : u8 = add(1,2,3);
                    print(cast<string>(res));
                } 

                fun add(a : u8, b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void FunctionWrongArgument3()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let res : u8 = add(1, cast<string>(2));
                    print(cast<string>(res));
                } 

                fun add(a : u8, b : u8) -> u8 {
                    return a + b;
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ForTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (let i : i64 in range(0, 3))
                        print(cast<string>(i));
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
                    for (let i : i64 in range(0, 10)) {
                        if (i == 3) break;
                        print(cast<string>(i));
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
                    for (let i : i64 in range(0, 10)) { 
                        if (i % 2 == 0) continue;
                        print(cast<string>(i));
                    }
                } 
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("13579", vm.CollectOutput());
        }

        [Fact]
        public void MethodChainTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Wrapper {
                    val: mut i64 = 0;

                    fun new(this: mut Wrapper, v: i64) {
                        this.val = v;
                    }

                    fun add(this: mut Wrapper, v: i64) -> Wrapper {
                        this.val = this.val + v;
                        return this;
                    }

                    fun get(this) -> i64 {
                        return this.val;
                    }
                }

                initial {
                    let w: mut Wrapper = new Wrapper(1);
                    let result: i64 = w.add(2).get();
                    print(cast<string>(result));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void CascadeForTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    for (let i : i64 in range(0, 3))
                        for (let j : i64 in range(0, 3))
                            print(cast<string>(j));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012012012", vm.CollectOutput());
        }

        /// <summary>
        /// Regression test: function with mut local, conditional early return, then mut reassignment.
        /// This pattern is used by generated TypeSpecifier.build_text() and was returning empty string.
        /// </summary>
        [Fact]
        public void FunctionMutLocalWithConditionalReturn()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Formatter {
                    name: mut string = """";
                    flag: mut bool = false;

                    fun build_text(this) -> string {
                        let prefix: mut string = """";
                        if (this.flag) { prefix = ""mut ""; }
                        if (this.flag) {
                            return prefix + ""special"";
                        }
                        let s: mut string = prefix + this.name;
                        return s;
                    }
                }

                initial {
                    let f = new Formatter();
                    f.name = ""List"";
                    println(f.build_text());
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("List" + EOL, vm.CollectOutput());
        }

        [Fact]
        public void FunctionMutLocalWithConditionalReturnTriggered()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Formatter {
                    name: mut string = """";
                    flag: mut bool = false;

                    fun build_text(this) -> string {
                        let prefix: mut string = """";
                        if (this.flag) { prefix = ""mut ""; }
                        if (this.flag) {
                            return prefix + ""special"";
                        }
                        let s: mut string = prefix + this.name;
                        return s;
                    }
                }

                initial {
                    let f = new Formatter();
                    f.name = ""List"";
                    f.flag = true;
                    println(f.build_text());
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("mut special" + EOL, vm.CollectOutput());
        }

        /// <summary>
        /// Regression test: method `this` binding loses field mutations.
        /// After pushing to a List field and calling a method, `this` inside
        /// the method should see the pushed element. This was broken when the
        /// class had multiple List&lt;Self&gt; and Option&lt;Self&gt; fields.
        /// </summary>
        [Fact]
        public void MethodThisSeesFieldMutation()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    name: mut string = """";
                    children: mut List<Node> = new List<Node>();
                    flag: mut bool = false;
                    extra: mut List<Node> = new List<Node>();
                    opt: mut Option<Node> = new Option<Node>.none();

                    fun describe(this) -> string {
                        let s: mut string = this.name;
                        s = s + ""("" + cast<string>(this.children.size()) + "")"";
                        return s;
                    }
                }

                initial {
                    let inner = new Node();
                    inner.name = ""i64"";
                    let outer = new Node();
                    outer.name = ""List"";
                    outer.children.push(inner);
                    let size_before: string = cast<string>(outer.children.size());
                    let desc: string = outer.describe();
                    let size_after: string = cast<string>(outer.children.size());
                    println(size_before + ""|"" + desc + ""|"" + size_after);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1|List(1)|1" + EOL, vm.CollectOutput());
        }

        /// <summary>
        /// Regression test: multiple `let x: mut T` declarations in different if-blocks
        /// within the same function corrupt variable slots when the class has
        /// self-referential List&lt;Self&gt; and Option&lt;Self&gt; fields.
        ///
        /// When a function has:
        ///   if (cond) { let s: mut string = ...; ... return s; }
        ///   if (cond2) { let s: mut string = ...; ... return s; }
        ///   let s: mut string = ...; // third declaration with same name
        ///   ... this.field.size() ...
        ///
        /// The `this.field.size()` returns 0 even though items were pushed.
        /// Root cause: BabyPenguin compiler/VM incorrectly handles variable slots
        /// when there are multiple `let s: mut string` in unreachable if-blocks
        /// followed by field accesses on self-referential class fields.
        /// </summary>
        [Fact]
        public void MultipleMutLetWithSelfReferentialFields()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    name: mut string = """";
                    children: mut List<Node> = new List<Node>();
                    is_func: mut bool = false;
                    is_async_func: mut bool = false;
                    func_params: mut List<Node> = new List<Node>();
                    ret_type: mut Option<Node> = new Option<Node>.none();
                    parts: mut List<string> = new List<string>();

                    fun build_text(this) -> string {
                        let prefix: mut string = """";
                        if (this.is_func) {
                            let s: mut string = prefix + ""fun<placeholder>"";
                            return s;
                        }
                        if (this.is_async_func) {
                            let s: mut string = prefix + ""async_fun<placeholder>"";
                            return s;
                        }
                        let s: mut string = prefix + this.name;
                        if (cast<i64>(this.children.size()) > 0) {
                            s = s + ""<"";
                            let i: mut i64 = 0;
                            while (i < cast<i64>(this.children.size())) {
                                if (i > 0) { s = s + "", ""; }
                                s = s + this.children.at(cast<u64>(i)).some.name;
                                i = i + 1;
                            }
                            s = s + "">"";
                        }
                        return s;
                    }
                }

                initial {
                    let inner = new Node();
                    inner.name = ""i64"";
                    let outer = new Node();
                    outer.name = ""List"";
                    outer.children.push(inner);
                    println(outer.build_text());
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("List<i64>" + EOL, vm.CollectOutput());
        }
    }
}
