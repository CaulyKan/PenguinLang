using static EmperorPenguin.Tests.BatchCompiler;

namespace EmperorPenguin.Tests;

/// <summary>
/// Comprehensive IR tests covering all implemented bound nodes.
/// Tests the new simplified IR with 16 core instructions.
/// </summary>
public class IRBasicTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitIRBatch<IRBasicTest>());

    #region Literal Tests (BoundLiteralExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun foo() -> i64 { return 42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 42
inst1=RET %t0")]
    public void TestLiteralInteger() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun foo() -> bool { return true; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:bool = CONST true
inst1=RET %t0")]
    public void TestLiteralBool() => _batch.Value.Assert();

    #endregion

    #region Binary Operation Tests (BoundBinaryExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return 1 + 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 2
inst2=%t2:i64 = BINOP add %t0, %t1
inst3=RET %t2")]
    public void TestBinaryAdd() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return 10 - 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 10
inst1=%t1:i64 = CONST 3
inst2=%t2:i64 = BINOP sub %t0, %t1
inst3=RET %t2")]
    public void TestBinarySub() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return 3 * 4; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 3
inst1=%t1:i64 = CONST 4
inst2=%t2:i64 = BINOP mul %t0, %t1
inst3=RET %t2")]
    public void TestBinaryMul() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> bool { return 1 < 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 2
inst2=%t2:i64 = BINOP slt %t0, %t1
inst3=RET %t2")]
    public void TestBinaryCompareLess() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> bool { return 1 == 1; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 1
inst2=%t2:i64 = BINOP eq %t0, %t1
inst3=RET %t2")]
    public void TestBinaryCompareEqual() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return 1 + 2 * 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 2
inst2=%t2:i64 = CONST 3
inst3=%t3:i64 = BINOP mul %t1, %t2
inst4=%t4:i64 = BINOP add %t0, %t3
inst5=RET %t4")]
    public void TestBinaryChain() => _batch.Value.Assert();

    #endregion

    #region Unary Operation Tests (BoundUnaryExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return -42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 42
inst1=%t1:i64 = UNARYOP neg %t0
inst2=RET %t1")]
    public void TestUnaryNegate() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> bool { return !true; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:bool = CONST true
inst1=%t1:bool = UNARYOP not %t0
inst2=RET %t1")]
    public void TestUnaryNot() => _batch.Value.Assert();

    #endregion

    #region Cast Tests (BoundCastExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let x: i32 = 42; return cast<i64>(x); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%x:i64 = CONST 42
inst1=%t0:i64 = CAST %x i64->i64
inst2=RET %t0")]
    public void TestCast() => _batch.Value.Assert();

    #endregion

    #region If Expression Tests (BoundIfExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test(x: i64) -> i64 { if (x > 0) { return 1; } else { return 0; } }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = ARG x 0
inst1=%t2:i64 = CONST 0
inst2=%t3:i64 = BINOP sgt %t0, %t2
inst3=BR_COND %t3, then1, else2
inst4=then1:
inst5=%t4:i64 = CONST 1
inst6=RET %t4
inst7=else2:
inst8=%t5:i64 = CONST 0
inst9=RET %t5
inst10=merge0:
inst11=RET_VOID")]
    public void TestIfExpr() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test(x: i64) -> i64 { if (x > 0) { return 1; } return 0; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = ARG x 0
inst1=%t2:i64 = CONST 0
inst2=%t3:i64 = BINOP sgt %t0, %t2
inst3=BR_COND %t3, then1, merge0
inst4=then1:
inst5=%t4:i64 = CONST 1
inst6=RET %t4
inst7=merge0:
inst8=%t5:i64 = CONST 0
inst9=RET %t5")]
    public void TestIfExprNoElse() => _batch.Value.Assert();

    #endregion

    #region While Expression Tests (BoundWhileExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let mut sum: i64 = 0; let mut i: i64 = 0; while (i < 3) { sum = sum + i; i = i + 1; } return sum; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%sum:i64 = CONST 0
inst1=%i:i64 = CONST 0
inst2=while0:
inst3=%t0:i64 = CONST 3
inst4=%t1:i64 = BINOP slt %i, %t0
inst5=BR_COND %t1, while_body1, while_exit2
inst6=while_body1:
inst7=%t2:i64 = BINOP add %sum, %i
inst8=%sum:i64 = ASSIGN %t2
inst9=%t3:i64 = CONST 1
inst10=%t4:i64 = BINOP add %i, %t3
inst11=%i:i64 = ASSIGN %t4
inst12=BR while0
inst13=while_exit2:
inst14=RET %sum")]
    public void TestWhileExpr() => _batch.Value.Assert();

    #endregion

    #region Code Block Tests (BoundCodeBlockExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let x: i64 = 1; { let y: i64 = 2; x = y; } return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%x:i64 = CONST 1
inst1=%y:i64 = CONST 2
inst2=%x:i64 = ASSIGN %y
inst3=RET %x")]
    public void TestCodeBlock() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { { 1; 2; 3 } }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 2
inst2=RET_VOID")]
    public void TestCodeBlockTrailingExpr() => _batch.Value.Assert();

    #endregion

    #region Assignment Tests (BoundAssignmentStatement)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let mut x: i64 = 0; x = 42; return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%x:i64 = CONST 0
inst1=%t0:i64 = CONST 42
inst2=%x:i64 = ASSIGN %t0
inst3=RET %x")]
    public void TestAssignment() => _batch.Value.Assert();

    #endregion

    #region Return Statement Tests (BoundReturnStatement)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() { return; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=RET_VOID")]
    public void TestReturnVoid() => _batch.Value.Assert();

    #endregion

    #region Break/Continue Tests (BoundBreakStatement, BoundContinueStatement)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let mut sum: i64 = 0; let mut i: i64 = 0; while (i < 10) { if (i == 5) { break; } sum = sum + i; i = i + 1; } return sum; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%sum:i64 = CONST 0
inst1=%i:i64 = CONST 0
inst2=while0:
inst3=%t0:i64 = CONST 10
inst4=%t1:i64 = BINOP slt %i, %t0
inst5=BR_COND %t1, while_body1, while_exit2
inst6=while_body1:
inst7=%t3:i64 = CONST 5
inst8=%t4:i64 = BINOP eq %i, %t3
inst9=BR_COND %t4, then4, merge3
inst10=then4:
inst11=BR while_exit2
inst12=merge3:
inst13=%t5:i64 = BINOP add %sum, %i
inst14=%sum:i64 = ASSIGN %t5
inst15=%t6:i64 = CONST 1
inst16=%t7:i64 = BINOP add %i, %t6
inst17=%i:i64 = ASSIGN %t7
inst18=BR while0
inst19=while_exit2:
inst20=RET %sum")]
    public void TestBreak() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let mut sum: i64 = 0; let mut i: i64 = 0; while (i < 10) { i = i + 1; if (i < 5) { continue; } sum = sum + i; } return sum; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%sum:i64 = CONST 0
inst1=%i:i64 = CONST 0
inst2=while0:
inst3=%t0:i64 = CONST 10
inst4=%t1:i64 = BINOP slt %i, %t0
inst5=BR_COND %t1, while_body1, while_exit2
inst6=while_body1:
inst7=%t2:i64 = CONST 1
inst8=%t3:i64 = BINOP add %i, %t2
inst9=%i:i64 = ASSIGN %t3
inst10=%t5:i64 = CONST 5
inst11=%t6:i64 = BINOP slt %i, %t5
inst12=BR_COND %t6, then4, merge3
inst13=then4:
inst14=BR while0
inst15=merge3:
inst16=%t7:i64 = BINOP add %sum, %i
inst17=%sum:i64 = ASSIGN %t7
inst18=BR while0
inst19=while_exit2:
inst20=RET %sum")]
    public void TestContinue() => _batch.Value.Assert();

    #endregion

    #region Function Call Tests (BoundFunctionCallExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun add(a: i64, b: i64) -> i64 { return a + b; } fun test() -> i64 { return add(1, 2); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(1)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 1
inst1=%t1:i64 = CONST 2
inst2=%t2:i64 = CALL @add(%t0, %t1)
inst3=RET %t2")]
    public void TestFunctionCall() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun void_func() {} fun test() { void_func(); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(1)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=CALL @void_func()
inst1=RET_VOID")]
    public void TestVoidFunctionCall() => _batch.Value.Assert();

    #endregion

    #region Identifier Tests (BoundIdentifierExpression)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let x: i64 = 42; let y: i64 = x; return y; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%x:i64 = CONST 42
inst1=%y:i64 = ASSIGN %x
inst2=RET %y")]
    public void TestIdentifier() => _batch.Value.Assert();

    #endregion

    #region Parameter Tests

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test(a: i64, b: i64, c: i64) -> i64 { return a + b + c; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = ARG a 0
inst1=%t1:i64 = ARG b 1
inst2=%t2:i64 = ARG c 2
inst3=%t3:i64 = BINOP add %t0, %t1
inst4=%t4:i64 = BINOP add %t3, %t2
inst5=RET %t4")]
    public void TestParameters() => _batch.Value.Assert();

    #endregion

    #region Module Tests

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun a() -> i64 { return 1; } fun b() -> i64 { return 2; } fun c() -> i64 { return 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    println(""funcs="" + cast<string>(cast<i64>(module.functions.size())));
}
", @"funcs=3")]
    public void TestMultipleFunctions() => _batch.Value.Assert();

    #endregion

    #region Complex Nested Tests

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test(n: i64) -> i64 { let mut result: i64 = 1; let mut i: i64 = 1; while (i <= n) { if (i % 2 == 0) { result = result * i; } else { result = result + i; } i = i + 1; } return result; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = ARG n 0
inst1=%result:i64 = CONST 1
inst2=%i:i64 = CONST 1
inst3=while0:
inst4=%t1:i64 = BINOP sle %i, %t0
inst5=BR_COND %t1, while_body1, while_exit2
inst6=while_body1:
inst7=%t3:i64 = CONST 2
inst8=%t4:i64 = BINOP mod %i, %t3
inst9=%t5:i64 = CONST 0
inst10=%t6:i64 = BINOP eq %t4, %t5
inst11=BR_COND %t6, then4, else5
inst12=then4:
inst13=%t7:i64 = BINOP mul %result, %i
inst14=%result:i64 = ASSIGN %t7
inst15=BR merge3
inst16=else5:
inst17=%t8:i64 = BINOP add %result, %i
inst18=%result:i64 = ASSIGN %t8
inst19=merge3:
inst20=%t9:i64 = CONST 1
inst21=%t10:i64 = BINOP add %i, %t9
inst22=%i:i64 = ASSIGN %t10
inst23=BR while0
inst24=while_exit2:
inst25=RET %result")]
    public void TestComplexNested() => _batch.Value.Assert();

    #endregion

    #region Additional IR Tests (merged from other IR test files)

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun foo() -> i64 { return 42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 42
inst1=RET %t0")]
    public void TestLiteralReturn() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun bar(x: i64) -> i64 { return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = ARG x 0
inst1=RET %t0")]
    public void TestParamReturn() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun callee(x: i64) -> i64 { return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    println(""params="" + cast<string>(cast<i64>(f.parameters.size())));
}
", @"params=1")]
    public void TestSimpleFunction() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { let x: i64 = 10; return x; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%x:i64 = CONST 10
inst1=RET %x")]
    public void TestIRFunctionParams() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun callee(x: i64) -> i64 { return x; } fun caller() -> i64 { return callee(42); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(1)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 42
inst1=%t1:i64 = CALL @callee(%t0)
inst2=RET %t1")]
    public void TestSimpleIRCall() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun test() -> i64 { return 42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(0)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=%t0:i64 = CONST 42
inst1=RET %t0")]
    public void TestIRCallVariant() => _batch.Value.Assert();

    [Fact]
    [BatchIRTest(@"
initial {
    let source = ""fun void_func() {} fun test() { void_func(); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let f = module.functions.at(cast<u64>(1)).some;
    let i: mut i64 = 0;
    while (i < cast<i64>(f.instructions.size())) {
        let inst = f.instructions.at(cast<u64>(i)).some;
        println(""inst"" + cast<string>(i) + ""="" + inst.display());
        i = i + 1;
    }
}
", @"inst0=CALL @void_func()
inst1=RET_VOID")]
    public void TestIRCallVoidVariant() => _batch.Value.Assert();

    #endregion

    #region Enum Tests

    // Test 1: Enum variant creation without payload
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { none; } fun create() -> Option { return new Option.none(); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0 = NEW_ENUM enum<Option>.none
inst1=RET %t0")]
    public void TestEnumVariantNone() => _batch.Value.Assert();

    // Test 2: Enum variant creation with payload
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun create() -> Option { return new Option.some(42); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:i64 = CONST 42
inst1=%t1 = NEW_ENUM enum<Option>.some(%t0)
inst2=RET %t1")]
    public void TestEnumVariantWithPayload() => _batch.Value.Assert();

    // Test 3: Enum with multiple variants
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum TrafficLight { Red; Yellow; Green; } fun red() -> TrafficLight { return new TrafficLight.Red(); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0 = NEW_ENUM enum<TrafficLight>.Red
inst1=RET %t0")]
    public void TestEnumMultipleVariants() => _batch.Value.Assert();

    // Test 4: Enum pattern matching with `is` expression
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun is_some(o: Option) -> bool { return o is Option.some; }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:enum<Option> = ARG o 0
inst1=%t1:i64 = CONST 0
inst2=%t2:bool = ISENUM %t0, %t1
inst3=RET %t2")]
    public void TestEnumPatternMatch() => _batch.Value.Assert();

    // Test 5: Enum variable declaration
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun test() { let x: Option = new Option.none(); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0 = NEW_ENUM enum<Option>.none
inst1=%x:enum<Option> = ASSIGN %t0
inst2=RET_VOID")]
    public void TestEnumVariableDeclaration() => _batch.Value.Assert();

    // Test 6: Enum mutable variable reassignment
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun test() { let mut x: Option = new Option.none(); x = new Option.some(5); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0 = NEW_ENUM enum<Option>.none
inst1=%x:enum<Option> = ASSIGN %t0
inst2=%t1:i64 = CONST 5
inst3=%t2 = NEW_ENUM enum<Option>.some(%t1)
inst4=%x:enum<Option> = ASSIGN %t2
inst5=RET_VOID")]
    public void TestEnumVariableReassignment() => _batch.Value.Assert();

    // Test 7: Enum return statement
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Result { ok: i64; error; } fun return_ok() -> Result { return new Result.ok(200); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:i64 = CONST 200
inst1=%t1 = NEW_ENUM enum<Result>.ok(%t0)
inst2=RET %t1")]
    public void TestEnumReturn() => _batch.Value.Assert();

    // Test 8: Enum in conditional (if-else pattern matching)
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun match_option(o: Option) -> i64 { if (o is Option.some) { return 1; } else { return 0; } }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:enum<Option> = ARG o 0
inst1=%t2:i64 = CONST 0
inst2=%t3:bool = ISENUM %t0, %t2
inst3=BR_COND %t3, then1, else2
inst4=then1:
inst5=%t4:i64 = CONST 1
inst6=RET %t4
inst7=else2:
inst8=%t5:i64 = CONST 0
inst9=RET %t5
inst10=merge0:
inst11=RET_VOID")]
    public void TestEnumPatternMatchBranch() => _batch.Value.Assert();

    // Test 9: Multiple enum definitions
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } enum Result { ok: i64; error; }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    println(""defs="" + cast<string>(cast<i64>(result.definitions.size())));
	}
	", @"defs=2")]
    public void TestMultipleEnumDefinitions() => _batch.Value.Assert();

    // Test 10: Enum as function parameter
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun take_option(o: Option) -> bool { if (o is Option.none) { return true; } else { return false; } }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:enum<Option> = ARG o 0
inst1=%t2:i64 = CONST 1
inst2=%t3:bool = ISENUM %t0, %t2
inst3=BR_COND %t3, then1, else2
inst4=then1:
inst5=%t4:bool = CONST true
inst6=RET %t4
inst7=else2:
inst8=%t5:bool = CONST false
inst9=RET %t5
inst10=merge0:
inst11=RET_VOID")]
    public void TestEnumFunctionParameter() => _batch.Value.Assert();

    // Test 11: Enum in complex function with nested pattern matching
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun nested_match(o: Option) -> i64 { if (o is Option.none) { return 0; } else { if (o is Option.some) { return 1; } else { return 2; } } }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:enum<Option> = ARG o 0
inst1=%t2:i64 = CONST 1
inst2=%t3:bool = ISENUM %t0, %t2
inst3=BR_COND %t3, then1, else2
inst4=then1:
inst5=%t4:i64 = CONST 0
inst6=RET %t4
inst7=else2:
inst8=%t6:i64 = CONST 0
inst9=%t7:bool = ISENUM %t0, %t6
inst10=BR_COND %t7, then4, else5
inst11=then4:
inst12=%t8:i64 = CONST 1
inst13=RET %t8
inst14=else5:
inst15=%t9:i64 = CONST 2
inst16=RET %t9
inst17=merge3:
inst18=merge0:
inst19=RET_VOID")]
    public void TestEnumNestedPatternMatch() => _batch.Value.Assert();

    // Test 12: Enum with arithmetic operations
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun add(o: Option, val: i64) -> Option { if (o is Option.some) { return new Option.some(val + 1); } else { return new Option.none(); } }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:enum<Option> = ARG o 0
inst1=%t1:i64 = ARG val 1
inst2=%t3:i64 = CONST 0
inst3=%t4:bool = ISENUM %t0, %t3
inst4=BR_COND %t4, then1, else2
inst5=then1:
inst6=%t5:i64 = CONST 1
inst7=%t6:i64 = BINOP add %t1, %t5
inst8=%t7 = NEW_ENUM enum<Option>.some(%t6)
inst9=RET %t7
inst10=else2:
inst11=%t8 = NEW_ENUM enum<Option>.none
inst12=RET %t8
inst13=merge0:
inst14=RET_VOID")]
    public void TestEnumWithArithmetic() => _batch.Value.Assert();

    // Test 13: Enum with while loop
    [Fact]
    [BatchIRTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun loop_test(n: i64) -> Option { while (n > 0) { n = n - 1; } return new Option.none(); }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let generator = new ir.IRGenerator();
	    let module = generator.generate(result);
	    let f = module.functions.at(cast<u64>(0)).some;
	    let i: mut i64 = 0;
	    while (i < cast<i64>(f.instructions.size())) {
	        let inst = f.instructions.at(cast<u64>(i)).some;
	        println(""inst"" + cast<string>(i) + ""="" + inst.display());
	        i = i + 1;
	    }
	}
	", @"inst0=%t0:i64 = ARG n 0
inst1=while0:
inst2=%t1:i64 = CONST 0
inst3=%t2:i64 = BINOP sgt %t0, %t1
inst4=BR_COND %t2, while_body1, while_exit2
inst5=while_body1:
inst6=%t3:i64 = CONST 1
inst7=%t4:i64 = BINOP sub %t0, %t3
inst8=%t0:i64 = ASSIGN %t4
inst9=BR while0
inst10=while_exit2:
inst11=%t5 = NEW_ENUM enum<Option>.none
inst12=RET %t5")]
    public void TestEnumWithWhileLoop() => _batch.Value.Assert();

    #endregion


    #region Enum Payload Access IR Tests

    // Test 1: Read enum payload (some variant with value)
    [Fact]
    [BatchIRTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun get_payload(o: Option) -> i64 { return o.some; }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let generator = new ir.IRGenerator();
            let module = generator.generate(result);
            let f = module.functions.at(cast<u64>(0)).some;
            let i: mut i64 = 0;
            while (i < cast<i64>(f.instructions.size())) {
                let inst = f.instructions.at(cast<u64>(i)).some;
                println(""inst"" + cast<string>(i) + ""="" + inst.display());
                i = i + 1;
            }
        }
        ", "inst0=%t0:enum<Option> = ARG o 0\ninst1=%t1:i64 = RDENUM %t0, .some\ninst2=RET %t1")]
    public void TestReadEnumPayload() => _batch.Value.Assert();

    // Test 2: Read enum payload in expression
    [Fact]
    [BatchIRTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun add_one(o: Option) -> i64 { return o.some + 1; }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let generator = new ir.IRGenerator();
            let module = generator.generate(result);
            let f = module.functions.at(cast<u64>(0)).some;
            let i: mut i64 = 0;
            while (i < cast<i64>(f.instructions.size())) {
                let inst = f.instructions.at(cast<u64>(i)).some;
                println(""inst"" + cast<string>(i) + ""="" + inst.display());
                i = i + 1;
            }
        }
        ", "inst0=%t0:enum<Option> = ARG o 0\ninst1=%t1:i64 = RDENUM %t0, .some\ninst2=%t2:i64 = CONST 1\ninst3=%t3:i64 = BINOP add %t1, %t2\ninst4=RET %t3")]
    public void TestReadEnumPayloadInExpression() => _batch.Value.Assert();

    #endregion

    #region Enum Pattern Matching IR Tests

    // Test 1: Simple is expression for enum variant
    [Fact]
    [BatchIRTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun is_some(o: Option) -> bool { return o is Option.some; }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let generator = new ir.IRGenerator();
            let module = generator.generate(result);
            let f = module.functions.at(cast<u64>(0)).some;
            let i: mut i64 = 0;
            while (i < cast<i64>(f.instructions.size())) {
                let inst = f.instructions.at(cast<u64>(i)).some;
                println(""inst"" + cast<string>(i) + ""="" + inst.display());
                i = i + 1;
            }
        }
        ", "inst0=%t0:enum<Option> = ARG o 0\ninst1=%t1:i64 = CONST 0\ninst2=%t2:bool = ISENUM %t0, %t1\ninst3=RET %t2")]
    public void TestIsExpressionIR() => _batch.Value.Assert();

    // Test 2: is expression for none variant
    [Fact]
    [BatchIRTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun is_none(o: Option) -> bool { return o is Option.none; }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let generator = new ir.IRGenerator();
            let module = generator.generate(result);
            let f = module.functions.at(cast<u64>(0)).some;
            let i: mut i64 = 0;
            while (i < cast<i64>(f.instructions.size())) {
                let inst = f.instructions.at(cast<u64>(i)).some;
                println(""inst"" + cast<string>(i) + ""="" + inst.display());
                i = i + 1;
            }
        }
        ", "inst0=%t0:enum<Option> = ARG o 0\ninst1=%t1:i64 = CONST 1\ninst2=%t2:bool = ISENUM %t0, %t1\ninst3=RET %t2")]
    public void TestIsNoneVariantIR() => _batch.Value.Assert();

    // Test 3: Pattern matching in if statement
    [Fact]
    [BatchIRTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun match_option(o: Option) -> i64 { if (o is Option.some) { return 1; } else { return 0; } }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let generator = new ir.IRGenerator();
            let module = generator.generate(result);
            let f = module.functions.at(cast<u64>(0)).some;
            let i: mut i64 = 0;
            while (i < cast<i64>(f.instructions.size())) {
                let inst = f.instructions.at(cast<u64>(i)).some;
                println(""inst"" + cast<string>(i) + ""="" + inst.display());
                i = i + 1;
            }
        }
        ", "inst0=%t0:enum<Option> = ARG o 0\ninst1=%t2:i64 = CONST 0\ninst2=%t3:bool = ISENUM %t0, %t2\ninst3=BR_COND %t3, then1, else2\ninst4=then1:\ninst5=%t4:i64 = CONST 1\ninst6=RET %t4\ninst7=else2:\ninst8=%t5:i64 = CONST 0\ninst9=RET %t5\ninst10=merge0:\ninst11=RET_VOID")]
    public void TestPatternMatchInIfIR() => _batch.Value.Assert();

    #endregion
}
