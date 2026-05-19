using static EmperorPenguin.Tests.BatchCompiler;

namespace EmperorPenguin.Tests;

/// <summary>
/// Test to check function call expression return type.
/// </summary>
public class BoundFunctionTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundFunctionTest>());
    [Fact]
    [BatchBoundTest(@"
initial {
    let source = ""fun callee(x: i64) -> i64 { return x; } fun caller() -> i64 { return callee(42); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);

    // Check the 'callee' function return type
    let definitions = result.definitions;
    if (definitions.size() > 0) {
        let def0 = definitions.at(cast<u64>(0)).some;
        if (def0 is bound.BoundDefinition.function_def) {
            let callee_func = def0.function_def;
            println(""callee return type kind: "" + (if (callee_func.return_type.kind is bound.TypeKind.PrimitiveKind) { ""Primitive"" } else { ""Other"" }));
            if (callee_func.return_type.kind is bound.TypeKind.PrimitiveKind) {
                if (callee_func.return_type.primitive is bound.PrimitiveType.I64) {
                    println(""callee return type: i64"");
                }
                if (callee_func.return_type.primitive is bound.PrimitiveType.VoidType) {
                    println(""callee return type: void"");
                }
            }
        }
    }
}
", @"callee return type kind: Primitive
callee return type: i64")]
    public void TestCalleeSymbolType() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let source = ""fun callee(x: i64) -> i64 { return x; } fun caller() -> i64 { return callee(42); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);

    // Get the 'caller' function
    let definitions = result.definitions;
    if (definitions.size() > 1) {
        let caller_def = definitions.at(cast<u64>(1)).some;
        if (caller_def is bound.BoundDefinition.function_def) {
            let caller_func = caller_def.function_def;
            if (caller_func.body.is_some()) {
                let body = caller_func.body.some;
                if (body is bound.BoundExpression.code_block) {
                    let cb = body.code_block;
                    if (cb.statements.size() > 0) {
                        let stmt = cb.statements.at(cast<u64>(0)).some;
                        if (stmt is bound.BoundStatement.return_stmt) {
                            let ret = stmt.return_stmt;
                            if (ret.value.is_some()) {
                                let val = ret.value.some;
                                println(""Return value type kind: "" + (if (val.get_bound_type().kind is bound.TypeKind.PrimitiveKind) { ""Primitive"" } else { ""Other"" }));
                                if (val.get_bound_type().kind is bound.TypeKind.PrimitiveKind) {
                                    if (val.get_bound_type().primitive is bound.PrimitiveType.I64) {
                                        println(""Return value type: i64"");
                                    }
                                    if (val.get_bound_type().primitive is bound.PrimitiveType.VoidType) {
                                        println(""Return value type: void"");
                                    }
                                }
                                if (val is bound.BoundExpression.function_call) {
                                    println(""Return value is function_call"");
                                    let fc = val.function_call;
                                    println(""Function call return type kind: "" + (if (fc.get_bound_type().kind is bound.TypeKind.PrimitiveKind) { ""Primitive"" } else { ""Other"" }));
                                    if (fc.get_bound_type().kind is bound.TypeKind.PrimitiveKind) {
                                        if (fc.get_bound_type().primitive is bound.PrimitiveType.I64) {
                                            println(""Function call return type: i64"");
                                        }
                                        if (fc.get_bound_type().primitive is bound.PrimitiveType.VoidType) {
                                            println(""Function call return type: void"");
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
}
", @"Return value type kind: Primitive
Return value type: i64
Return value is function_call
Function call return type kind: Primitive
Function call return type: i64")]
    public void TestFunctionCallRetType() => _batch.Value.Assert();

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
                println(""body is some"");
                let body = func.body.some;
                if (body is bound.BoundExpression.code_block) {
                    let cb = body.code_block;
                    let stmts = cb.statements;
                    if (stmts.size() > 0) {
                        let stmt = stmts.at(cast<u64>(0)).some;
                        if (stmt is bound.BoundStatement.return_stmt) {
                            let ret = stmt.return_stmt;
                            if (ret.value.is_some()) {
                                println(""return value is some"");
                                let val = ret.value.some;
                                if (val is bound.BoundExpression.identifier) {
                                    println(""return value is identifier"");
                                }
                            } else {
                                println(""return value is none"");
                            }
                        }
                    }
                }
            } else {
                println(""body is none"");
            }
        }
    }
}
", @"body is some
return value is some
return value is identifier")]
    public void TestFunctionBody() => _batch.Value.Assert();

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
            println(""Function full_name: "" + func.full_name);
            println(""Parameters count: "" + cast<string>(cast<i64>(func.parameters.size())));
            if (func.parameters.size() > 0) {
                let p = func.parameters.at(cast<u64>(0)).some;
                println(""Param name: "" + p.name);
                println(""Param type: "" + (if (p.bound_type.kind is bound.TypeKind.PrimitiveKind) { ""Primitive"" } else { ""Other"" }));
            }
        }
    }
}
", @"Function full_name: <global>.callee
Parameters count: 1
Param name: x
Param type: Primitive")]
    public void TestParameterSymbol() => _batch.Value.Assert();
}
