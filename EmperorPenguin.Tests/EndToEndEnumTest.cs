namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for enum features.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndEnumTest : EndToEndTestBase
{
    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndEnumTest>();

    [BatchE2ETest("""
        enum Color { Red; Green; Blue; }
        fun color_name(c: Color) -> string {
            if (c is Color.Red) { return "red"; }
            if (c is Color.Green) { return "green"; }
            return "blue";
        }
        initial {
            println(color_name(new Color.Red()));
            println(color_name(new Color.Blue()));
        }
        """,
        "red\nblue")]
    [Fact]
    public void EnumSimple() => batch.Assert();

    [BatchE2ETest("""
        enum OptVal { some: i32; none; }
        fun get_or_default(o: OptVal, def: i32) -> i32 {
            if (o is OptVal.some) { return o.some; }
            return def;
        }
        initial {
            let a = new OptVal.some(42);
            let b = new OptVal.none();
            println(cast<string>(get_or_default(a, 0)));
            println(cast<string>(get_or_default(b, -1)));
        }
        """,
        "42\n-1")]
    [Fact]
    public void EnumWithPayload() => batch.Assert();

    [BatchE2ETest("""
        enum ResVal { ok: i32; err; }
        initial {
            let r: ResVal = new ResVal.ok(100);
            if (r is ResVal.ok) {
                println("ok:" + cast<string>(r.ok));
            } else {
                println("err");
            }
        }
        """,
        "ok:100")]
    [Fact]
    public void EnumMatchBranch() => batch.Assert();

    [BatchE2ETest("""
        enum Shape { circle: i32; rect: i32; }
        fun area(s: Shape) -> i32 {
            if (s is Shape.circle) {
                let r: i32 = s.circle;
                return r * r * 3;
            }
            let side: i32 = s.rect;
            return side * side;
        }
        initial {
            println(cast<string>(area(new Shape.circle(5))));
            println(cast<string>(area(new Shape.rect(4))));
        }
        """,
        "75\n16")]
    [Fact]
    public void EnumMultipleVariants() => batch.Assert();

    [BatchE2ETest("""
        enum BoolVal { yes; no; }
        fun to_bool(b: BoolVal) -> bool {
            if (b is BoolVal.yes) { return true; }
            return false;
        }
        initial {
            println(cast<string>(to_bool(new BoolVal.yes())));
            println(cast<string>(to_bool(new BoolVal.no())));
        }
        """,
        "true\nfalse")]
    [Fact]
    public void EnumInFunction() => batch.Assert();

    [BatchE2ETest("""
        enum Color { red; green; blue; }
        fun get_color() -> Color {
            return new Color.red();
        }
        fun check_color(c: Color) -> string {
            if (c is Color.red) { return "red"; }
            return "other";
        }
        initial {
            let c = get_color();
            println(check_color(c));
        }
        """,
        "red")]
    [Fact]
    public void SmallEnumReturn() => batch.Assert();

    [BatchE2ETest("""
        enum OptStr { some: string; none; }
        fun make_some(s: string) -> OptStr {
            return new OptStr.some(s);
        }
        initial {
            let o = make_some("hello");
            if (o is OptStr.some) {
                println(o.some);
            } else {
                println("none");
            }
        }
        """,
        "hello")]
    [Fact]
    public void LargeEnumPtrPayloadReturn() => batch.Assert();

    [BatchE2ETest("""
        enum OptI64 { some: i64; none; }
        fun make_some(v: i64) -> OptI64 {
            return new OptI64.some(v);
        }
        initial {
            let o = make_some(cast<i64>(42));
            if (o is OptI64.some) {
                println(cast<string>(o.some));
            } else {
                println("none");
            }
        }
        """,
        "42")]
    [Fact]
    public void LargeEnumI64PayloadReturn() => batch.Assert();

    [BatchE2ETest("""
        class Point {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) {
                this.x = x;
                this.y = y;
            }
        }
        fun make_point(x: i32, y: i32) -> Point {
            return new Point(x, y);
        }
        initial {
            let p = make_point(3, 4);
            println(cast<string>(p.x + p.y));
        }
        """,
        "7")]
    [Fact]
    public void ValueTypeClassReturn() => batch.Assert();

    [BatchE2ETest("""
        enum Color { red; green; blue; }
        fun color_name(c: Color) -> string {
            return cast<string>(c);
        }
        initial {
            println(color_name(new Color.red()));
            println(color_name(new Color.blue()));
        }
        """,
        "0\n2")]
    [Fact]
    public void EnumCastToString() => batch.Assert();
}
