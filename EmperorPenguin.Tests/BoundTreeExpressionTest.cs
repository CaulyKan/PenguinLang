using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeExpressionTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundTreeExpressionTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return 42; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        let ret = stmt.return_stmt;
                        if (ret.value.is_some()) {
                            let val = ret.value.some;
                            if (val is bound.BoundExpression.literal) {
                                println(""lit_type="" + val.literal.get_bound_type().display_name());
                                println(""lit_value="" + val.literal.value);
                            }
                        }
                    }
                }
            }
        }
    }
}
", "lit_type=i64\nlit_value=42")]
    public void IntegerLiteralTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> string { return \""hello\""; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.literal) {
                                println(""lit_type="" + val.literal.get_bound_type().display_name());
                            }
                        }
                    }
                }
            }
        }
    }
}
", "lit_type=string")]
    public void StringLiteralTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> bool { return true; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.literal) {
                                println(""lit_type="" + val.literal.get_bound_type().display_name());
                                println(""lit_kind_bool="" + cast<string>(val.literal.literal_kind is bound.LiteralKind.BoolLiteral));
                                println(""lit_value="" + val.literal.value);
                            }
                        }
                    }
                }
            }
        }
    }
}
", "lit_type=bool\nlit_kind_bool=true\nlit_value=true")]
    public void BoolLiteralTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo(x: i64) -> i64 { return x; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.identifier) {
                                let id = val.identifier;
                                println(""id_type="" + id.get_bound_type().display_name());
                                if (id.symbol.is_some()) {
                                    let sym = id.symbol.some;
                                    println(""sym_name="" + sym.get_name());
                                    if (sym is bound.BoundSymbol.variable) {
                                        println(""is_param="" + cast<string>(sym.variable.variable_kind is bound.VariableSymbolKind.Parameter));
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
", "id_type=i64\nsym_name=x\nis_param=true")]
    public void IdentifierExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return 1 + 2; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.binary) {
                                let bin = val.binary;
                                println(""bin_type="" + bin.get_bound_type().display_name());
                                println(""operand_count="" + cast<string>(cast<i64>(bin.operands.size())));
                                println(""operator_count="" + cast<string>(cast<i64>(bin.operators.size())));
                            }
                        }
                    }
                }
            }
        }
    }
}
", "bin_type=i64\noperand_count=2\noperator_count=1")]
    public void BinaryExpressionTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return -42; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.unary) {
                                let un = val.unary;
                                println(""unary_type="" + un.get_bound_type().display_name());
                                if (un.operand.is_some()) {
                                    let inner = un.operand.some;
                                    if (inner is bound.BoundExpression.literal) {
                                        println(""inner_value="" + inner.literal.value);
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
", "unary_type=i64\ninner_value=42")]
    public void UnaryExpressionTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun bar() {} fun foo() { bar(); }"");
    let foo_def = result.definitions.at(cast<u64>(1)).some;
    if (foo_def is bound.BoundDefinition.function_def) {
        let func = foo_def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.function_call) {
                                let fc = val.function_call;
                                println(""call_type="" + fc.get_bound_type().display_name());
                                if (fc.callee_symbol.is_some()) {
                                    let sym = fc.callee_symbol.some;
                                    println(""callee_name="" + sym.get_name());
                                    if (sym is bound.BoundSymbol.function_sym) {
                                        println(""is_function=true"");
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
", "call_type=void\ncallee_name=bar\nis_function=true")]
    public void FunctionCallExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun add(a: i64, b: i64) -> i64 {} fun foo() { add(1, 2); }"");
    let foo_def = result.definitions.at(cast<u64>(1)).some;
    if (foo_def is bound.BoundDefinition.function_def) {
        let func = foo_def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.function_call) {
                                let fc = val.function_call;
                                println(""arg_count="" + cast<string>(cast<i64>(fc.arguments.size())));
                                if (cast<i64>(fc.arguments.size()) >= 2) {
                                    let arg0 = fc.arguments.at(cast<u64>(0)).some;
                                    let arg1 = fc.arguments.at(cast<u64>(1)).some;
                                    if (arg0 is bound.BoundExpression.literal && arg1 is bound.BoundExpression.literal) {
                                        println(""arg0_val="" + arg0.literal.value);
                                        println(""arg1_val="" + arg1.literal.value);
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
", "arg_count=2\narg0_val=1\narg1_val=2")]
    public void FunctionCallWithArgumentsTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { let x: i64 = 10; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.let_decl) {
                        let let_s = stmt.let_decl;
                        println(""let_type="" + let_s.bound_type.display_name());
                        if (let_s.variable_symbol.is_some()) {
                            println(""var_name="" + let_s.variable_symbol.some.name);
                        }
                        if (let_s.initializer.is_some()) {
                            let init = let_s.initializer.some;
                            if (init is bound.BoundExpression.literal) {
                                println(""init_val="" + init.literal.value);
                            }
                        }
                    }
                }
            }
        }
    }
}
", "let_type=i64\nvar_name=x\ninit_val=10")]
    public void LetDeclarationTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { if (true) {} }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.if_expr) {
                                let if_e = val.if_expr;
                                if (if_e.condition.is_some()) {
                                    let cond = if_e.condition.some;
                                    if (cond is bound.BoundExpression.literal) {
                                        println(""cond_type="" + cond.get_bound_type().display_name());
                                        println(""cond_val="" + cond.literal.value);
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
", "cond_type=bool\ncond_val=true")]
    public void IfStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (false) {} }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.while_expr) {
                                let w = val.while_expr;
                                if (w.condition.is_some()) {
                                    let cond = w.condition.some;
                                    if (cond is bound.BoundExpression.literal) {
                                        println(""cond_type="" + cond.get_bound_type().display_name());
                                        println(""cond_val="" + cond.literal.value);
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
", "cond_type=bool\ncond_val=false")]
    public void WhileStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { let mut x: i64 = 1; x = 2; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) >= 2) {
                    let stmt = cb.statements.at(cast<u64>(1)).some;
                    if (stmt is bound.BoundStatement.assignment) {
                        let assign = stmt.assignment;
                        if (assign.target.is_some()) {
                            let target = assign.target.some;
                            if (target is bound.BoundExpression.identifier) {
                                println(""target_name="" + target.identifier.symbol.some.get_name());
                            }
                        }
                        if (assign.value.is_some()) {
                            let val = assign.value.some;
                            if (val is bound.BoundExpression.literal) {
                                println(""assign_val="" + val.literal.value);
                            }
                        }
                    }
                }
            }
        }
    }
}
", "target_name=x\nassign_val=2")]
    public void AssignmentStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return 42; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        let ret = stmt.return_stmt;
                        println(""ret_type="" + ret.return_type.display_name());
                    }
                }
            }
        }
    }
}
", "ret_type=i64")]
    public void ReturnStatementTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Foo {} fun foo() { new Foo(); }"");
    let foo_def = result.definitions.at(cast<u64>(1)).some;
    if (foo_def is bound.BoundDefinition.function_def) {
        let func = foo_def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.new_expr) {
                                let ne = val.new_expr;
                                println(""new_type="" + ne.get_bound_type().display_name());
                                if (ne.type_symbol.is_some()) {
                                    println(""type_sym_name="" + ne.type_symbol.some.name);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
", "new_type=Foo\ntype_sym_name=Foo")]
    public void NewExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Bar { val: i64; } fun foo(b: Bar) -> i64 { return b.val; }"");
    let foo_def = result.definitions.at(cast<u64>(1)).some;
    if (foo_def is bound.BoundDefinition.function_def) {
        let func = foo_def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.member_access) {
                                let ma = val.member_access;
                                println(""member_name="" + ma.member_name);
                                if (ma.member_symbol.is_some()) {
                                    let sym = ma.member_symbol.some;
                                    println(""sym_name="" + sym.get_name());
                                    if (sym is bound.BoundSymbol.variable) {
                                        println(""is_field="" + cast<string>(sym.variable.variable_kind is bound.VariableSymbolKind.Field));
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
", "member_name=val\nsym_name=val\nis_field=true")]
    public void MemberAccessExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return cast<i64>(42); }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.cast_expr) {
                                let cast_e = val.cast_expr;
                                println(""cast_type="" + cast_e.get_bound_type().display_name());
                                println(""target_type="" + cast_e.target_type.display_name());
                                if (cast_e.inner.is_some()) {
                                    let inner = cast_e.inner.some;
                                    if (inner is bound.BoundExpression.literal) {
                                        println(""inner_val="" + inner.literal.value);
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
", "cast_type=i64\ntarget_type=i64\ninner_val=42")]
    public void CastExpressionTest() => _batch.Value.Assert();

    // Originally had timeoutSeconds: 30
    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (true) { break; } }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.while_expr) {
                                let w = val.while_expr;
                                if (w.body.is_some()) {
                                    let while_body = w.body.some;
                                    if (while_body is bound.BoundExpression.code_block) {
                                        let inner_cb = while_body.code_block;
                                        if (cast<i64>(inner_cb.statements.size()) > 0) {
                                            let inner = inner_cb.statements.at(cast<u64>(0)).some;
                                            if (inner is bound.BoundStatement.break_stmt) {
                                                println(""has_break=true"");
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
}
", "has_break=true")]
    public void BreakStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (true) { continue; } }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.while_expr) {
                                let w = val.while_expr;
                                if (w.body.is_some()) {
                                    let while_body = w.body.some;
                                    if (while_body is bound.BoundExpression.code_block) {
                                        let inner_cb = while_body.code_block;
                                        if (cast<i64>(inner_cb.statements.size()) > 0) {
                                            let inner = inner_cb.statements.at(cast<u64>(0)).some;
                                            if (inner is bound.BoundStatement.continue_stmt) {
                                                println(""has_continue=true"");
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
}
", "has_continue=true")]
    public void ContinueStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { let a: i64 = 1; let b: i64 = 2; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                println(""stmt_count="" + cast<string>(cast<i64>(cb.statements.size())));
                println(""block_type="" + cb.get_bound_type().display_name());
                if (cast<i64>(cb.statements.size()) >= 2) {
                    let s0 = cb.statements.at(cast<u64>(0)).some;
                    let s1 = cb.statements.at(cast<u64>(1)).some;
                    if (s0 is bound.BoundStatement.let_decl) {
                        println(""s0_var="" + s0.let_decl.variable_symbol.some.name);
                    }
                    if (s1 is bound.BoundStatement.let_decl) {
                        println(""s1_var="" + s1.let_decl.variable_symbol.some.name);
                    }
                }
            }
        }
    }
}
", "stmt_count=2\nblock_type=void\ns0_var=a\ns1_var=b")]
    public void CodeBlockExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""initial { 42; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.initial_routine) {
        let init = def.initial_routine;
        if (init.body.is_some()) {
            let body = init.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.literal) {
                                println(""init_body_val="" + val.literal.value);
                                println(""init_body_type="" + val.literal.get_bound_type().display_name());
                            }
                        }
                    }
                }
            }
        }
    }
}
", "init_body_val=42\ninit_body_type=i64")]
    public void InitialRoutineBodyTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""namespace App { fun add(a: i64, b: i64) -> i64 { return a; } }"");
    let ns_def = result.definitions.at(cast<u64>(0)).some;
    if (ns_def is bound.BoundDefinition.namespace_def) {
        let ns = ns_def.namespace_def;
        if (cast<i64>(ns.children.size()) > 0) {
            let child = ns.children.at(cast<u64>(0)).some;
            if (child is bound.BoundDefinition.function_def) {
                let func = child.function_def;
                if (func.body.is_some()) {
                    let body = func.body.some;
                    if (body is bound.BoundExpression.code_block) {
                        let cb = body.code_block;
                        if (cast<i64>(cb.statements.size()) > 0) {
                            let stmt = cb.statements.at(cast<u64>(0)).some;
                            if (stmt is bound.BoundStatement.return_stmt) {
                                if (stmt.return_stmt.value.is_some()) {
                                    let val = stmt.return_stmt.value.some;
                                    if (val is bound.BoundExpression.identifier) {
                                        println(""param_ref="" + val.identifier.symbol.some.get_name());
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
", "param_ref=a")]
    public void NestedNamespaceFunctionBodyTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Counter { count: i64; fun get() -> i64 { return this.count; } }"");
    let cls_def = result.definitions.at(cast<u64>(0)).some;
    if (cls_def is bound.BoundDefinition.class_def) {
        let cls = cls_def.class_def;
        if (cast<i64>(cls.methods.size()) > 0) {
            let md = cls.methods.at(cast<u64>(0)).some;
            if (md is bound.BoundDefinition.function_def) {
                let func = md.function_def;
                if (func.body.is_some()) {
                    let body = func.body.some;
                    if (body is bound.BoundExpression.code_block) {
                        let cb = body.code_block;
                        if (cast<i64>(cb.statements.size()) > 0) {
                            let stmt = cb.statements.at(cast<u64>(0)).some;
                            if (stmt is bound.BoundStatement.return_stmt) {
                                if (stmt.return_stmt.value.is_some()) {
                                    let val = stmt.return_stmt.value.some;
                                    if (val is bound.BoundExpression.member_access) {
                                        println(""method_body_bound=true"");
                                        println(""member="" + val.member_access.member_name);
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
", "method_body_bound=true\nmember=count")]
    public void ClassMethodBodyTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { if (true) {} else {} }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.expression) {
                        if (stmt.expression.expression.is_some()) {
                            let val = stmt.expression.expression.some;
                            if (val is bound.BoundExpression.if_expr) {
                                let if_e = val.if_expr;
                                if (if_e.then_block.is_some()) {
                                    println(""has_then=true"");
                                }
                                if (if_e.else_block.is_some()) {
                                    println(""has_else=true"");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
", "has_then=true\nhas_else=true")]
    public void IfElseStatementTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { return; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        let ret = stmt.return_stmt;
                        if (ret.value.is_none()) {
                            println(""void_return=true"");
                        }
                        println(""ret_type="" + ret.return_type.display_name());
                    }
                }
            }
        }
    }
}
", "void_return=true\nret_type=void")]
    public void VoidReturnTypeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return 1 + 2 + 3; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                if (cast<i64>(cb.statements.size()) > 0) {
                    let stmt = cb.statements.at(cast<u64>(0)).some;
                    if (stmt is bound.BoundStatement.return_stmt) {
                        if (stmt.return_stmt.value.is_some()) {
                            let val = stmt.return_stmt.value.some;
                            if (val is bound.BoundExpression.binary) {
                                let bin = val.binary;
                                println(""operand_count="" + cast<string>(cast<i64>(bin.operands.size())));
                                println(""operator_count="" + cast<string>(cast<i64>(bin.operators.size())));
                            }
                        }
                    }
                }
            }
        }
    }
}
", "operand_count=3\noperator_count=2")]
    public void ChainedBinaryExpressionTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { let x: i64 = 42; return x; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                let stmt_count: i64 = cast<i64>(cb.statements.size());
                println(""stmt_count="" + cast<string>(stmt_count));
                if (stmt_count >= 2) {
                    let s0 = cb.statements.at(cast<u64>(0)).some;
                    let s1 = cb.statements.at(cast<u64>(1)).some;
                    if (s0 is bound.BoundStatement.let_decl) {
                        println(""s0_kind=let_decl"");
                        println(""s0_name="" + s0.let_decl.variable_symbol.some.name);
                        println(""s0_type="" + s0.let_decl.bound_type.display_name());
                    }
                    if (s1 is bound.BoundStatement.return_stmt) {
                        println(""s1_kind=return_stmt"");
                        if (s1.return_stmt.value.is_some()) {
                            let ret_val = s1.return_stmt.value.some;
                            println(""ret_type="" + ret_val.get_bound_type().display_name());
                            if (ret_val is bound.BoundExpression.identifier) {
                                println(""ret_name="" + ret_val.identifier.symbol.some.get_name());
                            }
                        }
                    }
                }
            }
        }
    }
}
", "stmt_count=2\ns0_kind=let_decl\ns0_name=x\ns0_type=i64\ns1_kind=return_stmt\nret_type=i64\nret_name=x")]
    public void LetAndReturnTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo(n: i64) -> i64 { let sum: mut i64 = 0; let i: mut i64 = 0; while (i < n) { sum = sum + i; i = i + 1; } return sum; }"");
    let def = result.definitions.at(cast<u64>(0)).some;
    if (def is bound.BoundDefinition.function_def) {
        let func = def.function_def;
        if (func.body.is_some()) {
            let body = func.body.some;
            if (body is bound.BoundExpression.code_block) {
                let cb = body.code_block;
                let stmt_count: i64 = cast<i64>(cb.statements.size());
                println(""stmt_count="" + cast<string>(stmt_count));
                if (stmt_count >= 4) {
                    let s3 = cb.statements.at(cast<u64>(3)).some;
                    if (s3 is bound.BoundStatement.return_stmt) {
                        if (s3.return_stmt.value.is_some()) {
                            let ret_val = s3.return_stmt.value.some;
                            println(""ret_type="" + ret_val.get_bound_type().display_name());
                            if (ret_val is bound.BoundExpression.identifier) {
                                println(""ret_name="" + ret_val.identifier.symbol.some.get_name());
                            }
                        }
                    }
                }
            }
        }
    }
}
", "stmt_count=4\nret_type=i64\nret_name=sum")]
    public void LetWhileReturnTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { let x: i64 = 42; let y: i64 = x; return y; }"");
    let err_count: i64 = cast<i64>(result.errors.size());
    println(""errors="" + cast<string>(err_count));
    if (err_count == 0) {
        let def = result.definitions.at(cast<u64>(0)).some;
        if (def is bound.BoundDefinition.function_def) {
            let func = def.function_def;
            if (func.body.is_some()) {
                let body = func.body.some;
                if (body is bound.BoundExpression.code_block) {
                    let cb = body.code_block;
                    if (cast<i64>(cb.statements.size()) >= 3) {
                        let s2 = cb.statements.at(cast<u64>(2)).some;
                        if (s2 is bound.BoundStatement.return_stmt) {
                            if (s2.return_stmt.value.is_some()) {
                                println(""ret_type="" + s2.return_stmt.value.some.get_bound_type().display_name());
                            }
                        }
                    }
                }
            }
        }
    }
}
", "errors=0\nret_type=i64")]
    public void LetVariableChainTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let src: string = ""namespace myns { class Box { val: i64; } } fun make_box() -> myns.Box { let result: mut myns.Box = new myns.Box(); return result; }"";
    let result = compiler.compile(src);
    let err_count: i64 = cast<i64>(result.errors.size());
    println(""errors="" + cast<string>(err_count));
    if (err_count > 0) {
        let ei: mut i64 = 0;
        while (ei < err_count) {
            let err = result.errors.at(cast<u64>(ei)).some;
            println(""err_"" + cast<string>(ei) + ""="" + err.message);
            ei = ei + 1;
        }
    }
}
", "errors=0")]
    public void QualifiedTypeResolveTest() => _batch.Value.Assert();
}
