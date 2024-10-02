using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using Xunit.Abstractions;

namespace BabyPenguin.Tests
{
    public class HelloWorldTest(ITestOutputHelper helper) : TestBase(helper)
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
            Assert.Equal(5, model.Namespaces[0].Symbols.Count);
            Assert.True(model.Namespaces[0].Symbols[0].TypeInfo.IsStringType);
            Assert.Equal("test1", model.Namespaces[0].Symbols[0].Name);
            Assert.True(model.Namespaces[0].Symbols[1].TypeInfo.FullName == "u8");
            Assert.Equal("test2", model.Namespaces[0].Symbols[1].Name);
            Assert.True(model.Namespaces[0].Symbols[2].TypeInfo.FullName == "i32");
            Assert.Equal("test3", model.Namespaces[0].Symbols[2].Name);
            Assert.True(model.Namespaces[0].Symbols[3].TypeInfo.IsBoolType);
            Assert.Equal("test4", model.Namespaces[0].Symbols[3].Name);
            Assert.True(model.Namespaces[0].Symbols[4].TypeInfo.IsFloatType);
            Assert.Equal("test5", model.Namespaces[0].Symbols[4].Name);
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
            Assert.Equal(5, ns!.Symbols.Count);
            Assert.True(ns.Symbols[0].TypeInfo.IsStringType);
            Assert.True(ns.Symbols[0].FullName == "Test.test1");
            Assert.True(ns.Symbols[1].TypeInfo.FullName == "u8");
            Assert.True(ns.Symbols[1].FullName == "Test.test2");
            Assert.True(ns.Symbols[2].TypeInfo.FullName == "i32");
            Assert.True(ns.Symbols[2].FullName == "Test.test3");
            Assert.True(ns.Symbols[3].TypeInfo.IsBoolType);
            Assert.True(ns.Symbols[3].FullName == "Test.test4");
            Assert.True(ns.Symbols[4].TypeInfo.IsFloatType);
            Assert.True(ns.Symbols[4].FullName == "Test.test5");
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
            Assert.Equal(2, model.Namespaces.Count);
            Assert.Single(model.Namespaces.First().Classes);
            Assert.Equal("TestClass", model.Namespaces.First().Classes[0].Name);
            Assert.Equal(2, model.Namespaces[1].Classes.Count);
            Assert.Equal("Test.TestClass", (model.Namespaces[1].Classes[0] as ISemanticScope).FullName);
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
            Assert.Single(model.Namespaces[0].Symbols);
            Assert.Single(model.Namespaces[0].Functions);
            Assert.Equal("test1", model.Namespaces[0].Symbols[0].Name);
            Assert.True(model.Namespaces[0].Symbols[0] is FunctionSymbol);
            Assert.True(((FunctionSymbol)model.Namespaces[0].Symbols[0]).ReturnTypeInfo.IsStringType);
            Assert.True(((FunctionSymbol)model.Namespaces[0].Symbols[0]).Parameters.Count == 0);
            Assert.Single(model.Namespaces[1].Symbols);
            Assert.Single(model.Namespaces[1].Functions);
            Assert.Equal("Test.test1", model.Namespaces[1].Symbols[0].FullName);
            Assert.True(model.Namespaces[1].Symbols[0] is FunctionSymbol);
            Assert.True(((FunctionSymbol)model.Namespaces[1].Symbols[0]).ReturnTypeInfo.IsVoidType);
            Assert.True(((FunctionSymbol)model.Namespaces[1].Symbols[0]).Parameters.Count == 0);
        }

        [Fact]
        public void InitialDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {

                }
                namespace Test {
                initial {}
                }
            ");
            var model = compiler.Compile();

            Assert.Single(model.Namespaces[0].InitialRoutines);
            Assert.Single(model.Namespaces[1].InitialRoutines);
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

            Assert.Single(model.Namespaces[0].InitialRoutines);
            var symbols = model.Namespaces[0].InitialRoutines[0].Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(5, symbols.Count);
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

            Assert.Single(model.Namespaces[0].Functions);
            var symbols = model.Namespaces[0].Functions[0].Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(7, symbols.Count);
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

            Assert.Single(model.Namespaces[0].InitialRoutines);
            var symbols = model.Namespaces[0].InitialRoutines[0].Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(3, symbols.Count);
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
        public void DuplicateClassDeclare()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class A {}
                class A {}
            ");
            Assert.Throws<PenguinLangException>(compiler.Compile);
        }

        [Fact]
        public void DuplicateFunctionParam()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun test(val param1: u64, var param1: char){}
            ");
            Assert.Throws<PenguinLangException>(compiler.Compile);
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

            Assert.Single(model.Namespaces[0].Classes);
            var symbols = model.Namespaces[0].Classes[0].Symbols.Where(x => !x.IsTemp).ToList();
            Assert.Equal(5, symbols.Count);
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
    }
}
