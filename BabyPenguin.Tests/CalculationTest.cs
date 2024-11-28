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
                    var temp : i8 = 1;
                    var a : i8 = temp + 2 - 4 * 3 / 2;
                    var b : string = a as string;
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
                    var a : u8 = (4 + (4 - 2) * 3) / 5;
                    var b : string = a as string;
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
                    var a : bool = true && false;
                    var b : string = a as string;
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
                    var a : bool = true && false || true;
                    var b : string = a as string;
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
                    var a : bool = true && false || true && (1>2);
                    var b : string = a as string;
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
                    var a : bool = (1==1) && (4>=5) || (1<=1) && (1>2) || 1!=2 && 1<2;
                    var b : string = a as string;
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
                    var a : u8 = 30 & 15 | 10 ^ 5;
                    var b : string = a as string;
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
                    var a : u8 = 1 << 2 >> 1;
                    a <<= 2;
                    a >>= 1;
                    var b : string = a as string;
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
                    var a : bool = !(1==1);
                    var b : string = a as string;
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
                    var a : i16 = -(-1);
                    var b : string = a as string;
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
                    var a : i8 = 1;
                    a = ~a;
                    var b : string = a as string;
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
                    var a : u8 = 1;
                    {
                        var a : u8 = 2;
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
            var vm = new BabyPenguinVM(model);
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
                        var a : u8;
                        var b : u8;

                        fun print_sum() {
                            a=1;
                        }
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
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
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void ClassDefaultConstructorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        var test : Test = new Test();
                        test.print_sum();
                    }

                    class Test {
                        var a : u8=1;
                        var b : u8=1+1;

                        fun print_sum(val this: Test) {
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
        public void EnumBasicTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        var test : Test = new Test.a();
                        if (test is Test.a) {
                            print(""a"");
                        }
                        test = new Test.b(2);
                        if (test is Test.b) {
                            print(test.b as string);
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
        public void ClassConstructorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        var test : Test = new Test(2);
                        test.print_sum();
                    }

                    class Test {
                        var a : u8=1;
                        var b : u8;

                        fun print_sum(val this: Test) {
                            print((this.a + this.b) as string);
                        }

                        fun new(var this: Test, val b: u8) {
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

        // this test case requries flow analysis to determine every member is initialized or not.
        // [Fact]
        // public void ClassConstructorDefaultInitializerTest()
        // {
        //     var compiler = new SemanticCompiler();
        //     compiler.AddSource(@"
        //         namespace ns {
        //             initial {
        //                 var test : Test = new Test(2);
        //                 print(test.a as string);
        //                 print(test.b as string);
        //                 print(test.c as string); // empty
        //                 print(test.test2.d as string);
        //             }

        //             class Test {
        //                 var a : u8=1;
        //                 var b : u8;
        //                 var c : string;
        //                 var test2 : Test2;

        //                 fun new(var this: Test, val b: u8) {
        //                     this.b = b;
        //                 }
        //             }

        //             class Test2 {
        //                 var d : u8 = 3;
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
                        var x: T;
                    }

                    initial {
                        val t : Test<u8> = new Test<u8>();
                        t.x = 1;
                        print(t.x as string);

                        val t2 : Test<string> = new Test<string>();
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
                        var x: T;
                    }

                    class Test2 <T> {
                        var y: T;
                    }

                    initial {
                        val t : Test<Test2<u8>> = new Test<Test2<u8>>();
                        t.x = new Test2<u8>();
                        t.x.y = 1;
                        print(t.x.y as string);

                        val t2 : Test<Test2<string>> = new Test<Test2<string>>();
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
                        var x: T;
                        var y: U;
                    }

                    initial {
                        val t : Test<u8, string> = new Test<u8, string>();
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
                        var x: T;
                        var y: U;
                        fun print_sum(val this: Test<T, U>) {
                            print((this.x + this.y) as string);
                        }
                    }

                    initial {
                        val t : Test<u8, u16> = new Test<u8, u16>();
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

        [Fact]
        public void EnumGenericTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        var test : Test<u8> = new Test<u8>.a();
                        if (test is Test<u8>.a) {
                            print(""a"");
                        }
                        test = new Test<u8>.b(2);
                        if (test is Test<u8>.b) {
                            print(test.b as string);
                        } else if (test is Test<u8>.a) {
                            print(""not possible"");
                        }
                    }

                    enum Test <T> {
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
                        var test : Test<Foo> = new Test<Foo>.a();
                        if (test is Test<Foo>.a) {
                            print(""a"");
                        }
                        test = new Test<Foo>.b(new Foo());
                        if (test is Test<Foo>.b) {
                            print(test.b.x as string);
                            test.b.x = 1;
                            print(test.b.x as string);
                        } else if (test is Test<Foo>.a) {
                            print(""not possible"");
                        }
                    }

                    class Foo {
                        var x: u8 = 0;
                    }

                    enum Test <T> {
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
        public void InterfaceImplementationBasic()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun bar(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(val this: IFoo<u8>) -> u8 {
                                return 0;
                            }
                        }
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f as IFoo<u8>;
                        print(f2.foo() as string);
                        print(f2.bar() as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("01", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceCastToClass()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun bar(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        val a: u8 = 9;
                        impl IFoo<u8> {
                            fun foo(val this: IFoo<u8>) -> u8 {
                                val f : Foo = this as Foo;
                                return f.a;
                            }
                        }
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f as IFoo<u8>;
                        print(f2.foo() as string);
                        print(f2.bar() as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("91", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceUpcastDowncast()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun foo2(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    interface IBar<T> {
                        impl IFoo<T> {
                            fun foo(val this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                        fun bar(val this: IBar<T>) -> T {
                            return 3;
                        }
                    }
                    
                    class Foo {
                        impl IBar<u8>;
                        val a: u8 = 9;
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f as IFoo<u8>;
                        val f3 : IBar<u8> = f2 as IBar<u8>;
                        val f4 : IBar<u8> = f as IBar<u8>;
                        val f5 : IFoo<u8> = f3 as IFoo<u8>;
                        val f6 : Foo = f4 as Foo;
                        print(f2.foo() as string);
                        print(f2.foo2() as string);
                        print(f3.bar() as string);
                        print(f4.bar() as string);
                        print(f5.foo() as string);
                        print(f5.foo2() as string);
                        print(f6.a as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2133219", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceImplementationOverride()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun foo2(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    interface IBar<T> {
                        impl IFoo<T> {
                            fun foo(val this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(val this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                            fun foo2(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        val a: u8 = 9;
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f as IFoo<u8>;
                        val f3 : IBar<u8> = f2 as IBar<u8>;
                        val f4 : IFoo<u8> = f as IFoo<u8>;
                        print(f2.foo() as string);
                        print(f2.foo2() as string);
                        print(f4.foo() as string);
                        print(f4.foo2() as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3333", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceImplementationOverride2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun foo2(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    interface IBar<T> {
                        impl IFoo<T> {
                            fun foo(val this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(val this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo2(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        val a: u8 = 9;
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f as IFoo<u8>;
                        val f3 : IBar<u8> = f2 as IBar<u8>;
                        val f4 : IFoo<u8> = f3 as IFoo<u8>;
                        print(f2.foo() as string);
                        print(f2.foo2() as string);
                        print(f4.foo() as string);
                        print(f4.foo2() as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3333", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceImplicitCasting()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo(val this: IFoo<T>) -> T;
                        fun foo2(val this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    interface IBar<T> {
                        impl IFoo<T> {
                            fun foo(val this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(val this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo2(val this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        val a: u8 = 9;
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        val f2 : IFoo<u8> = f;
                        val f3 : IBar<u8> = f2 as IBar<u8>;
                        val f4 : IFoo<u8> = f3;
                        print(f2.foo() as string);
                        print(f2.foo2() as string);
                        print(f4.foo() as string);
                        print(f4.foo2() as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("3333", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceImplicitCastingInParameter()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo(val this: IFoo) {
                            print(""1"");
                        }
                        fun foo2(val this: Foo) {
                            print(this.a as string);
                        }
                    }
                    
                    class Foo {
                        impl IFoo;
                        val a : u8 = 2;
                    }

                    fun test(val a : Foo, val b: IFoo) {}
                
                    initial {
                        var f : Foo = new Foo();
                        test(f,f);
                        f.foo();
                        f.foo2();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }
    }
}
