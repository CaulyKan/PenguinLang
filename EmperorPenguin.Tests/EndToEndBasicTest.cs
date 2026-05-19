namespace EmperorPenguin.Tests;

/// <summary>
/// Basic end-to-end tests: print, arithmetic, variables, comparison, control flow, functions, string, casting.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndBasicTest : EndToEndTestBase
{
    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndBasicTest>();

    #region Print & Output

    [BatchE2ETest("""
        initial {
            println("Hello, World!");
        }
        """,
        "Hello, World!")]
    [Fact]
    public void PrintHelloWorld() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println("");
            println("x");
        }
        """,
        "\nx")]
    [Fact]
    public void PrintEmptyLine() => batch.Assert();

    #endregion

    #region Integer Arithmetic

    [BatchE2ETest("""
        initial {
            println(cast<string>(3 + 4));
        }
        """,
        "7")]
    [Fact]
    public void ArithmeticAdd() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(10 - 3));
            println(cast<string>(3 * 4));
        }
        """,
        "7\n12")]
    [Fact]
    public void ArithmeticSubMul() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(10 / 3));
            println(cast<string>(10 % 3));
        }
        """,
        "3\n1")]
    [Fact]
    public void ArithmeticDivMod() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(2 + 3 * 4));
        }
        """,
        "14")]
    [Fact]
    public void ArithmeticPrecedence() => batch.Assert();

    #endregion

    #region Variables & Assignment

    [BatchE2ETest("""
        initial {
            let x: mut i64 = 1;
            x = 42;
            println(cast<string>(x));
        }
        """,
        "42")]
    [Fact]
    public void VariableAssignment() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let a: mut i64 = 1;
            let b: mut i64 = 2;
            let t: mut i64 = a;
            a = b;
            b = t;
            println(cast<string>(a));
            println(cast<string>(b));
        }
        """,
        "2\n1")]
    [Fact]
    public void VariableSwap() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let x: i64 = 1000000;
            println(cast<string>(x + 1));
        }
        """,
        "1000001")]
    [Fact]
    public void I64Variable() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let a: i64 = 10;
            let b: i64 = 20;
            println(cast<string>(a + b));
        }
        """,
        "30")]
    [Fact]
    public void MultipleVariables() => batch.Assert();

    #endregion

    #region Comparison & Logic

    [BatchE2ETest("""
        initial {
            println(cast<string>(1 < 2));
            println(cast<string>(3 >= 3));
            println(cast<string>(2 == 2));
        }
        """,
        "true\ntrue\ntrue")]
    [Fact]
    public void ComparisonTrue() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(1 > 2));
            println(cast<string>(3 != 3));
        }
        """,
        "false\nfalse")]
    [Fact]
    public void ComparisonFalse() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(!true));
            println(cast<string>(!false));
        }
        """,
        "false\ntrue")]
    [Fact]
    public void LogicNot() => batch.Assert();

    [BatchE2ETest("""
        initial {
            println(cast<string>(12 & 10));
            println(cast<string>(12 | 10));
        }
        """,
        "8\n14")]
    [Fact]
    public void BitwiseOps() => batch.Assert();

    #endregion

    #region Control Flow

    [BatchE2ETest("""
        initial {
            if (1 > 0) {
                println("yes");
            } else {
                println("no");
            }
        }
        """,
        "yes")]
    [Fact]
    public void IfElseTrue() => batch.Assert();

    [BatchE2ETest("""
        initial {
            if (0 > 1) {
                println("yes");
            } else {
                println("no");
            }
        }
        """,
        "no")]
    [Fact]
    public void IfElseFalse() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let x: i64 = 15;
            if (x > 10) {
                if (x > 20) {
                    println("big");
                } else {
                    println("mid");
                }
            }
        }
        """,
        "mid")]
    [Fact]
    public void NestedIf() => batch.Assert();

    [Fact]
    public void WhileLoop()
    {
        Assert.Equal("01234\n", RunEndToEnd("""
            initial {
                let i: mut i64 = 0;
                while (i < 5) {
                    print(cast<string>(i));
                    i = i + 1;
                }
                println("");
            }
            """));
    }

    [BatchE2ETest("""
        initial {
            let sum: mut i64 = 0;
            let i: mut i64 = 1;
            while (i <= 10) {
                sum = sum + i;
                i = i + 1;
            }
            println(cast<string>(sum));
        }
        """,
        "55")]
    [Fact]
    public void WhileSum() => batch.Assert();

    #endregion

    #region Functions

    [BatchE2ETest("""
        fun add(a: i64, b: i64) -> i64 {
            return a + b;
        }
        initial {
            println(cast<string>(add(3, 4)));
        }
        """,
        "7")]
    [Fact]
    public void FunctionCall() => batch.Assert();

    [BatchE2ETest("""
        fun mul3(a: i64, b: i64, c: i64) -> i64 {
            return a * b * c;
        }
        initial {
            println(cast<string>(mul3(2, 3, 4)));
        }
        """,
        "24")]
    [Fact]
    public void FunctionMultiParam() => batch.Assert();

    [BatchE2ETest("""
        fun greet() {
            println("hi");
        }
        initial {
            greet();
        }
        """,
        "hi")]
    [Fact]
    public void VoidFunction() => batch.Assert();

    [BatchE2ETest("""
        fun fact(n: i64) -> i64 {
            if (n <= 1) { return 1; }
            return n * fact(n - 1);
        }
        initial {
            println(cast<string>(fact(10)));
        }
        """,
        "3628800")]
    [Fact]
    public void RecursiveFactorial() => batch.Assert();

    [BatchE2ETest("""
        fun fib(n: i64) -> i64 {
            if (n <= 1) { return n; }
            return fib(n - 1) + fib(n - 2);
        }
        initial {
            println(cast<string>(fib(10)));
        }
        """,
        "55")]
    [Fact]
    public void RecursiveFibonacci() => batch.Assert();

    [BatchE2ETest("""
        fun dbl(x: i64) -> i64 {
            return x * 2;
        }
        fun add_dbl(a: i64, b: i64) -> i64 {
            return dbl(a) + dbl(b);
        }
        initial {
            println(cast<string>(add_dbl(3, 4)));
        }
        """,
        "14")]
    [Fact]
    public void NestedFunctionCalls() => batch.Assert();

    #endregion

    #region String Operations

    [BatchE2ETest("""
        initial {
            let s: string = "hello" + " " + "world";
            println(s);
        }
        """,
        "hello world")]
    [Fact]
    public void StringConcat() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let x: i64 = 42;
            println("x=" + cast<string>(x));
        }
        """,
        "x=42")]
    [Fact]
    public void StringPrintInt() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let a: string = "a" + "b" + "c";
            println(a);
        }
        """,
        "abc")]
    [Fact]
    public void StringMultiConcat() => batch.Assert();

    #endregion

    #region Type Casting

    [BatchE2ETest("""
        initial {
            let n: i64 = 123;
            let s: string = cast<string>(n);
            println(s);
        }
        """,
        "123")]
    [Fact]
    public void CastI64ToString() => batch.Assert();

    #endregion

    #region Global Variables

    [BatchE2ETest("""
        let x: i64 = 42;
        initial {
            println(cast<string>(x));
        }
        """,
        "42")]
    [Fact]
    public void GlobalVariable() => batch.Assert();

    [BatchE2ETest("""
        let counter: mut i64 = 0;
        initial {
            println(cast<string>(counter));
            counter = 1;
            println(cast<string>(counter));
        }
        """,
        "0\n1")]
    [Fact]
    public void MutableGlobalVariable() => batch.Assert();

    [BatchE2ETest("""
        namespace Foo {
            let msg: string = "hello";
        }
        initial {
            println(Foo.msg);
        }
        """,
        "hello")]
    [Fact]
    public void NamespaceGlobalVariable() => batch.Assert();

    [BatchE2ETest("""
        let a: i64 = 10;
        let b: i64 = 20;
        initial {
            println(cast<string>(a + b));
        }
        """,
        "30")]
    [Fact]
    public void GlobalVariableArithmetic() => batch.Assert();

    #endregion

    #region Garbage Collection

    /// <summary>
    /// Verify GC does not corrupt reachable objects during collection.
    /// Creates many short-lived objects, forces GC, then checks that a
    /// retained object is still valid.
    /// </summary>
    [BatchE2ETest("""
        class Node {
            val: i64;
            fun new(mut this, v: i64) {
                this.val = v;
            }
        }
        initial {
            let anchor = new Node(999);
            let i: mut i64 = 0;
            while (i < 1000) {
                let tmp = new Node(i);
                i = i + 1;
            }
            _emperor_gc_collect();
            println(cast<string>(anchor.val));
        }
        """,
        "999")]
    [Fact]
    public void GCRetainsReachable() => batch.Assert();

    /// <summary>
    /// Verify GC does not collect string references held in local variables.
    /// Allocates many strings, forces GC, then reads back the retained string.
    /// </summary>
    [BatchE2ETest("""
        initial {
            let s: string = "alive";
            let i: mut i64 = 0;
            while (i < 1000) {
                let tmp: string = "garbage" + cast<string>(i);
                i = i + 1;
            }
            _emperor_gc_collect();
            println(s);
        }
        """,
        "alive")]
    [Fact]
    public void GCStringSurvives() => batch.Assert();

    /// <summary>
    /// Verify GC does not collect string globals.
    /// A global string is set, then many allocations trigger GC, then the global is read.
    /// </summary>
    [BatchE2ETest("""
        let msg: string = "global_alive";
        initial {
            let i: mut i64 = 0;
            while (i < 1000) {
                let tmp: string = "noise" + cast<string>(i);
                i = i + 1;
            }
            _emperor_gc_collect();
            println(msg);
        }
        """,
        "global_alive")]
    [Fact]
    public void GCGlobalStringSurvives() => batch.Assert();

    /// <summary>
    /// Verify that _emperor_gc_collect() actually frees memory.
    /// Allocates objects, records heap size, forces GC, records heap size again.
    /// Heap after collection should be smaller than before.
    /// </summary>
    [BatchE2ETest("""
        class Node {
            val: i64;
            fun new(mut this, v: i64) {
                this.val = v;
            }
        }
        initial {
            let i: mut i64 = 0;
            while (i < 500) {
                let tmp = new Node(i);
                i = i + 1;
            }
            let before: i64 = _emperor_gc_info();
            _emperor_gc_collect();
            let after: i64 = _emperor_gc_info();
            if (after < before) {
                println("freed");
            } else {
                println("no_free");
            }
        }
        """,
        "freed")]
    [Fact]
    public void GCCollectFreesMemory() => batch.Assert();

    /// <summary>
    /// Verify _emperor_gc_info() returns a reasonable non-zero value after allocations.
    /// Uses string concatenation to force a GC-tracked allocation.
    /// </summary>
    [BatchE2ETest("""
        initial {
            let before: i64 = _emperor_gc_info();
            let s: string = "hello" + " world";
            let after: i64 = _emperor_gc_info();
            if (after > before) {
                println("grew");
            } else {
                println("no_grow");
            }
        }
        """,
        "grew")]
    [Fact]
    public void GCInfoReflectsAllocations() => batch.Assert();

    #endregion
}
