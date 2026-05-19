using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeBindSymbolTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeBindSymbolTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Person { name: string; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        let field_sym = cls.scope.some.lookup_symbol(""name"");
        if (field_sym.is_some()) {
            let sym = field_sym.some;
            println(""sym_name="" + sym.get_name());
            println(""sym_full="" + sym.get_full_name());
            if (sym is bound.BoundSymbol.variable) {
                println(""is_field="" + cast<string>(sym.variable.variable_kind is bound.VariableSymbolKind.Field));
            }
        }
    }
}
", "sym_name=name\nsym_full=<global>.Person.name\nis_field=true")]
    public void ClassFieldSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class MyClass { value: i32; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        if (cast<i64>(cls.fields.size()) == 1) {
            let f = cls.fields.at(cast<u64>(0)).some;
            if (f is bound.BoundDefinition.class_field) {
                if (f.class_field.field_symbol.is_some()) {
                    let sym = f.class_field.field_symbol.some;
                    if (sym.bound_type.kind is bound.TypeKind.PrimitiveKind) {
                        println(""field_type_resolved"");
                    }
                }
            }
        }
    }
}
", "field_type_resolved")]
    public void ClassFieldBoundTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun add(a: i32, b: i32) {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.scope.is_some()) {
            let sym_a = func.scope.some.lookup_symbol(""a"");
            let sym_b = func.scope.some.lookup_symbol(""b"");
            if (sym_a.is_some() && sym_b.is_some()) {
                if (sym_a.some is bound.BoundSymbol.variable) {
                    let va = sym_a.some.variable;
                    println(""param_name="" + va.name);
                    println(""param_kind="" + cast<string>(va.variable_kind is bound.VariableSymbolKind.Parameter));
                    println(""param_index="" + cast<string>(va.parameter_index));
                }
            }
        }
    }
}
", "param_name=a\nparam_kind=true\nparam_index=0")]
    public void FunctionParameterSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Color { Red; Blue; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.enum_def) {
        let e = def.enum_def;
        let red_sym = e.scope.some.lookup_symbol(""Red"");
        let blue_sym = e.scope.some.lookup_symbol(""Blue"");
        if (red_sym.is_some() && blue_sym.is_some()) {
            if (red_sym.some is bound.BoundSymbol.enum_member) {
                let rs = red_sym.some.enum_member;
                println(""member_name="" + rs.name);
                println(""member_val="" + cast<string>(rs.enum_value));
            }
            if (blue_sym.some is bound.BoundSymbol.enum_member) {
                let bs = blue_sym.some.enum_member;
                println(""blue_val="" + cast<string>(bs.enum_value));
            }
        }
    }
}
", "member_name=Red\nmember_val=0\nblue_val=1")]
    public void EnumMemberSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Point { x: i64; y: i64; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.class_def) {
        let cls = def.class_def;
        let x_sym = cls.scope.some.lookup_symbol(""x"");
        let y_sym = cls.scope.some.lookup_symbol(""y"");
        if (x_sym.is_some() && y_sym.is_some()) {
            println(""both_fields_found"");
        }
    }
}
", "both_fields_found")]
    public void ClassWithMultipleFieldsTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace App { class Widget { id: i32; } }"");
    let ns = result.definitions.at(cast<u64>(0)).some;
    if (ns is bound.BoundDefinition.namespace_def) {
        let child = ns.namespace_def.children.at(cast<u64>(0)).some;
        if (child is bound.BoundDefinition.class_def) {
            let cls = child.class_def;
            let id_sym = cls.scope.some.lookup_symbol(""id"");
            if (id_sym.is_some()) {
                println(""field_found="" + id_sym.some.get_name());
                println(""field_full="" + id_sym.some.get_full_name());
            }
        }
    }
}
", "field_found=id\nfield_full=<global>.App.Widget.id")]
    public void NestedClassFieldSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun compute(x: i64, y: f64) {}"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.scope.is_some()) {
            let scope = func.scope.some;
            let x_sym = scope.lookup_symbol(""x"");
            let y_sym = scope.lookup_symbol(""y"");
            if (x_sym.is_some() && y_sym.is_some()) {
                println(""params_found"");
            }
        }
    }
}
", "params_found")]
    public void FunctionParameterCountTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""enum Status { Active; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.enum_def) {
        let e = def.enum_def;
        let sym = e.scope.some.lookup_symbol(""Active"");
        if (sym.is_some()) {
            if (sym.some is bound.BoundSymbol.enum_member) {
                let es = sym.some.enum_member;
                if (es.bound_type.kind is bound.TypeKind.EnumKind) {
                    println(""member_has_enum_type"");
                }
            }
        }
    }
}
", "member_has_enum_type")]
    public void EnumMemberBoundTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let source = ""fun callee(x: i64) -> i64 { return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);

    let definitions = result.definitions;
    if (definitions.size() > 0) {
        let def = definitions.at(cast<u64>(0)).some;
        if (def is bound.BoundDefinition.function_def) {
            let func = def.function_def;
            if (func.body.is_some()) {
                let body = func.body.some;
                if (body is bound.BoundExpression.code_block) {
                    let cb = body.code_block;
                    if (cb.statements.size() > 0) {
                        let stmt = cb.statements.at(cast<u64>(0)).some;
                        if (stmt is bound.BoundStatement.return_stmt) {
                            let ret = stmt.return_stmt;
                            if (ret.value.is_some()) {
                                let val = ret.value.some;
                                if (val is bound.BoundExpression.identifier) {
                                    let id_expr = val.identifier;
                                    if (id_expr.symbol.is_some()) {
                                        let sym = id_expr.symbol.some;
                                        println(""symbol full_name: "" + sym.get_full_name());
                                        println(""func full_name: "" + func.full_name);
                                    } else {
                                        println(""symbol is none"");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
", @"symbol full_name: <global>.callee.<body>.x
func full_name: <global>.callee")]
    public void TestSymbolFullName() => _batch.Value.Assert();
}
