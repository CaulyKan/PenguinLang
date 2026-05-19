namespace EmperorPenguin.Tests;

public class BoundTreeConstructorTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeConstructorTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class MyClass {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some()) {
            println(""has_ctor"");
            let ctor = cls.constructor.some;
            println(""ctor_name="" + ctor.name);
            println(""ctor_is_new="" + cast<string>(ctor.is_new));
        }
    }
}
", "has_ctor\nctor_name=new\nctor_is_new=true")]
    public void ImplicitConstructorTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Point {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some()) {
            let ctor = cls.constructor.some;
            if (cast<i64>(ctor.parameters.size()) >= 1) {
                let p = ctor.parameters.at(cast<u64>(0)).some;
                println(""param_name="" + p.name);
                println(""is_this="" + cast<string>(p.is_this));
                println(""is_mutable="" + cast<string>(p.is_mutable));
            }
        }
    }
}
", "param_name=this\nis_this=true\nis_mutable=true")]
    public void ImplicitConstructorParameterTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Foo {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some()) {
            let ctor = cls.constructor.some;
            println(""ret_type="" + ctor.return_type.display_name());
        }
    }
}
", "ret_type=void")]
    public void ImplicitConstructorReturnTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Bar {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.scope.is_some()) {
            let ctor_sym = cls.scope.some.lookup_symbol(""new"");
            if (ctor_sym.is_some()) {
                println(""ctor_sym_name="" + ctor_sym.some.get_name());
                if (ctor_sym.some is bound.BoundSymbol.function_sym) {
                    println(""is_function_sym"");
                }
            }
        }
    }
}
", "ctor_sym_name=new\nis_function_sym")]
    public void ImplicitConstructorSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Widget {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some() && cls.constructor.some.symbol.is_some()) {
            let sym = cls.constructor.some.symbol.some;
            println(""sym_type="" + sym.bound_type.display_name());
        }
    }
}
", "sym_type=fun<void, mut Widget>")]
    public void ImplicitConstructorFunctionTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class MyClass {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        println(""ctor_count="" + cast<string>(cast<i64>(cls.constructors.size())));
    }
}
", "ctor_count=1")]
    public void ConstructorsListTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Person { fun new(mut this, name: string) {} }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some()) {
            let ctor = cls.constructor.some;
            println(""has_ctor"");
            println(""ctor_name="" + ctor.name);
            println(""ctor_is_new="" + cast<string>(ctor.is_new));
            println(""param_count="" + cast<string>(cast<i64>(ctor.parameters.size())));
        }
    }
}
", "has_ctor\nctor_name=new\nctor_is_new=true\nparam_count=2")]
    public void ExplicitConstructorTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace App { class Item {} }"");
    let ns = result.definitions.at(cast<u64>(0)).some;
    if (ns is bound.BoundDefinition.namespace_def) {
        let child = ns.namespace_def.children.at(cast<u64>(0)).some;
        if (child is bound.BoundDefinition.class_def) {
            let cls = child.class_def;
            if (cls.constructor.is_some()) {
                println(""has_ctor"");
                let ctor = cls.constructor.some;
                println(""ctor_full="" + ctor.full_name);
            }
        }
    }
}
", "has_ctor\nctor_full=<global>.App.Item.new")]
    public void ImplicitConstructorInNamespaceTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Data {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.constructor.is_some() && cls.constructor.some.symbol.is_some()) {
            let sym = cls.constructor.some.symbol.some;
            if (sym.bound_type.kind is bound.TypeKind.FunctionKind) {
                println(""is_func_type"");
            }
            println(""is_new="" + cast<string>(sym.is_new));
        }
    }
}
", "is_func_type\nis_new=true")]
    public void ConstructorWithSymbolBoundTypeTest() => _batch.Value.Assert();
}
