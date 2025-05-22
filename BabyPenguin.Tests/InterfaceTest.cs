namespace BabyPenguin.Tests
{
    public class InterfaceTest(ITestOutputHelper helper) : TestBase(helper)
    {
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

        [Fact]
        public void InterfaceDeclarationTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        var a : u8 = 1;
                        fun foo(val this: IFoo) {
                            print(this.a as string);
                        }
                    }
                    
                    class Foo {
                        impl IFoo;
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        f.foo();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceDeclarationExternImplErrorTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        var a : u8 = 1;
                        fun foo(val this: IFoo) {
                            print(this.a as string);
                        }
                    }
                    
                    class Foo {
                    }

                    impl IFoo for Foo;
                
                    initial {
                        var f : Foo = new Foo();
                        f.foo();
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void InterfaceDeclarationUseInClassTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        var a : u8 = 1;
                        fun foo(val this: IFoo) {
                            print(this.a as string);
                        }
                    }
                    
                    class Foo {
                        impl IFoo;
                        fun bar(val this: Foo) {
                            var f : IFoo = this as IFoo;
                            print(f.a as string);
                        }
                    }
                
                    initial {
                        var f : Foo = new Foo();
                        f.bar();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }
    }
}