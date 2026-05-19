using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeBuildScopeTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeBuildScopeTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun my_func();"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        println(""func_name="" + func.name);
        println(""func_full="" + func.full_name);
    }
    let func_sym = result.global_scope.lookup_symbol(""my_func"");
    if (func_sym.is_some()) {
        println(""func_sym="" + func_sym.some.get_name());
    }
}
", "func_name=my_func\nfunc_full=<global>.my_func\nfunc_sym=my_func")]
    public void BindFunctionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class MyClass {}"");
    if (cast<i64>(result.definitions.size()) == 1) {
        let def = result.definitions.at(cast<u64>(0)).some;
        println(""def_name="" + def.get_name());
        if (def is bound.BoundDefinition.class_def) {
            println(""is_class"");
        }
    }
}
", "def_name=MyClass\nis_class")]
    public void BindClassBasicTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Color { Red; Green; Blue; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.enum_def) {
        let e = def.enum_def;
        println(""enum_name="" + e.name);
        println(""member_count="" + cast<string>(cast<i64>(e.members.size())));
        if (cast<i64>(e.members.size()) > 0) {
            println(""first_member="" + e.members.at(cast<u64>(0)).some.name);
        }
    }
}
", "enum_name=Color\nmember_count=3\nfirst_member=Red")]
    public void BindEnumBasicTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun func_a(); fun func_b();"");
    println(""def_count="" + cast<string>(cast<i64>(result.definitions.size())));
}
", "def_count=2")]
    public void BindMultipleDefinitionsTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""extern fun external_func();"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        println(""name="" + func.name);
        println(""extern="" + cast<string>(func.is_extern));
        println(""static="" + cast<string>(func.is_static));
    }
}
", "name=external_func\nextern=true\nstatic=true")]
    public void BindExternFunctionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Person { name; fun greet(); }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        println(""field_count="" + cast<string>(cast<i64>(cls.fields.size())));
        println(""method_count="" + cast<string>(cast<i64>(cls.methods.size())));
    }
}
", "field_count=1\nmethod_count=1")]
    public void BindClassWithFieldAndMethodTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""pure fun pure_func();"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        println(""pure="" + cast<string>(func.is_pure));
    }
}
", "pure=true")]
    public void BindPureFunctionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""type MyInt = i64; initial {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.type_ref_def) {
        println(""type_ref_name="" + def.type_ref_def.name);
    }
}
", "type_ref_name=MyInt")]
    public void BindTypeRefTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class MyClass {}"");
    let type_sym = result.global_scope.lookup_type_in_scope(""MyClass"");
    if (type_sym.is_some()) {
        println(""type_found="" + type_sym.some.get_name());
    }
}
", "type_found=MyClass")]
    public void BindClassScopeLookupTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Color { Red; }"");
    let type_sym = result.global_scope.lookup_type_in_scope(""Color"");
    if (type_sym.is_some()) {
        println(""type_found="" + type_sym.some.get_name());
    }
}
", "type_found=Color")]
    public void BindEnumScopeLookupTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""interface IPrintable { fun print(); }"");
    let type_sym = result.global_scope.lookup_type_in_scope(""IPrintable"");
    if (type_sym.is_some()) {
        println(""type_found="" + type_sym.some.get_name());
    }
}
", "type_found=IPrintable")]
    public void BindInterfaceScopeLookupTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace MyApp { class Widget {} }"");
    let ns_bound = result.definitions.at(cast<u64>(0)).some;
    if (ns_bound is bound.BoundDefinition.namespace_def) {
        let ns = ns_bound.namespace_def;
        println(""ns_name="" + ns.name);
        if (cast<i64>(ns.children.size()) == 1) {
            let child = ns.children.at(cast<u64>(0)).some;
            if (child is bound.BoundDefinition.class_def) {
                println(""child_class="" + child.class_def.name);
            }
        }
    }
}
", "ns_name=MyApp\nchild_class=Widget")]
    public void BindNamespaceWithClassTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace Utils { fun helper_a(); } namespace Utils { fun helper_b(); }"");
    let ns_scope = result.global_scope.lookup_namespace(""Utils"");
    if (ns_scope.is_some()) {
        let sym_a = ns_scope.some.lookup_symbol(""helper_a"");
        let sym_b = ns_scope.some.lookup_symbol(""helper_b"");
        if (sym_a.is_some() && sym_b.is_some()) {
            println(""both_merged"");
        }
    }
}
", "both_merged")]
    public void BindNamespaceScopeMergeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""initial {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.initial_routine) {
        let init = def.initial_routine;
        println(""init_full="" + init.full_name);
        if (init.scope.is_some()) {
            println(""has_scope"");
        }
        if (init.symbol.is_some()) {
            println(""has_symbol"");
        }
    }
}
", "init_full=<global>.<initial>\nhas_scope\nhas_symbol")]
    public void BindInitialRoutineTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Foo {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cls.bound_type.kind is bound.TypeKind.ClassKind) {
            println(""type_kind=class"");
        }
        if (cls.type_symbol.is_some()) {
            println(""has_type_sym"");
        }
    }
}
", "type_kind=class\nhas_type_sym")]
    public void BindClassBoundTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Status { Ok; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.enum_def) {
        let e = def.enum_def;
        if (e.bound_type.kind is bound.TypeKind.EnumKind) {
            println(""type_kind=enum"");
        }
        if (cast<i64>(e.members.size()) == 1) {
            let member = e.members.at(cast<u64>(0)).some;
            println(""member_val="" + cast<string>(member.value));
        }
    }
}
", "type_kind=enum\nmember_val=0")]
    public void BindEnumBoundTypeTest() => _batch.Value.Assert();
}
