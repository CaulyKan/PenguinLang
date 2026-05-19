using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTreeControlFlowTest
{
    private static readonly Lazy<BatchResults> _batch = new(() => BatchCompiler.InitBoundBatch<BoundTreeControlFlowTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 {}"");
    if (result.has_errors()) {
        let err = result.errors.at(cast<u64>(0)).some;
        println(""has_error=true"");
        println(""error_msg="" + err.message);
    }
}
", "has_error=true\nerror_msg=Non-void function 'foo' must return a value on all paths")]
    public void NonVoidFunctionMissingReturnTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() {}"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void VoidFunctionNoReturnOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { return; }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void VoidFunctionWithReturnOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { return 42; }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void ReturnWithCorrectTypeOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (true) { break; } }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void BreakInsideWhileOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (true) { continue; } }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void ContinueInsideWhileOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { break; }"");
    if (result.has_errors()) {
        let err = result.errors.at(cast<u64>(0)).some;
        println(""has_error=true"");
        println(""error_msg="" + err.message);
    }
}
", "has_error=true\nerror_msg=break statement not inside a loop")]
    public void BreakOutsideLoopErrorTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { continue; }"");
    if (result.has_errors()) {
        let err = result.errors.at(cast<u64>(0)).some;
        println(""has_error=true"");
        println(""error_msg="" + err.message);
    }
}
", "has_error=true\nerror_msg=continue statement not inside a loop")]
    public void ContinueOutsideLoopErrorTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> bool { return true; }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void NonVoidFunctionWithReturnOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""extern fun foo() -> i64;"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void ExternFunctionNoReturnOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { while (true) { while (true) { break; } break; } }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void NestedBreakInWhileOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() -> i64 { while (true) { return 1; } }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void ReturnInsideIfInWhileOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""fun foo() { break; continue; }"");
    println(""error_count="" + cast<string>(cast<i64>(result.errors.size())));
}
", "error_count=2")]
    public void MultipleErrorsTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""initial { 42; }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void InitialRoutineNoReturnOkTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Foo { fun get() -> i64 {} }"");
    if (result.has_errors()) {
        println(""has_error=true"");
    }
}
", "has_error=true")]
    public void ClassMethodMissingReturnTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(""class Foo { fun get() -> i64 { return 0; } }"");
    println(""has_errors="" + cast<string>(result.has_errors()));
}
", "has_errors=false")]
    public void ClassMethodWithReturnOkTest() => _batch.Value.Assert();
}
