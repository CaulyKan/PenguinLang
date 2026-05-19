namespace BabyPenguin.Tests
{
    public class EnumTest(ITestOutputHelper helper) : TestBase(helper)
    {

        [Fact]
        public void EnumBasicTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test = new Test.a();
                        if (test is Test.a) {
                            print(""a"");
                        }
                        test = new Test.b(2);
                        if (test is Test.b) {
                            print(cast<string>(test.b));
                        } else if (test is Test.a) {
                            print(""not possible"");
                        }
                    }

                    enum Test {
                        a;
                        b : u8;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a2", vm.CollectOutput());
        }

        [Fact]
        public void EnumGenericTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test<u8> = new Test<u8>.a();
                        if (test is Test<u8>.a) {
                            print(""a"");
                        }
                        test = new Test<u8>.b(2);
                        if (test is Test<u8>.b) {
                            print(cast<string>(test.b));
                        } else if (test is Test<u8>.a) {
                            print(""not possible"");
                        }
                    }

                    #template(T: type)
                    enum Test {
                        a;
                        b : T;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a2", vm.CollectOutput());
        }

        [Fact]
        public void EnumGenericCustomTypeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test<mut Foo> = new Test<mut Foo>.a();
                        if (test is Test<mut Foo>.a) {
                            print(""a"");
                        }
                        test = new Test<mut Foo>.b(new Foo());
                        if (test is Test<mut Foo>.b) {
                            print(cast<string>(test.b.x));
                            test.b.x = 1;
                            print(cast<string>(test.b.x));
                        } else if (test is Test<mut Foo>.a) {
                            print(""not possible"");
                        }
                    }

                    class Foo {
                        x: u8 = 0;
                    }

                    #template(T: type)
                    enum Test {
                        a;
                        b : T;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a01", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantMutableAccess_NonGenericTest()
        {
            // Test: modifying a class field through enum variant access (non-generic enum)
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    enum E {
                        a : Foo;
                    }

                    initial {
                        let e : mut E = new E.a(new Foo());
                        print(cast<string>(e.a.x));
                        e.a.x = 42;
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("042", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantAssignToMutableVariableTest()
        {
            // Test: assign enum variant value to a mutable variable, modify, check original
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    #template(T: type)
                    enum E {
                        a : T;
                    }

                    initial {
                        let e : mut E<mut Foo> = new E<mut Foo>.a(new Foo());
                        let foo : mut Foo = e.a;
                        foo.x = 99;
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("99", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantReplaceValueTest()
        {
            // Test: replace the entire enum variant's contained value
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    enum E {
                        a : Foo;
                    }

                    initial {
                        let e : mut E = new E.a(new Foo());
                        print(cast<string>(e.a.x));
                        e = new E.a(new Foo());
                        e.a.x = 10;
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("010", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantMethodCallTest()
        {
            // Test: call a method on the enum variant value and modify self
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    #template(T: type)
                    enum E {
                        a : T;
                    }

                    initial {
                        let e : mut E<mut Foo> = new E<mut Foo>.a(new Foo());
                        e.a.x = 5;
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("5", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantNestedAccessTest()
        {
            // Test: enum containing a class with another class field
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Inner {
                        val: mut i64 = 0;
                    }

                    class Outer {
                        inner: mut Inner = new Inner();
                    }

                    #template(T: type)
                    enum E {
                        a : T;
                    }

                    initial {
                        let e : mut E<mut Outer> = new E<mut Outer>.a(new Outer());
                        e.a.inner.val = 77;
                        print(cast<string>(e.a.inner.val));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("77", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantMethodModifySelfTest()
        {
            // Test: call a method on enum variant that modifies self
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Counter {
                        count: mut i64 = 0;

                        fun increment(this: mut Counter) {
                            this.count = this.count + 1;
                        }
                    }

                    #template(T: type)
                    enum E {
                        a : T;
                    }

                    initial {
                        let e : mut E<mut Counter> = new E<mut Counter>.a(new Counter());
                        e.a.increment();
                        e.a.increment();
                        print(cast<string>(e.a.count));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantPassToFunctionTest()
        {
            // Test: pass enum variant value to a function that modifies it
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    fun setX(foo: mut Foo, val: i64) {
                        foo.x = val;
                    }

                    #template(T: type)
                    enum E {
                        a : T;
                    }

                    initial {
                        let e : mut E<mut Foo> = new E<mut Foo>.a(new Foo());
                        setX(e.a, 55);
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("55", vm.CollectOutput());
        }

        [Fact]
        public void EnumVariantDirectAssignTest()
        {
            // Test: directly assign a new value to an enum variant's contained data
            // This was failing because the compiler generated WriteMemberInstruction (WRMBR)
            // instead of WriteEnumInstruction (WRENUM) for enum variant assignment.
            // WRMBR writes to Fields dictionary which doesn't contain the variant name,
            // while RDENUM reads from ContainingValue - so writes were lost.
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Foo {
                        x: mut i64 = 0;
                    }

                    enum E {
                        a : mut Foo;
                    }

                    initial {
                        let e : mut E = new E.a(new Foo());
                        e.a.x = 42;
                        print(cast<string>(e.a.x));
                        e.a = new Foo();
                        print(cast<string>(e.a.x));
                        e.a.x = 99;
                        print(cast<string>(e.a.x));
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("42099", vm.CollectOutput());
        }

    }
}
