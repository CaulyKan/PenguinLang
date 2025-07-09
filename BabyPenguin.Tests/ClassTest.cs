namespace BabyPenguin.Tests
{
    public class ClassTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void ClassMemberTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let test : mut Test = new Test();
                    test.a = 1;
                    test.b = 1;
                    test.b += 1;
                    print(test.a as string);
                    print(test.b as string);
                    print((test.a + test.b) as string);
                }

                class Test {
                    a : u8;
                    b : u8;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("123", vm.CollectOutput());
        }


        [Fact]
        public void ClassMemberCascadeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let test : mut Test2 = new Test2();
                    test.test1 = new Test1();
                    test.test1.a = 1;
                    test.test1.b = 1;
                    test.test1.b += 1;
                    print(test.test1.a as string);
                    print(test.test1.b as string);
                    print((test.test1.a + test.test1.b) as string);
                }

                class Test1 {
                    a : u8;
                    b : u8;
                }

                class Test2 {
                    test1: Test1;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
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
                        let test : mut Test = new Test();
                        test.a = 1;
                        test.b = 1;
                        test.print_sum();
                    }

                    class Test {
                        a : u8;
                        b : u8;

                        fun print_sum(this) {
                            print((this.a + this.b) as string);
                        }
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodCantVisitMemberDirectly()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Test {
                        a : u8;
                        b : u8;

                        fun print_sum() {
                            a=1;
                        }
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassMethodCascadeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let test : Test2 = new Test2();
                    test.test1 = new Test1();
                    test.test1.a = 1;
                    test.test1.b = 1;
                    test.test1.print_sum();
                }

                class Test1 {
                    a : u8;
                    b : u8;
                    fun print_sum(this) {
                        print((this.a + this.b) as string);
                    }
                }

                class Test2 {
                    test1: Test1;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
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
                    a : u8;
                    b : u8;
                    fun print_sum(this) {
                        print((this.a + this.b) as string);
                    }
                }

                fun foo() -> Test {
                    let test : Test = new Test();
                    test.a = 1;
                    test.b = 1;
                    return test;
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassMethodWrongOwnerTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let test : Test2 = new Test2();
                    test.print_sum();
                }

                class Test1 {
                    a : u8;
                    b : u8;
                    fun print_sum() {
                        print((this.a + this.b) as string);
                    }
                }

                class Test2 {
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassDefaultConstructorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : Test = new Test();
                        test.print_sum();
                    }

                    class Test {
                        a : u8=1;
                        b : u8=1+1;

                        fun print_sum(this) {
                            print((this.a + this.b) as string);
                        }
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void ClassConstructorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : Test = new Test(2);
                        test.print_sum();
                    }

                    class Test {
                        a : u8=1;
                        b : u8;

                        fun print_sum(this) {
                            print((this.a + this.b) as string);
                        }

                        fun new(mut this, b: u8) {
                            this.b = b;
                        }
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }

        [Fact]
        public void NamespaceTest()
        {
            {
                var compiler = new SemanticCompiler();
                compiler.AddSource(@"
                    namespace ns {
                        initial {
                            print(a as string);
                        }
                        let a : u8 = 1+1;
                    }
                ");
                var model = compiler.Compile();
                var vm = new BabyPenguinVM(model);
                vm.Run();
                Assert.Equal("2", vm.CollectOutput());
            }
        }

        // this test case requries flow analysis to determine every member is initialized or not.
        // [Fact]
        // public void ClassConstructorDefaultInitializerTest()
        // {
        //     var compiler = new SemanticCompiler();
        //     compiler.AddSource(@"
        //         namespace ns {
        //             initial {
        //                 let test : Test = new Test(2);
        //                 print(test.a as string);
        //                 print(test.b as string);
        //                 print(test.c as string); // empty
        //                 print(test.test2.d as string);
        //             }

        //             class Test {
        //                 let a : u8=1;
        //                 let b : u8;
        //                 let c : string;
        //                 let test2 : Test2;

        //                 fun new(let b: u8) {
        //                     this.b = b;
        //                 }
        //             }

        //             class Test2 {
        //                 let d : u8 = 3;
        //             }
        //         }
        //     ");
        //     var model = compiler.Compile();
        //     var vm = new BabyPenguinVM(model);
        //     vm.Run();
        //     Assert.Equal("123", vm.CollectOutput());
        // }

        [Fact]
        public void GenericTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Test <T> {
                        x: T;
                    }

                    initial {
                        let t : Test<u8> = new Test<u8>();
                        t.x = 1;
                        print(t.x as string);

                        let t2 : Test<string> = new Test<string>();
                        t2.x = ""2"";
                        print(t2.x);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void GenericCascadeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Test <T> {
                        x: T;
                    }

                    class Test2 <T> {
                        y: T;
                    }

                    initial {
                        let t : Test<Test2<u8>> = new Test<Test2<u8>>();
                        t.x = new Test2<u8>();
                        t.x.y = 1;
                        print(t.x.y as string);

                        let t2 : Test<Test2<string>> = new Test<Test2<string>>();
                        t2.x = new Test2<string>();
                        t2.x.y = ""2"";
                        print(t2.x.y);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }


        [Fact]
        public void GenericMutliParamTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Test <T, U> {
                        x: T;
                        y: U;
                    }

                    initial {
                        let t : Test<u8, string> = new Test<u8, string>();
                        t.x = 1;
                        t.y = ""2"";
                        print(t.x as string);
                        print(t.y as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void GenericMethodTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class Test <T, U> {
                        x: T;
                        y: U;
                        fun print_sum(this) {
                            print((this.x + this.y) as string);
                        }
                    }

                    initial {
                        let t : Test<u8, u16> = new Test<u8, u16>();
                        t.x = 1;
                        t.y = 2;
                        t.print_sum();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3", vm.CollectOutput());
        }
    }
}