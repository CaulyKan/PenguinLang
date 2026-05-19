using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeResolveTypeTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeResolveTypeTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun get_int() -> i32 {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let f = def.function_def;
        println(""ret_type="" + f.return_type.display_name());
    }
}
", "ret_type=i32")]
    public void FunctionReturnTypePrimitiveTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun add(a: i32, b: i64) -> i32 {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let f = def.function_def;
        println(""param_count="" + cast<string>(cast<i64>(f.parameters.size())));
        if (cast<i64>(f.parameters.size()) >= 2) {
            let bp0 = f.parameters.at(cast<u64>(0)).some;
            let bp1 = f.parameters.at(cast<u64>(1)).some;
            println(""p0_name="" + bp0.name);
            println(""p0_type="" + bp0.bound_type.display_name());
            println(""p1_name="" + bp1.name);
            println(""p1_type="" + bp1.bound_type.display_name());
        }
    }
}
", "param_count=2\np0_name=a\np0_type=i32\np1_name=b\np1_type=i64")]
    public void FunctionWithParametersTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun compute(x: f64) -> bool {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let f = def.function_def;
        if (f.symbol.is_some()) {
            println(""sym_type="" + f.symbol.some.bound_type.display_name());
        }
    }
}
", "sym_type=fun<bool, f64>")]
    public void FunctionSymbolBoundTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Point { x: i32; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let c = def.class_def;
        if (cast<i64>(c.fields.size()) == 1) {
            let fd = c.fields.at(cast<u64>(0)).some;
            if (fd is bound.BoundDefinition.class_field) {
                println(""field_type="" + fd.class_field.bound_type.display_name());
            }
        }
    }
}
", "field_type=i32")]
    public void ClassFieldPrimitiveTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun do_thing();"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let f = def.function_def;
        println(""ret_type="" + f.return_type.display_name());
    }
}
", "ret_type=void")]
    public void FunctionVoidReturnTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Status { Active; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.enum_def) {
        let e = def.enum_def;
        if (cast<i64>(e.members.size()) == 1) {
            let member = e.members.at(cast<u64>(0)).some;
            println(""member_type="" + member.member_type.display_name());
        }
    }
}
", "member_type=Status")]
    public void EnumMemberTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""initial {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.initial_routine) {
        let init = def.initial_routine;
        if (init.symbol.is_some()) {
            println(""sym_type="" + init.symbol.some.bound_type.display_name());
        }
    }
}
", "sym_type=fun<void>")]
    public void InitialRoutineFunctionTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Widget {} fun make_widget() -> Widget {}"");
    let def = result.definitions.at(cast<u64>(1)).some;
    if (def is bound.BoundDefinition.function_def) {
        let f = def.function_def;
        println(""ret_type="" + f.return_type.display_name());
        if (f.return_type.kind is bound.TypeKind.ClassKind) {
            println(""is_class_type"");
        }
    }
}
", "ret_type=Widget\nis_class_type")]
    public void ResolveTypeUserClassTest() => _batch.Value.Assert();
}
