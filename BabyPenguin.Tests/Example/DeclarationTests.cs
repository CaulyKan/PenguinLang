using System.Diagnostics.SymbolStore;
using BabyPenguin;
using BabyPenguin.Semantic;

namespace PenguinLangAntlr.Tests;

public class HelloWorldTest
{
    [Fact]
    public void GlobalDeclare()
    {
        var compiler = new SemanticCompiler();
        compiler.AddSource(@"
            val test1 : string = "" "";
            val test2 : u8 = 1;
            val test3 : i32 = 1;
            val test4 : bool = true;
            val test5 : float = 3.14159;
        ");
        var model = compiler.Compile();
        Assert.Equal(5, model.Namespaces[0].Symbols.Count);
        Assert.True(model.Namespaces[0].Symbols[0].Type.IsStringType);
        Assert.Equal("test1", model.Namespaces[0].Symbols[0].Name);
        Assert.True(model.Namespaces[0].Symbols[1].Type.FullName == "u8");
        Assert.Equal("test2", model.Namespaces[0].Symbols[1].Name);
        Assert.True(model.Namespaces[0].Symbols[2].Type.FullName == "i32");
        Assert.Equal("test3", model.Namespaces[0].Symbols[2].Name);
        Assert.True(model.Namespaces[0].Symbols[3].Type.IsBoolType);
        Assert.Equal("test4", model.Namespaces[0].Symbols[3].Name);
        Assert.True(model.Namespaces[0].Symbols[4].Type.IsFloatType);
        Assert.Equal("test5", model.Namespaces[0].Symbols[4].Name);
    }

    [Fact]
    public void NamesapceDeclare()
    {
        var compiler = new SemanticCompiler();
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
        Assert.True(ns.Symbols[0].Type.IsStringType);
        Assert.True(ns.Symbols[0].FullName == "Test.test1");
        Assert.True(ns.Symbols[1].Type.FullName == "u8");
        Assert.True(ns.Symbols[1].FullName == "Test.test2");
        Assert.True(ns.Symbols[2].Type.FullName == "i32");
        Assert.True(ns.Symbols[2].FullName == "Test.test3");
        Assert.True(ns.Symbols[3].Type.IsBoolType);
        Assert.True(ns.Symbols[3].FullName == "Test.test4");
        Assert.True(ns.Symbols[4].Type.IsFloatType);
        Assert.True(ns.Symbols[4].FullName == "Test.test5");
    }

    [Fact]
    public void ClassDeclare()
    {
        var compiler = new SemanticCompiler();
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
        var compiler = new SemanticCompiler();
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
        Assert.True(((FunctionSymbol)model.Namespaces[0].Symbols[0]).ReturnType.IsStringType);
        Assert.True(((FunctionSymbol)model.Namespaces[0].Symbols[0]).Parameters.Count == 0);
        Assert.Single(model.Namespaces[1].Symbols);
        Assert.Single(model.Namespaces[1].Functions);
        Assert.Equal("Test.test1", model.Namespaces[1].Symbols[0].FullName);
        Assert.True(model.Namespaces[1].Symbols[0] is FunctionSymbol);
        Assert.True(((FunctionSymbol)model.Namespaces[1].Symbols[0]).ReturnType.IsVoidType);
        Assert.True(((FunctionSymbol)model.Namespaces[1].Symbols[0]).Parameters.Count == 0);
    }

    [Fact]
    public void InitialDeclare()
    {
        var compiler = new SemanticCompiler();
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
        var compiler = new SemanticCompiler();
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
        var symbols = model.Namespaces[0].InitialRoutines[0].Symbols;
        Assert.Equal(5, symbols.Count);
        Assert.Equal("test1", symbols[0].Name);
        Assert.True(symbols[0].Type.IsStringType);
        Assert.True(symbols[0].IsLocal);
        Assert.True(model.Symbols[1].Type.FullName == "u8");
        Assert.Equal("test2", model.Symbols[1].Name);
        Assert.True(model.Symbols[2].Type.FullName == "i32");
        Assert.Equal("test3", model.Symbols[2].Name);
        Assert.True(model.Symbols[3].Type.IsBoolType);
        Assert.Equal("test4", model.Symbols[3].Name);
        Assert.True(model.Symbols[4].Type.IsFloatType);
        Assert.Equal("test5", model.Symbols[4].Name);
    }
}
