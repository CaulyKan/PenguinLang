using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundMetaTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundMetaTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#fun fib(n: u32) -> u32 { 0 }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    println(""d0_type="" + def.get_name());
    if (def is emperor.BoundDefinition.meta_function_def) {
        let mf = def.meta_function_def;
        println(""name="" + mf.name);
        println(""param_count="" + cast<string>(cast<i64>(mf.parameters.size())));
        if (mf.symbol.is_some()) {
            let sym = mf.symbol.some;
            println(""is_meta="" + cast<string>(sym.is_meta));
        }
    }
    let found = result.global_scope.lookup_symbol_anywhere(""fib"");
    println(""symbol_found="" + cast<string>(found.is_some()));
}
", "d0_type=fib\nname=fib\nparam_count=1\nis_meta=true\nsymbol_found=true")]
    public void BindMetaFunction() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#if (true) { fun a(); } fun b();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_name="" + d1.get_name());
}
", "count=2\nd0_name=a\nd1_name=b")]
    public void BindMetaIfTopLevel() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#define(\""DEBUG\"",\""1\""); #if (#defined(\""DEBUG\"")) { fun a(); } fun b();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_name="" + d1.get_name());
}
", "count=2\nd0_name=a\nd1_name=b")]
    public void BindMetaDefineIfInclude() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#if (#defined(\""RELEASE\"")) { fun a(); } fun b();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
}
", "count=1\nd0_name=b")]
    public void BindMetaIfExclude() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#if (false) { fun a(); } #else { fun c(); } fun b();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_name="" + d1.get_name());
}
", "count=2\nd0_name=c\nd1_name=b")]
    public void BindMetaIfElse() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#if (true) { #if (false) { fun a(); } fun b(); } fun c();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_name="" + d1.get_name());
}
", "count=2\nd0_name=b\nd1_name=c")]
    public void BindMetaIfNested() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#define(\""X\"",\""1\""); #if (#defined(\""X\"") || #defined(\""Y\"")) { fun a(); } fun b();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
}
", "count=2\nd0_name=a")]
    public void BindMetaIfLogicOr() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""fun f() -> i64 { #if (true) { return 1; } return 0; }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.function_def) {
        let fn = d0.function_def;
        if (fn.body.is_some()) {
            let body = fn.body.some;
            if (body is emperor.BoundExpression.code_block) {
                let stmts = body.code_block.statements;
                println(""count="" + cast<string>(cast<i64>(stmts.size())));
                let s0 = stmts.at(cast<u64>(0)).some;
                println(""s0_return="" + cast<string>(s0 is emperor.BoundStatement.return_stmt));
            }
        }
    }
}
", "count=2\ns0_return=true")]
    public void BindMetaIfInBodyTrue() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""fun f() -> i64 { #if (false) { return 1; } return 0; }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.function_def) {
        let fn = d0.function_def;
        if (fn.body.is_some()) {
            let body = fn.body.some;
            if (body is emperor.BoundExpression.code_block) {
                let stmts = body.code_block.statements;
                println(""count="" + cast<string>(cast<i64>(stmts.size())));
                let s0 = stmts.at(cast<u64>(0)).some;
                println(""s0_return="" + cast<string>(s0 is emperor.BoundStatement.return_stmt));
            }
        }
    }
}
", "count=1\ns0_return=true")]
    public void BindMetaIfInBodyFalse() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#define(\""DEBUG\"",\""1\""); fun f() -> i64 { #if (#defined(\""DEBUG\"")) { return 1; } return 0; }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.function_def) {
        let fn = d0.function_def;
        if (fn.body.is_some()) {
            let body = fn.body.some;
            if (body is emperor.BoundExpression.code_block) {
                let stmts = body.code_block.statements;
                println(""count="" + cast<string>(cast<i64>(stmts.size())));
                let s0 = stmts.at(cast<u64>(0)).some;
                println(""s0_return="" + cast<string>(s0 is emperor.BoundStatement.return_stmt));
            }
        }
    }
}
", "count=2\ns0_return=true")]
    public void BindMetaIfInBodyDefine() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""fun f() -> i64 { #if (true) { #if (false) { return 1; } return 2; } return 0; }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.function_def) {
        let fn = d0.function_def;
        if (fn.body.is_some()) {
            let body = fn.body.some;
            if (body is emperor.BoundExpression.code_block) {
                let stmts = body.code_block.statements;
                println(""count="" + cast<string>(cast<i64>(stmts.size())));
                let s0 = stmts.at(cast<u64>(0)).some;
                println(""s0_return="" + cast<string>(s0 is emperor.BoundStatement.return_stmt));
            }
        }
    }
}
", "count=2\ns0_return=true")]
    public void BindMetaIfInBodyNested() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace ns { #if (true) { fun a(); } fun b(); }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.namespace_def) {
        let ns = d0.namespace_def;
        println(""ns_count="" + cast<string>(cast<i64>(ns.children.size())));
    }
}
", "ns_count=2")]
    public void BindMetaIfInNamespace() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""class C { fun f() -> i64 { #if (true) { return 1; } return 0; } }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.class_def) {
        let c = d0.class_def;
        println(""methods="" + cast<string>(cast<i64>(c.methods.size())));
        if (cast<i64>(c.methods.size()) > 0) {
            let m = c.methods.at(cast<u64>(0)).some;
            if (m is emperor.BoundDefinition.function_def) {
                let fn = m.function_def;
                if (fn.body.is_some()) {
                    let body = fn.body.some;
                    if (body is emperor.BoundExpression.code_block) {
                        println(""count="" + cast<string>(cast<i64>(body.code_block.statements.size())));
                    }
                }
            }
        }
    }
}
", "methods=1\ncount=2")]
    public void BindMetaIfInClassMethod() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#for (let i in 0..3) { fun a(); } #while (false) { fun b(); } fun c();"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_type="" + d0.get_name());
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_name="" + d1.get_name());
}
", "count=2\nd0_type=<meta_for>\nd1_name=c")]
    public void BindMetaForWhileTopLevel() => _batch.Value.Assert();

    [Fact]
    // Unknown def-position #name: E_RESOLVE_SYMBOL, call + trailing def dropped,
    // remaining defs still bound (was: silent passthrough of a bound meta_call_def).
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#derive_clone(T); fun after();"");
    println(""errors="" + cast<string>(cast<i64>(result.errors.size())));
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
}
", "errors=1\ncount=1\nd0_name=after")]
    public void BindMetaCallTopLevel() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#derive_clone(10); fun x();"");
    println(""errors="" + cast<string>(cast<i64>(result.errors.size())));
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_name="" + d0.get_name());
}
", "errors=1\ncount=1\nd0_name=x")]
    public void BindMetaCallUnknownDefPositionErrors() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""fun f() -> void { let x = #foo(1); }"");
    println(""errors="" + cast<string>(cast<i64>(result.errors.size())));
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is emperor.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is emperor.BoundExpression.code_block) {
                let stmts = body.code_block.statements;
                let s0 = stmts.at(cast<u64>(0)).some;
                if (s0 is emperor.BoundStatement.let_decl) {
                    let let_stmt = s0.let_decl;
                    println(""has_init="" + cast<string>(let_stmt.initializer.is_some()));
                }
            }
        }
    }
}
", "errors=1\nhas_init=false")]
    public void BindMetaCallInFunctionBody() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#fun g(t: u32) -> u32 { 0 }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is emperor.BoundDefinition.meta_function_def) {
        let mf = def.meta_function_def;
        println(""name="" + mf.name);
        println(""has_return_type="" + cast<string>(mf.return_type.is_some()));
        println(""has_body="" + cast<string>(mf.body.is_some()));
        let param0 = mf.parameters.at(cast<u64>(0)).some;
        println(""param0_name="" + param0.name);
        println(""param0_kind="" + param0.kind);
    }
}
", "name=g\nhas_return_type=true\nhas_body=true\nparam0_name=t\nparam0_kind=i64")]
    public void BindMetaFunctionSignaturePreserved() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#fun m() -> u32; fun normal(); class C {}"");
    println(""count="" + cast<string>(cast<i64>(result.definitions.size())));
    let d0 = result.definitions.at(cast<u64>(0)).some;
    println(""d0_meta="" + cast<string>(d0 is emperor.BoundDefinition.meta_function_def));
    let d1 = result.definitions.at(cast<u64>(1)).some;
    println(""d1_normal="" + cast<string>(d1 is emperor.BoundDefinition.function_def));
    let d2 = result.definitions.at(cast<u64>(2)).some;
    println(""d2_class="" + cast<string>(d2 is emperor.BoundDefinition.class_def));
}
", "count=3\nd0_meta=true\nd1_normal=true\nd2_class=true")]
    public void BindMetaMixWithNormal() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new emperor.EmperorPenguinCompiler();
    let result = compiler.compile(""#fun g(t: type) -> type { return #typeof(i32); }"");
    let d0 = result.definitions.at(cast<u64>(0)).some;
    if (d0 is emperor.BoundDefinition.meta_function_def) {
        let mf = d0.meta_function_def;
        println(""name="" + mf.name);
        println(""has_body="" + cast<string>(mf.body.is_some()));
        if (mf.body.is_some()) {
            let body = mf.body.some;
            if (body is emperor.Expression.code_block) {
                let s0 = body.code_block.statements.at(cast<u64>(0)).some;
                if (s0 is emperor.Statement.return_stmt) {
                    if (s0.return_stmt.value.is_some()) {
                        let val = s0.return_stmt.value.some;
                        if (val is emperor.Expression.meta_call) {
                            println(""call="" + val.meta_call.func_name);
                            if (cast<i64>(val.meta_call.arguments.size()) == 1) {
                                let a0 = val.meta_call.arguments.at(cast<u64>(0)).some;
                                if (a0 is emperor.Expression.identifier) {
                                    println(""arg="" + a0.identifier.name);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
", "name=g\nhas_body=true\ncall=typeof\narg=i32")]
    public void BindMetaTypeofPrimitiveArg() => _batch.Value.Assert();
}
