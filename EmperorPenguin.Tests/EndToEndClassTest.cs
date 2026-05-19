namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for class features.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndClassTest : EndToEndTestBase
{
    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndClassTest>();

    [BatchE2ETest("""
        class Point {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) {
                this.x = x;
                this.y = y;
            }
        }
        initial {
            let p = new Point(3, 4);
            println(cast<string>(p.x));
            println(cast<string>(p.y));
        }
        """,
        "3\n4")]
    [Fact]
    public void ClassBasicField() => batch.Assert();

    [BatchE2ETest("""
        class Point {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) {
                this.x = x;
                this.y = y;
            }
            fun sum(this) -> i32 {
                return this.x + this.y;
            }
        }
        initial {
            let p = new Point(3, 4);
            println(cast<string>(p.sum()));
        }
        """,
        "7")]
    [Fact]
    public void ClassMethodReturn() => batch.Assert();

    [BatchE2ETest("""
        class Foo {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) {
                this.x = x;
                this.y = y;
            }
            fun to_str(this) -> string {
                return "foo=" + cast<string>(this.x) + "," + cast<string>(this.y);
            }
        }
        initial {
            let f = new Foo(1, 2);
            println(f.to_str());
        }
        """,
        "foo=1,2")]
    [Fact]
    public void ClassToString() => batch.Assert();

    [BatchE2ETest("""
        class Counter {
            val: mut i32;
            fun new(mut this) {
                this.val = 0;
            }
            fun increment(mut this) {
                this.val = this.val + 1;
            }
            fun get(this) -> i32 {
                return this.val;
            }
        }
        initial {
            let c = new Counter();
            c.increment();
            c.increment();
            c.increment();
            println(cast<string>(c.get()));
        }
        """,
        "3")]
    [Fact]
    public void ClassMutableField() => batch.Assert();

    [BatchE2ETest("""
        class Calc {
            val: mut i32;
            fun new(mut this, v: i32) {
                this.val = v;
            }
            fun dbl(this) -> i32 {
                return this.val * 2;
            }
            fun neg(this) -> i32 {
                return -this.val;
            }
        }
        initial {
            let c = new Calc(5);
            println(cast<string>(c.dbl()));
            println(cast<string>(c.neg()));
        }
        """,
        "10\n-5")]
    [Fact]
    public void ClassMultipleMethods() => batch.Assert();

    [BatchE2ETest("""
        class Box {
            value: i32;
            fun new(mut this, v: i32) {
                this.value = v;
            }
            fun get(this) -> i32 {
                return this.value;
            }
        }
        fun wrap(x: i32) -> Box {
            return new Box(x);
        }
        initial {
            let b = wrap(99);
            println(cast<string>(b.get()));
        }
        """,
        "99")]
    [Fact]
    public void ClassWithMethodChain() => batch.Assert();

    [BatchE2ETest("""
        class Pair {
            a: i32;
            b: i32;
            fun new(mut this, a: i32, b: i32) {
                this.a = a;
                this.b = b;
            }
        }
        initial {
            let p = new Pair(1, 2);
            let sum: i32 = p.a + p.b;
            println(cast<string>(sum));
        }
        """,
        "3")]
    [Fact]
    public void ClassFieldAssign() => batch.Assert();

    [BatchE2ETest("""
        class Math {
            fun new(mut this) {}
            fun add_mul(this, x: i32, y: i32, z: i32) -> i32 {
                return (x + y) * z;
            }
        }
        initial {
            let m = new Math();
            println(cast<string>(m.add_mul(2, 3, 4)));
        }
        """,
        "20")]
    [Fact]
    public void ClassMethodWithParam() => batch.Assert();
}
