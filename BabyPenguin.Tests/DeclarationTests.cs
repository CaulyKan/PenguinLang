namespace BabyPenguin.Tests
{
    public class DeclarationTests(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void GlobalDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                val test1 : string = "" "";
                val test2 : u8 = 1;
                val test3 : i32 = 1;
                val test4 : bool = true;
                val test5 : float = 3.14159;
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;
            Assert.Equal(5, ns.Symbols.Where(i => !i.IsTemp).Count());
            Assert.True(ns.Symbols.ElementAt(0).TypeInfo.IsStringType);
            Assert.Equal("test1", ns.Symbols.ElementAt(0).Name);
            Assert.True(ns.Symbols.ElementAt(1).TypeInfo.FullName == "u8");
            Assert.Equal("test2", ns.Symbols.ElementAt(1).Name);
            Assert.True(ns.Symbols.ElementAt(2).TypeInfo.FullName == "i32");
            Assert.Equal("test3", ns.Symbols.ElementAt(2).Name);
            Assert.True(ns.Symbols.ElementAt(3).TypeInfo.IsBoolType);
            Assert.Equal("test4", ns.Symbols.ElementAt(3).Name);
            Assert.True(ns.Symbols.ElementAt(4).TypeInfo.IsFloatType);
            Assert.Equal("test5", ns.Symbols.ElementAt(4).Name);
        }

        [Fact]
        public void NamesapceDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace Test {
                    val test1 : string = "" "";
                    val test2 : u8 = 1;
                    val test3 : i32 = 1;
                    val test4 : bool = true;
                    val test5 : float = 3.14159;
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name == "Test");
            Assert.Equal(5, ns!.Symbols.Where(i => !i.IsTemp).Count());
            Assert.True(ns.Symbols.ElementAt(0).TypeInfo.IsStringType);
            Assert.True(ns.Symbols.ElementAt(0).FullName == "Test.test1");
            Assert.True(ns.Symbols.ElementAt(1).TypeInfo.FullName == "u8");
            Assert.True(ns.Symbols.ElementAt(1).FullName == "Test.test2");
            Assert.True(ns.Symbols.ElementAt(2).TypeInfo.FullName == "i32");
            Assert.True(ns.Symbols.ElementAt(2).FullName == "Test.test3");
            Assert.True(ns.Symbols.ElementAt(3).TypeInfo.IsBoolType);
            Assert.True(ns.Symbols.ElementAt(3).FullName == "Test.test4");
            Assert.True(ns.Symbols.ElementAt(4).TypeInfo.IsFloatType);
            Assert.True(ns.Symbols.ElementAt(4).FullName == "Test.test5");
        }

        [Fact]
        public void NamesapceDuplicateDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace Test {
                    val test1 : string = "" "";
                    val test1 : u8 = 1;
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void NamesapceMerging()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace Test {
                    val test1 : string = "" "";
                }

                namespace Test {
                    val test2 : string = "" "";
                }
            ");
            var model = compiler.Compile();
            Assert.Equal(2, model.Namespaces.Count);
            var ns = model.Namespaces.Find(x => x.Name == "Test")!;
            Assert.Equal(2, ns!.Symbols.Count());
        }

        [Fact]
        public void ClassDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class TestClass {}
                namespace Test {
                    class TestClass {}
                    class TestClass2 {}
                }
            ");
            var model = compiler.Compile();
            Assert.Equal(3, model.Namespaces.Count);

            var ns = model.Namespaces.Find(x => x.Name != "__builtin" && x.Name != "Test")!;
            Assert.Single(ns.Classes);
            Assert.Equal("TestClass", ns.Classes.ElementAt(0).Name);

            var ns2 = model.Namespaces.Find(x => x.Name == "Test")!;
            Assert.Equal(2, ns2.Classes.Count());
            Assert.Equal("Test.TestClass", (ns2.Classes.First() as IClass).FullName);
        }

        [Fact]
        public void FunctionDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun test1() -> string {}
                namespace Test {
                fun test1() {}
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin" && x.Name != "Test")!;
            Assert.Single(ns.Symbols);
            Assert.Single(ns.Functions);
            Assert.Equal("test1", ns.Symbols.ElementAt(0).Name);
            Assert.True(ns.Symbols.ElementAt(0) is FunctionSymbol);
            Assert.True(((FunctionSymbol)ns.Symbols.ElementAt(0)).ReturnTypeInfo.IsStringType);
            Assert.True(((FunctionSymbol)ns.Symbols.ElementAt(0)).Parameters.Count == 0);

            var ns2 = model.Namespaces.Find(x => x.Name == "Test")!;
            Assert.Single(ns2.Symbols);
            Assert.Single(ns2.Functions);
            Assert.Equal("Test.test1", ns2.Symbols.ElementAt(0).FullName);
            Assert.True(ns2.Symbols.ElementAt(0) is FunctionSymbol);
            Assert.True(((FunctionSymbol)ns2.Symbols.ElementAt(0)).ReturnTypeInfo.IsVoidType);
            Assert.True(((FunctionSymbol)ns2.Symbols.ElementAt(0)).Parameters.Count == 0);
        }

        [Fact]
        public void InitialDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial foo {

                }
                namespace Test {
                initial {}
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin" && x.Name != "Test")!;

            Assert.Single(ns.InitialRoutines);
            Assert.Equal("foo", ns.InitialRoutines.ElementAt(0).Name);

            var ns2 = model.Namespaces.Find(x => x.Name == "Test")!;
            Assert.Single(ns2.InitialRoutines);
        }

        [Fact]
        public void InitialVarDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var test1 : string = "" "";
                    val test2 : u8 = 1;
                    val test3 : i32 = 1;
                    val test4 : bool = true;
                    val test5 : float = 3.14159;
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.InitialRoutines);
            var symbols = ns.InitialRoutines.ElementAt(0).Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(5, symbols.Count());
            Assert.Equal("test1", symbols[0].Name);
            Assert.True(symbols[0].TypeInfo.IsStringType);
            Assert.True(symbols[0].IsLocal);
            Assert.True(symbols[1].TypeInfo.FullName == "u8");
            Assert.Equal("test2", symbols[1].Name);
            Assert.True(symbols[2].TypeInfo.FullName == "i32");
            Assert.Equal("test3", symbols[2].Name);
            Assert.True(symbols[3].TypeInfo.IsBoolType);
            Assert.Equal("test4", symbols[3].Name);
            Assert.True(symbols[4].TypeInfo.IsFloatType);
            Assert.Equal("test5", symbols[4].Name);
        }

        [Fact]
        public void FunVarDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun test(val param1: u64, var param2: char) {
                    var test1 : string = "" "";
                    val test2 : u8 = 1;
                    val test3 : i32 = 1;
                    val test4 : bool = true;
                    val test5 : float = 3.14159;
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.Functions);
            var symbols = ns.Functions.ElementAt(0).Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(7, symbols.Count());
            Assert.Equal("param1", symbols[0].Name);
            Assert.Equal("u64", symbols[0].TypeInfo.FullName);
            Assert.True(symbols[0].IsReadonly);
            Assert.True(symbols[0].IsParameter);

            Assert.Equal("param2", symbols[1].Name);
            Assert.Equal("char", symbols[1].TypeInfo.FullName);
            Assert.False(symbols[1].IsReadonly);
            Assert.True(symbols[1].IsParameter);

            Assert.True(symbols[2].TypeInfo.IsStringType);
            Assert.True(symbols[2].IsLocal);

            Assert.True(symbols[3].TypeInfo.FullName == "u8");
            Assert.False(symbols[3].IsParameter);
            Assert.Equal("test2", symbols[3].Name);

            Assert.True(symbols[4].TypeInfo.FullName == "i32");
            Assert.Equal("test3", symbols[4].Name);

            Assert.True(symbols[5].TypeInfo.IsBoolType);
            Assert.Equal("test4", symbols[5].Name);

            Assert.True(symbols[6].TypeInfo.IsFloatType);
            Assert.Equal("test5", symbols[6].Name);
        }

        [Fact]
        public void ShadowDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    var test1 : string = "" "";
                    var test1 : string = "" "";
                    {
                        var test1 : string = "" "";
                    }
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.InitialRoutines);
            var symbols = ns.InitialRoutines.ElementAt(0).Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(3, symbols.Count());
            Assert.Equal("test1", symbols[0].Name);
            Assert.Equal("test1", symbols[0].OriginName);
            Assert.True(symbols[0].TypeInfo.IsStringType);
            Assert.True(symbols[0].IsLocal);

            Assert.NotEqual("test1", symbols[1].Name);
            Assert.Equal("test1", symbols[1].OriginName);
            Assert.True(symbols[1].TypeInfo.IsStringType);
            Assert.True(symbols[1].IsLocal);
            Assert.Equal(symbols[0].ScopeDepth, symbols[1].ScopeDepth);

            Assert.NotEqual("test1", symbols[2].Name);
            Assert.Equal("test1", symbols[2].OriginName);
            Assert.True(symbols[2].TypeInfo.IsStringType);
            Assert.True(symbols[2].IsLocal);
            Assert.Equal(symbols[0].ScopeDepth + 1, symbols[2].ScopeDepth);
        }

        [Fact]
        public void ShadowDeclare2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                var test1 : string = "" "";
                initial {
                    var test1 : string = "" "";
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.Symbols);
            Assert.Equal("test1", ns.Symbols.ElementAt(0).Name);

            Assert.Single(ns.InitialRoutines);
            var symbols = ns.InitialRoutines.ElementAt(0).Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Single(symbols);
            Assert.NotEqual("test1", symbols[0].Name);
            Assert.Equal("test1", symbols[0].OriginName);
            Assert.True(symbols[0].TypeInfo.IsStringType);
            Assert.True(symbols[0].IsLocal);
        }

        [Fact]
        public void DuplicateClassDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class A {}
                class A {}
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void DuplicateFunctionParam()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun test(val param1: u64, var param1: char){}
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void ClassMemberDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Test {
                    var test1 : string = "" "";
                    var test2 : u8 = 1;
                    var test3 : i32 = 1;
                    val test4 : bool = true;
                    val test5 : float = 3.14159;
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.Classes);
            var symbols = ns.Classes.ElementAt(0).Symbols.Where(x => !x.IsTemp && x is not FunctionSymbol).ToList();
            Assert.Equal(5, symbols.Count());
            Assert.Equal("test1", symbols[0].Name);
            Assert.True(symbols[0].TypeInfo.IsStringType);
            Assert.True(symbols[0].IsClassMember);
            Assert.False(symbols[0].IsReadonly);
            Assert.True(symbols[1].TypeInfo.FullName == "u8");
            Assert.Equal("test2", symbols[1].Name);
            Assert.True(symbols[2].TypeInfo.FullName == "i32");
            Assert.Equal("test3", symbols[2].Name);
            Assert.True(symbols[3].TypeInfo.IsBoolType);
            Assert.Equal("test4", symbols[3].Name);
            Assert.True(symbols[4].TypeInfo.IsFloatType);
            Assert.Equal("test5", symbols[4].Name);
            Assert.True(symbols[4].IsReadonly);
        }

        [Fact]
        public void ClassMethodDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Test {
                    fun test1() {}
                    fun test2(var this: Test) {}
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            var cls = ns.Classes.ElementAt(0);
            Assert.Equal(3, cls.Functions.Count);
            Assert.Equal("test1", cls.Functions[0].Name);
            Assert.Equal("test2", cls.Functions[1].Name);
            Assert.Equal("new", cls.Functions[2].Name);
            Assert.True(cls.Functions[0].IsStatic);
            Assert.False(cls.Functions[1].IsStatic);
            Assert.False(cls.Functions[2].IsStatic);

            var fun1 = model.ResolveSymbol("test1", scope: cls);
            Assert.True(fun1 is FunctionSymbol);
            Assert.True(fun1.IsStatic);

            var fun2 = model.ResolveSymbol("test2", scope: cls);
            Assert.True(fun2 is FunctionSymbol);
            Assert.False(fun2.IsStatic);
        }

        [Fact]
        public void EnumDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                enum Test {
                    a;
                    b;
                    c: u8;
                    d: string;
                }
            ");
            var model = compiler.Compile();
            var ns = model.Namespaces.Find(x => x.Name != "__builtin")!;

            Assert.Single(ns.Enums);
            Assert.Equal("Test", ns.Enums.ElementAt(0).Name);
            Assert.Equal(4, ns.Enums.ElementAt(0).EnumDeclarations.Count);
            Assert.Equal("a", ns.Enums.ElementAt(0).EnumDeclarations[0].Name);
            Assert.Equal("void", ns.Enums.ElementAt(0).EnumDeclarations[0].TypeInfo.Name);
            Assert.Equal("b", ns.Enums.ElementAt(0).EnumDeclarations[1].Name);
            Assert.Equal("void", ns.Enums.ElementAt(0).EnumDeclarations[1].TypeInfo.Name);
            Assert.Equal("c", ns.Enums.ElementAt(0).EnumDeclarations[2].Name);
            Assert.Equal("u8", ns.Enums.ElementAt(0).EnumDeclarations[2].TypeInfo.Name);
            Assert.Equal("d", ns.Enums.ElementAt(0).EnumDeclarations[3].Name);
            Assert.Equal("string", ns.Enums.ElementAt(0).EnumDeclarations[3].TypeInfo.Name);
        }

        [Fact]
        public void ResolveTypeBasic()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    enum Foo { }
                    class Bar { }
                }
            ");
            var model = compiler.Compile();

            var foo1 = model.ResolveType("ns.Foo");
            Assert.NotNull(foo1);
            Assert.Equal("ns.Foo", foo1.FullName);

            var foo2 = model.ResolveType("Foo", scope: model.Classes.FirstOrDefault(c => c.Name == "Bar"));
            Assert.NotNull(foo2);
            Assert.Equal("ns.Foo", foo2.FullName);

            var bar1 = model.ResolveType("ns.Bar");
            Assert.NotNull(bar1);
            Assert.Equal("ns.Bar", bar1.FullName);

            var bar2 = model.ResolveType("Bar", scope: model.Classes.FirstOrDefault(c => c.Name == "Bar"));
            Assert.NotNull(bar2);
            Assert.Equal("ns.Bar", bar2.FullName);
        }

        [Fact]
        public void ResolveTypeWithGenericClass()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Bar<X,Y,Z> { }
                }
            ");
            var model = compiler.Compile();

            var bar1 = model.ResolveType("ns.Bar<?,?,?>");
            Assert.NotNull(bar1);
            Assert.True(bar1.IsGeneric);
            Assert.False(bar1.IsSpecialized);
            Assert.Equal("ns.Bar<?,?,?>", bar1.FullName);

            var bar2 = model.ResolveType("ns.Bar<u8,i8,string>");
            Assert.NotNull(bar2);
            Assert.True(bar2.IsGeneric);
            Assert.True(bar2.IsSpecialized);
            Assert.Equal("u8", bar2.GenericArguments[0].FullName);
            Assert.Equal("i8", bar2.GenericArguments[1].FullName);
            Assert.Equal("string", bar2.GenericArguments[2].FullName);
            Assert.Equal("ns.Bar<u8,i8,string>", bar2.FullName);

            var bar3 = model.ResolveType("Bar<?,?,?>", scope: model.Classes.FirstOrDefault(c => c.Name == "Bar"));
            Assert.True(bar1.FullName == bar3!.FullName);
            Assert.Single(bar3.GenericInstances);
            Assert.True(bar3.GenericInstances.First() == bar2);
        }

        [Fact]
        public void ResolveTypeWithGenericEnum()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    enum Foo<T> { }
                }
            ");
            var model = compiler.Compile();

            var foo1 = model.ResolveType("ns.Foo<?>");
            Assert.NotNull(foo1);
            Assert.True(foo1.IsGeneric);
            Assert.False(foo1.IsSpecialized);
            Assert.Equal("ns.Foo<?>", foo1.FullName);

            var foo2 = model.ResolveType("ns.Foo<u8>");
            Assert.NotNull(foo2);
            Assert.True(foo2.IsGeneric);
            Assert.True(foo2.IsSpecialized);
            Assert.Equal("ns.Foo<u8>", foo2.FullName);

            var foo3 = model.ResolveType("Foo<?>", scope: model.Namespaces.Find(i => i.Name == "ns"));
            Assert.True(foo1.FullName == foo3!.FullName);
            Assert.Single(foo3.GenericInstances);
            Assert.True(foo3.GenericInstances.First() == foo2);
        }

        [Fact]
        public void ResolveTypeInsideGenericClass()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Foo {}
                    class Bar<T,Foo> { }
                }
            ");
            var model = compiler.Compile();

            var bar1 = model.ResolveType("ns.Bar <  u8, i8>");
            Assert.NotNull(bar1);
            Assert.True(bar1.IsGeneric);
            Assert.True(bar1.IsSpecialized);
            Assert.Equal("ns.Bar<u8,i8>", bar1.FullName);

            var T1 = model.ResolveType("T", scope: bar1 as IClass);
            Assert.Equal("u8", T1!.FullName);

            var T2 = model.ResolveType("Foo", scope: bar1 as IClass);
            Assert.Equal("i8", T2!.FullName);

            var fooOutOfScope = model.ResolveType("Foo", scope: model.Namespaces.Find(i => i.Name == "ns"));
            Assert.Equal("ns.Foo", fooOutOfScope!.FullName);
        }

        [Fact]
        public void ResolveTypeGenericCascade()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Foo<T> {}
                    class Bar<T> { 
                        val a : T;
                    }
                }
            ");
            var model = compiler.Compile();

            var bar1 = model.ResolveType("ns.Bar<ns.Foo<u8>>");
            Assert.True(bar1!.IsSpecialized);
            Assert.Equal("ns.Bar<ns.Foo<u8>>", bar1.FullName);
            Assert.Equal("ns.Foo<u8>", bar1.GenericArguments[0].FullName);
            Assert.Equal("u8", bar1.GenericArguments[0].GenericArguments[0].FullName);

            var bar2 = model.ResolveType("Bar<Foo<u8>>", scope: model.Classes.FirstOrDefault(c => c.Name == "Bar"));
            Assert.Equal(bar1.FullName, bar2!.FullName);

            var foo1 = model.ResolveType("T", scope: bar1 as IClass);
            Assert.Equal("ns.Foo<u8>", foo1!.FullName);

            var u8 = model.ResolveType("T", scope: foo1 as IClass);
            Assert.Equal("u8", u8!.FullName);
        }

        [Fact]
        public void ResolveShortSymbol()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    var a : u8 = 1;
                    class Foo {
                        var a : u8 = 1;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var foo = ns!.Classes.FirstOrDefault(i => i.Name == "Foo");
            Assert.Equal("ns.a", model.ResolveShortSymbol("a", scope: ns)!.FullName);
            Assert.Equal("ns.Foo.a", model.ResolveShortSymbol("a", scope: foo)!.FullName);
        }

        [Fact]
        public void ResolveSymbol()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    var a : u8 = 1;
                    class Foo {
                        var a : u8 = 1;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var foo = ns!.Classes.FirstOrDefault(i => i.Name == "Foo");
            Assert.Equal("ns.a", model.ResolveSymbol("ns.a")!.FullName);
            Assert.Equal("ns.a", model.ResolveSymbol("a", scope: ns)!.FullName);
            Assert.Equal("ns.a", model.ResolveSymbol("ns.a", scope: ns)!.FullName);
            Assert.Equal("ns.Foo.a", model.ResolveSymbol("a", scope: foo)!.FullName);
            Assert.Equal("ns.Foo.a", model.ResolveSymbol("Foo.a", scope: foo)!.FullName);
            Assert.Equal("ns.Foo.a", model.ResolveSymbol("ns.Foo.a", scope: foo)!.FullName);
        }

        [Fact]
        public void ResolveSymbolWithGeneric()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Foo<T> {}
                    class Bar<T> { 
                        val a : T;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var symbol1 = model.ResolveSymbol("ns.Bar<ns.Foo<u8>>.a");
            Assert.Equal("ns.Bar<ns.Foo<u8>>.a", symbol1!.FullName);
            Assert.Equal("ns.Foo<u8>", symbol1.TypeInfo.FullName);

            var symbol2 = model.ResolveSymbol("Bar<Foo<i8>>.a", scope: ns);
            Assert.Equal("ns.Bar<ns.Foo<i8>>.a", symbol2!.FullName);
            Assert.Equal("ns.Foo<i8>", symbol2.TypeInfo.FullName);

            var bar1 = model.ResolveType("ns.Bar<ns.Foo<string>>");
            var symbol3 = model.ResolveSymbol("a", scope: bar1 as IClass);
            Assert.Equal("ns.Bar<ns.Foo<string>>.a", symbol3!.FullName);
            Assert.Equal("ns.Foo<string>", symbol3.TypeInfo.FullName);
        }

        [Fact]
        public void CantDeclareTypeWithoutGeneric1()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Foo<T> {}
                    class Bar { 
                        val a : Foo;
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void CantDeclareTypeWithoutGeneric2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    class Foo<T> {}
                    initial {
                        val a : Foo;
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }


        [Fact]
        public void InterfaceDefinition()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {}
                    interface IBar<T> { 
                        fun bar() -> T;
                        fun bar2(val this: IBar<T>) {}
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var ifoo = ns!.Interfaces.FirstOrDefault(i => i.Name == "IFoo");
            var ibar = ns.Interfaces.FirstOrDefault(i => i.Name == "IBar");
            Assert.NotNull(ifoo);
            Assert.NotNull(ibar);
            Assert.Single(ibar.GenericDefinitions);

            var ibar_u8 = model.ResolveType("ns.IBar<u8>") as Interface;
            Assert.NotNull(ibar_u8);

            var bar = ibar_u8.Functions.Find(i => i.Name == "bar");
            Assert.Equal("u8", bar!.ReturnTypeInfo.FullName);
            Assert.True(bar.IsDeclarationOnly);
            var bar2 = ibar_u8.Functions.Find(i => i.Name == "bar2");
            Assert.Equal("ns.IBar<u8>", bar2!.Parameters[0].Type.FullName);
            Assert.Equal("this", bar2!.Parameters[0].Name);
            Assert.False(bar2.IsDeclarationOnly);
        }

        [Fact]
        public void InterfaceImplementation()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {}
                    
                    class Foo {
                        impl IFoo;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var ifoo = ns!.Interfaces.FirstOrDefault(i => i.Name == "IFoo");
            var foo = ns.Classes.FirstOrDefault(i => i.Name == "Foo");
            Assert.NotNull(ifoo);
            Assert.NotNull(foo);
            Assert.Single(foo.VTables);
            Assert.Equal("ns.IFoo", foo.VTables[0].Interface?.FullName);
        }

        [Fact]
        public void InterfaceFunctionImplementation()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo() -> T;
                        fun bar() -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo() -> u8 {
                                return 1;
                            }
                        }
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var foo = ns!.Classes.FirstOrDefault(i => i.Name == "Foo");
            Assert.Single(foo!.VTables);
            var slotFoo = foo.VTables[0].Slots.Find(i => i.InterfaceSymbol.Name == "foo");
            var slotBar = foo.VTables[0].Slots.Find(i => i.InterfaceSymbol.Name == "bar");
            Assert.Equal("ns.IFoo<u8>.bar", slotBar!.InterfaceSymbol.FullName);
            Assert.Equal("ns.IFoo<u8>.bar", slotBar.ImplementationSymbol.FullName);
            Assert.Equal("ns.IFoo<u8>.foo", slotFoo!.InterfaceSymbol.FullName);
            Assert.Equal("ns.Foo.vtable-ns-IFoo<u8>.foo", slotFoo.ImplementationSymbol.FullName);
        }

        [Fact]
        public void InterfaceFunctionImplementationError1()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo() -> T;
                        fun bar() -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8>;
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void InterfaceFunctionImplementationError2()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo() -> T;
                        fun bar() -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo() -> u8 {
                                return 1;
                            }
                            fun foo2() -> u8 {
                                return 1;
                            }
                        }
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void InterfaceFunctionImplementationError3()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo<T> {
                        fun foo() -> T;
                        fun bar() -> T {
                            return 1;
                        }
                    }
                    
                    class Foo {
                        impl IFoo<u8> {
                            fun foo() -> u16 {
                                return 1;
                            }
                        }
                    }
                }
            ");
            Assert.Throws<BabyPenguinException>(compiler.Compile);
        }

        [Fact]
        public void InterfaceCascadeImplementation()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {}
                    interface IBar<T> { 
                        impl IFoo;
                    }
                    interface IQux<T> { 
                        impl IBar<T>;
                    }
                    class Qux {
                        impl IQux<u8>;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var qux = ns!.Classes.FirstOrDefault(i => i.Name == "Qux") as IClass;
            Assert.Equal(3, qux!.ImplementedInterfaces.Count());
            Assert.Contains("ns.IFoo", qux.ImplementedInterfaces.Select(i => i.FullName));
            Assert.Contains("ns.IBar<u8>", qux.ImplementedInterfaces.Select(i => i.FullName));
            Assert.Contains("ns.IQux<u8>", qux.ImplementedInterfaces.Select(i => i.FullName));
        }

        [Fact]
        public void InterfaceImplicitlyConversion()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {}
                    interface IBar<T> { 
                        impl IFoo;
                    }
                    interface IQux<T> { 
                        impl IBar<T>;
                    }
                    class Qux {
                        impl IQux<u8>;
                    }
                }
            ");
            var model = compiler.Compile();

            var ns = model.Namespaces.Find(i => i.Name == "ns");
            var qux = ns!.Classes.FirstOrDefault(i => i.Name == "Qux") as IClass;
            var ifoo = ns.Interfaces.FirstOrDefault(i => i.Name == "IFoo") as IInterface;
            var iqux = model.ResolveType("ns.IQux<u8>") as IInterface;
            var ibar = model.ResolveType("ns.IBar<u8>") as IInterface;

            Assert.True(qux!.CanImplicitlyCastTo(qux!));
            Assert.True(qux!.CanImplicitlyCastTo(iqux!));
            Assert.True(qux!.CanImplicitlyCastTo(ibar!));
            Assert.True(qux!.CanImplicitlyCastTo(ifoo!));
            Assert.False(iqux!.CanImplicitlyCastTo(qux!));
            Assert.True(iqux!.CanImplicitlyCastTo(iqux!));
            Assert.True(iqux!.CanImplicitlyCastTo(ibar!));
            Assert.True(iqux!.CanImplicitlyCastTo(ifoo!));
            Assert.False(ibar!.CanImplicitlyCastTo(qux!));
            Assert.False(ibar!.CanImplicitlyCastTo(iqux!));
            Assert.True(ibar!.CanImplicitlyCastTo(ibar!));
            Assert.True(ibar!.CanImplicitlyCastTo(ifoo!));
            Assert.False(ifoo!.CanImplicitlyCastTo(qux!));
            Assert.False(ifoo!.CanImplicitlyCastTo(iqux!));
            Assert.False(ifoo!.CanImplicitlyCastTo(ibar!));
            Assert.True(ifoo!.CanImplicitlyCastTo(ifoo!));
        }

        [Fact]
        public void SelfTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                namespace ns {
                    interface IFoo {
                        fun a() -> Self {}
                    }
                    class Foo {
                        fun a() -> Self {}
                        impl IFoo {
                            fun a() -> IFoo {
                                val b: Self; 
                            } 
                        }
                    }
                }
            ");
            var model = compiler.Compile();

            var ifoo_a = model.ResolveSymbol("ns.IFoo.a") as FunctionSymbol;
            Assert.Equal("ns.IFoo", ifoo_a!.ReturnTypeInfo.FullName);
            var foo_a = model.ResolveSymbol("ns.Foo.a") as FunctionSymbol;
            Assert.Equal("ns.Foo", foo_a!.ReturnTypeInfo.FullName);
            var vtable_foo = model.Classes.First(i => i.Name == "Foo")!.VTables.First();
            var f = vtable_foo.Functions.First()!;
            Assert.Equal("ns.Foo", model.ResolveShortSymbol("b", scope: f)!.TypeInfo.FullName);
        }
    }
}
