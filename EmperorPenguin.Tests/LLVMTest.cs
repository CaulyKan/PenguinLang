using static EmperorPenguin.Tests.BatchCompiler;

namespace EmperorPenguin.Tests;

/// <summary>
/// Tests for LLVM IR emission from EmperorPenguin IR.
/// Each test compiles source → IR → LLVM IR and verifies the LLVM IR output.
/// </summary>
public class LLVMTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitLLVMBatch<LLVMTest>());

    #region Integer Constant and Return

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun foo() -> i64 { return 42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @foo() {
entry:
  %t0 = add i64 0, 42
  ret i64 %t0
}")]
    public void TestLLVMIntReturn() => _batch.Value.AssertSemantic();

    #endregion

    #region Boolean Constants

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun foo() -> bool { return true; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @foo() {
entry:
  %t0 = add i8 0, 1
  ret i8 %t0
}")]
    public void TestLLVMBoolReturn() => _batch.Value.AssertSemantic();

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun foo() -> bool { return false; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @foo() {
entry:
  %t0 = add i8 0, 0
  ret i8 %t0
}")]
    public void TestLLVMBoolFalse() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Operations

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 1 + 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 1
  %t1 = add i64 0, 2
  %t2 = add i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryAdd() => _batch.Value.AssertSemantic();

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 3 - 1; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 3
  %t1 = add i64 0, 1
  %t2 = sub i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinarySub() => _batch.Value.AssertSemantic();

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 3 * 4; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 3
  %t1 = add i64 0, 4
  %t2 = mul i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryMul() => _batch.Value.AssertSemantic();

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 1 < 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 1
  %t1 = add i64 0, 2
  %t2 = icmp slt i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryCompare() => _batch.Value.AssertSemantic();

    #endregion

    #region Function Call

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun add(a: i64, b: i64) -> i64 { return a + b; } fun test() -> i64 { return add(1, 2); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @add(i64 %a, i64 %b) {
entry:
  %t2 = add i64 %a, %b
  ret i64 %t2
}

define i64 @test() {
entry:
  %t0 = add i64 0, 1
  %t1 = add i64 0, 2
  %t2 = call i64 @add(i64 %t0, i64 %t1)
  ret i64 %t2
}")]
    public void TestLLVMFunctionCall() => _batch.Value.AssertSemantic();

    #endregion

    #region Void Function

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun void_func() {}"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define void @void_func() {
entry:
  ret void
}")]
    public void TestLLVMVoidFunction() => _batch.Value.AssertSemantic();

    #endregion

    #region Unary Negation

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return -42; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 42
  %t1 = sub i64 0, %t0
  ret i64 %t1
}")]
    public void TestLLVMUnaryNeg() => _batch.Value.AssertSemantic();

    #endregion

    #region If/Else Branching

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test(x: i64) -> i64 { if (x > 0) { return 1; } else { return 0; } }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test(i64 %x) {
entry:
  %t2 = add i64 0, 0
  %t3 = icmp sgt i64 %x, %t2
  br i1 %t3, label %then1, label %else2
then1:
  %t4 = add i64 0, 1
  ret i64 %t4
else2:
  %t5 = add i64 0, 0
  ret i64 %t5
merge0:
  ret void
}")]
    public void TestLLVMIfElse() => _batch.Value.AssertSemantic();

    #endregion

    #region While Loop

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { let mut sum: i64 = 0; let mut i: i64 = 0; while (i < 3) { sum = sum + i; i = i + 1; } return sum; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %sum.addr = alloca i64
  %i.addr = alloca i64
  store i64 0, ptr %sum.addr
  store i64 0, ptr %i.addr
  br label %while0
while0:
  %t0 = add i64 0, 3
  %tmp_0 = load i64, ptr %i.addr
  %t1 = icmp slt i64 %tmp_0, %t0
  br i1 %t1, label %while_body1, label %while_exit2
while_body1:
  %tmp_1 = load i64, ptr %sum.addr
  %tmp_2 = load i64, ptr %i.addr
  %t2 = add i64 %tmp_1, %tmp_2
  store i64 %t2, ptr %sum.addr
  %t3 = add i64 0, 1
  %tmp_3 = load i64, ptr %i.addr
  %t4 = add i64 %tmp_3, %t3
  store i64 %t4, ptr %i.addr
  br label %while0
while_exit2:
  %tmp_4 = load i64, ptr %sum.addr
  ret i64 %tmp_4
}")]
    public void TestLLVMWhileLoop() => _batch.Value.AssertSemantic();

    #endregion

    #region Extern Function Declaration

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""extern fun puts(s: string) -> i32;"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"declare i32 @puts(ptr)")]
    public void TestLLVMExternDecl() => _batch.Value.AssertSemantic();

    #endregion

    #region Enum NEW_ENUM

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""enum Option { none; } fun create() -> Option { return new Option.none(); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"%enum.Option = type { ptr, i32 }
@Option_interface_map = private constant [0 x { ptr, ptr }] []
@Option_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
  ptr @.Option_name,
  i32 0,
  i32 0,
  ptr null,
  ptr null,
  ptr null,
  i32 0,
  ptr @Option_interface_map,
  ptr null
}
@.Option_name = private unnamed_addr constant [7 x i8] c""Option\00""

declare ptr @_emperor_int_to_string(i32)
declare ptr @_emperor_i64_to_string(i64)
declare ptr @_emperor_string_concat(ptr, ptr)

define %enum.Option @create() {
entry:
  %tmp_0 = alloca %enum.Option
  %tmp_1 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 0
  store ptr @Option_metadata, ptr %tmp_1
  %tmp_2 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 1
  store i32 0, ptr %tmp_2
  %t0 = load %enum.Option, ptr %tmp_0
  ret %enum.Option %t0
}")]
    public void TestLLVMNewEnum() => _batch.Value.AssertSemantic();

    #endregion

    #region Enum NEW_ENUM with Payload

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""enum Option { some: i64; none; } fun create() -> Option { return new Option.some(42); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"%enum.Option = type { ptr, i32, i64 }
@Option_interface_map = private constant [0 x { ptr, ptr }] []
@Option_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
  ptr @.Option_name,
  i32 0,
  i32 0,
  ptr null,
  ptr null,
  ptr null,
  i32 0,
  ptr @Option_interface_map,
  ptr null
}
@.Option_name = private unnamed_addr constant [7 x i8] c""Option\00""

declare ptr @_emperor_int_to_string(i32)
declare ptr @_emperor_i64_to_string(i64)
declare ptr @_emperor_string_concat(ptr, ptr)

define %enum.Option @create() {
entry:
  %t0 = add i64 0, 42
  %tmp_0 = alloca %enum.Option
  %tmp_1 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 0
  store ptr @Option_metadata, ptr %tmp_1
  %tmp_2 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 1
  store i32 0, ptr %tmp_2
  %tmp_3 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 2
  store i64 %t0, ptr %tmp_3
  %t1 = load %enum.Option, ptr %tmp_0
  ret %enum.Option %t1
}")]
    public void TestLLVMNewEnumWithPayload() => _batch.Value.AssertSemantic();

    #endregion

    #region Enum ISENUM

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""enum Option { some: i64; none; } fun is_some(o: Option) -> bool { return o is Option.some; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"%enum.Option = type { ptr, i32, i64 }
@Option_interface_map = private constant [0 x { ptr, ptr }] []
@Option_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
  ptr @.Option_name,
  i32 0,
  i32 0,
  ptr null,
  ptr null,
  ptr null,
  i32 0,
  ptr @Option_interface_map,
  ptr null
}
@.Option_name = private unnamed_addr constant [7 x i8] c""Option\00""

declare ptr @_emperor_int_to_string(i32)
declare ptr @_emperor_i64_to_string(i64)
declare ptr @_emperor_string_concat(ptr, ptr)

define i8 @is_some(%enum.Option %o) {
entry:
  %t1 = add i64 0, 0
  %tmp_0 = alloca %enum.Option
  store %enum.Option %o, ptr %tmp_0
  %tmp_1 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 1
  %tmp_2 = load i32, ptr %tmp_1
  %tmp_3 = trunc i64 %t1 to i32
  %tmp_4 = icmp eq i32 %tmp_2, %tmp_3
  %t2 = zext i1 %tmp_4 to i8
  ret i8 %t2
}")]
    public void TestLLVMIsEnum() => _batch.Value.AssertSemantic();

    #endregion

    #region Enum with If/Else (ISENUM + BR_COND)

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""enum Option { some: i64; none; } fun match_option(o: Option) -> i64 { if (o is Option.some) { return 1; } else { return 0; } }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"%enum.Option = type { ptr, i32, i64 }
@Option_interface_map = private constant [0 x { ptr, ptr }] []
@Option_metadata = private constant { ptr, i32, i32, ptr, ptr, ptr, i32, ptr, ptr } {
  ptr @.Option_name,
  i32 0,
  i32 0,
  ptr null,
  ptr null,
  ptr null,
  i32 0,
  ptr @Option_interface_map,
  ptr null
}
@.Option_name = private unnamed_addr constant [7 x i8] c""Option\00""

declare ptr @_emperor_int_to_string(i32)
declare ptr @_emperor_i64_to_string(i64)
declare ptr @_emperor_string_concat(ptr, ptr)

define i64 @match_option(%enum.Option %o) {
entry:
  %t2 = add i64 0, 0
  %tmp_0 = alloca %enum.Option
  store %enum.Option %o, ptr %tmp_0
  %tmp_1 = getelementptr %enum.Option, ptr %tmp_0, i32 0, i32 1
  %tmp_2 = load i32, ptr %tmp_1
  %tmp_3 = trunc i64 %t2 to i32
  %tmp_4 = icmp eq i32 %tmp_2, %tmp_3
  %t3 = zext i1 %tmp_4 to i8
  %cond_0 = trunc i8 %t3 to i1
  br i1 %cond_0, label %then1, label %else2
then1:
  %t4 = add i64 0, 1
  ret i64 %t4
else2:
  %t5 = add i64 0, 0
  ret i64 %t5
merge0:
  ret void
}")]
    public void TestLLVMEnumBranching() => _batch.Value.AssertSemantic();

    #endregion

    #region Void Call

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun void_func() {} fun test() { void_func(); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define void @void_func() {
entry:
  ret void
}

define void @test() {
entry:
  call void @void_func()
  ret void
}")]
    public void TestLLVMVoidCall() => _batch.Value.AssertSemantic();

    #endregion

    #region Cast Operation

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { let x: i32 = 42; return cast<i64>(x); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %x = add i64 0, 42
  %t0 = add i64 0, %x
  ret i64 %t0
}")]
    public void TestLLVMCast() => _batch.Value.AssertSemantic();

    #endregion

    #region Unary Not

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return !true; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i8 0, 1
  %t1 = xor i8 %t0, 1
  ret i8 %t1
}")]
    public void TestLLVMUnaryNot() => _batch.Value.AssertSemantic();

    #endregion

    #region Multiple Functions in Module

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun a() -> i64 { return 1; } fun b() -> i64 { return 2; } fun c() -> i64 { return 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @a() {
entry:
  %t0 = add i64 0, 1
  ret i64 %t0
}

define i64 @b() {
entry:
  %t0 = add i64 0, 2
  ret i64 %t0
}

define i64 @c() {
entry:
  %t0 = add i64 0, 3
  ret i64 %t0
}")]
    public void TestLLVMMultipleFunctions() => _batch.Value.AssertSemantic();

    #endregion

    #region Complex: If/Else in While Loop

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test(n: i64) -> i64 { let mut sum: i64 = 0; let mut i: i64 = 0; while (i < n) { if (i > 0) { sum = sum + i; } i = i + 1; } return sum; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test(i64 %n) {
entry:
  %sum.addr = alloca i64
  %i.addr = alloca i64
  store i64 0, ptr %sum.addr
  store i64 0, ptr %i.addr
  br label %while0
while0:
  %tmp_0 = load i64, ptr %i.addr
  %t1 = icmp slt i64 %tmp_0, %n
  br i1 %t1, label %while_body1, label %while_exit2
while_body1:
  %t3 = add i64 0, 0
  %tmp_1 = load i64, ptr %i.addr
  %t4 = icmp sgt i64 %tmp_1, %t3
  br i1 %t4, label %then4, label %merge3
then4:
  %tmp_2 = load i64, ptr %sum.addr
  %tmp_3 = load i64, ptr %i.addr
  %t5 = add i64 %tmp_2, %tmp_3
  store i64 %t5, ptr %sum.addr
  br label %merge3
merge3:
  %t6 = add i64 0, 1
  %tmp_4 = load i64, ptr %i.addr
  %t7 = add i64 %tmp_4, %t6
  store i64 %t7, ptr %i.addr
  br label %while0
while_exit2:
  %tmp_5 = load i64, ptr %sum.addr
  ret i64 %tmp_5
}")]
    public void TestLLVMIfInWhile() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Division

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 10 / 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 10
  %t1 = add i64 0, 3
  %t2 = sdiv i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryDiv() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Modulo

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 10 % 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 10
  %t1 = add i64 0, 3
  %t2 = srem i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryMod() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Equal

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 1 == 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 1
  %t1 = add i64 0, 2
  %t2 = icmp eq i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryEqual() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Not Equal

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 1 != 2; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 1
  %t1 = add i64 0, 2
  %t2 = icmp ne i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryNotEqual() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Greater Than

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 5 > 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 5
  %t1 = add i64 0, 3
  %t2 = icmp sgt i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryGreaterThan() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Less Than or Equal

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 3 <= 5; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 3
  %t1 = add i64 0, 5
  %t2 = icmp sle i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryLessThanOrEqual() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Greater Than or Equal

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> bool { return 5 >= 3; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i8 @test() {
entry:
  %t0 = add i64 0, 5
  %t1 = add i64 0, 3
  %t2 = icmp sge i64 %t0, %t1
  ret i8 %t2
}")]
    public void TestLLVMBinaryGreaterThanOrEqual() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Bitwise And

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 12 & 10; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 12
  %t1 = add i64 0, 10
  %t2 = and i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryBitwiseAnd() => _batch.Value.AssertSemantic();

    #endregion

    #region Binary Bitwise Or

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test() -> i64 { return 12 | 10; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @test() {
entry:
  %t0 = add i64 0, 12
  %t1 = add i64 0, 10
  %t2 = or i64 %t0, %t1
  ret i64 %t2
}")]
    public void TestLLVMBinaryBitwiseOr() => _batch.Value.AssertSemantic();

    #endregion

    #region Multiple Parameters

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun mul(a: i64, b: i64, c: i64) -> i64 { return a * b + c; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @mul(i64 %a, i64 %b, i64 %c) {
entry:
  %t3 = mul i64 %a, %b
  %t4 = add i64 %t3, %c
  ret i64 %t4
}")]
    public void TestLLVMMultipleParams() => _batch.Value.AssertSemantic();

    #endregion

    #region Nested Function Calls

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun dbl(x: i64) -> i64 { return x * 2; } fun add_dbl(a: i64, b: i64) -> i64 { return dbl(a) + dbl(b); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @dbl(i64 %x) {
entry:
  %t1 = add i64 0, 2
  %t2 = mul i64 %x, %t1
  ret i64 %t2
}

define i64 @add_dbl(i64 %a, i64 %b) {
entry:
  %t2 = call i64 @dbl(i64 %a)
  %t3 = call i64 @dbl(i64 %b)
  %t4 = add i64 %t2, %t3
  ret i64 %t4
}")]
    public void TestLLVMNestedCalls() => _batch.Value.AssertSemantic();

    #endregion

    #region Recursive Fibonacci

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun fib(n: i64) -> i64 { if (n <= 1) { return n; } return fib(n - 1) + fib(n - 2); }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i64 @fib(i64 %n) {
entry:
  %t2 = add i64 0, 1
  %t3 = icmp sle i64 %n, %t2
  br i1 %t3, label %then1, label %merge0
then1:
  ret i64 %n
merge0:
  %t4 = add i64 0, 1
  %t5 = sub i64 %n, %t4
  %t6 = call i64 @fib(i64 %t5)
  %t7 = add i64 0, 2
  %t8 = sub i64 %n, %t7
  %t9 = call i64 @fib(i64 %t8)
  %t10 = add i64 %t6, %t9
  ret i64 %t10
}")]
    public void TestLLVMRecursiveFib() => _batch.Value.AssertSemantic();

    #endregion

    #region i32 Type

    [Fact]
    [BatchLLVMTest(@"
initial {
    let source = ""fun test(x: i32) -> i32 { return x + 1; }"";
    let mut compiler = new bound.EmperorPenguinCompiler();
    let result = compiler.compile(source);
    let generator = new ir.IRGenerator();
    let module = generator.generate(result);
    let emitter = new llvm.LLVMEmitter();
    let llvm_ir: string = emitter.lower(module, result);
    println(llvm_ir);
}
", @"define i32 @test(i32 %x) {
entry:
  %t1 = add i64 0, 1
  %tmp_0 = trunc i64 %t1 to i32
  %t2 = add i32 %x, %tmp_0
  ret i32 %t2
}")]
    public void TestLLVMi32Type() => _batch.Value.AssertSemantic();

    #endregion
}
