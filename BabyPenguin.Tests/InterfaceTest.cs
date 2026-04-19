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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun bar(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(this: IFoo<u8>) -> u8 {
                                return 0;
                            }
                        }
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.bar()));
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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun bar(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        a: u8 = 9;
                        impl IFoo<u8> {
                            fun foo(this: IFoo<u8>) -> u8 {
                                let f : Foo = cast<Foo>(this);
                                return f.a;
                            }
                        }
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.bar()));
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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun foo2(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    #template(T: type)
                    interface IBar {
                        impl IFoo<T> {
                            fun foo(this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                        fun bar(this: IBar<T>) -> T {
                            return 3;
                        }
                    }
                    
                    class Foo {
                        impl IBar<u8>;
                        a: u8 = 9;
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
                        let f3 : IBar<u8> = cast<IBar<u8>>(f2);
                        let f4 : IBar<u8> = cast<IBar<u8>>(f);
                        let f5 : IFoo<u8> = cast<IFoo<u8>>(f3);
                        let f6 : Foo = cast<Foo>(f4);
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.foo2()));
                        print(cast<string>(f3.bar()));
                        print(cast<string>(f4.bar()));
                        print(cast<string>(f5.foo()));
                        print(cast<string>(f5.foo2()));
                        print(cast<string>(f6.a));
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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun foo2(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    #template(T: type)
                    interface IBar {
                        impl IFoo<T> {
                            fun foo(this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                            fun foo2(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        a: u8 = 9;
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
                        let f3 : IBar<u8> = cast<IBar<u8>>(f2);
                        let f4 : IFoo<u8> = cast<IFoo<u8>>(f);
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.foo2()));
                        print(cast<string>(f4.foo()));
                        print(cast<string>(f4.foo2()));
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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun foo2(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    #template(T: type)
                    interface IBar {
                        impl IFoo<T> {
                            fun foo(this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo2(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        a: u8 = 9;
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
                        let f3 : IBar<u8> = cast<IBar<u8>>(f2);
                        let f4 : IFoo<u8> = cast<IFoo<u8>>(f3);
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.foo2()));
                        print(cast<string>(f4.foo()));
                        print(cast<string>(f4.foo2()));
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
                    #template(T: type)
                    interface IFoo {
                        fun foo(this: IFoo<T>) -> T;
                        fun foo2(this: IFoo<T>) -> T {
                            return 1;
                        }
                    }

                    #template(T: type)
                    interface IBar {
                        impl IFoo<T> {
                            fun foo(this: IFoo<T>) -> T {
                                return 2;
                            }
                            fun foo2(this: IFoo<T>) -> T {
                                return 2;
                            }
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        impl IBar<u8>;
                        impl IFoo<u8> {
                            fun foo2(this: IFoo<u8>) -> u8 {
                                return 3;
                            }
                        }
                        a: u8 = 9;
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        let f2 : IFoo<u8> = f;
                        let f3 : IBar<u8> = cast<IBar<u8>>(f2);
                        let f4 : IFoo<u8> = f3;
                        print(cast<string>(f2.foo()));
                        print(cast<string>(f2.foo2()));
                        print(cast<string>(f4.foo()));
                        print(cast<string>(f4.foo2()));
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
                        fun foo(this: IFoo) {
                            print(""1"");
                        }
                        fun foo2(this: Foo) {
                            print(cast<string>(this.a));
                        }
                    }

                    class Foo {
                        impl IFoo;
                        a : u8 = 2;
                    }

                    fun test(a : Foo, b: IFoo) {}
                
                    initial {
                        let f : Foo = new Foo();
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
                        a : u8 = 1;
                        fun foo(this: IFoo) {
                            print(cast<string>(this.a));
                        }
                    }

                    class Foo {
                        impl IFoo;
                    }

                    initial {
                        let f : Foo = new Foo();
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
                        a : u8 = 1;
                        fun foo(this: IFoo) {
                            print(cast<string>(this.a));
                        }
                    }

                    class Foo {
                    }

                    impl IFoo for Foo;
                
                    initial {
                        let f : Foo = new Foo();
                        f.foo();
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void InterfaceDeclarationUseInClassTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        a : u8 = 1;
                        fun foo(this: IFoo) {
                            print(cast<string>(this.a));
                        }
                    }

                    class Foo {
                        impl IFoo;
                        fun bar(this: Foo) {
                            let f : IFoo = cast<IFoo>(this);
                            print(cast<string>(f.a));
                        }
                    }
                
                    initial {
                        let f : Foo = new Foo();
                        f.bar();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionBasic()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo() {
                            print(""IFoo.foo"");
                        }
                    }
                    
                    initial {
                        IFoo.foo();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("IFoo.foo", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionBasicWithNamespace()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo() {
                            print(""IFoo.foo"");
                        }
                    }
                    
                    class Foo {
                        impl IFoo {
                            fun foo() {
                                print(""Foo.foo"");
                            }
                        }
                    }
                }
                
                initial {
                    ns.IFoo.foo();
                    ns.Foo.foo();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("IFoo.fooFoo.foo", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo() {
                            print(""IFoo.foo"");
                        }
                    }
                    
                    class Foo {
                        impl IFoo;
                    }
                
                    initial {
                        Foo.foo();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("IFoo.foo", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionOverrideTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo() {
                            print(""IFoo.foo"");
                        }
                    }
                    
                    class Foo {
                        impl IFoo {
                            fun foo() {
                                print(""Foo.foo"");
                            }
                        }
                    }
                
                    initial {
                        Foo.foo();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("Foo.foo", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionAsMemberCallOverrideTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun foo() {
                            print(""IFoo.foo"");
                        }
                    }
                    
                    class Foo {
                        impl IFoo {
                            fun foo() {
                                print(""Foo.foo"");
                            }
                        }
                    }
                
                    initial {
                        let f : IFoo = new Foo();
                        f.foo();
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("Foo.foo", vm.CollectOutput());
        }

        [Fact]
        public void InterfaceStaticFunctionAmbiguousTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IB {
                        fun foo() {
                            print(""IB.foo"");
                        }
                    }

                    interface IC {
                        fun foo() {
                            print(""IC.foo"");
                        }
                    }

                    class Foo {
                        impl IB;
                        impl IC;
                    }

                    initial {
                        Foo.foo();
                    }
                }
            ");
            var ex = Assert.Throws<BabyPenguinException>(() => compiler.Compile());
            Assert.Contains("Ambiguous", ex.Message);
            Assert.Contains("IB.foo", ex.Message);
            Assert.Contains("IC.foo", ex.Message);
        }


    }
}
