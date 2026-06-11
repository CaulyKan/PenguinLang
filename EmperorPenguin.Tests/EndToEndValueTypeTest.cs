namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for IValueType / IReferenceType / ICopy semantic rules.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndValueTypeTest
{
    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndValueTypeTest>();

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
            let q: mut Point = p;  // value types: imm→mut works (copy)
            println(cast<string>(q.x + q.y));
        }
        """,
        "7")]
    [Fact]
    public void AutoValueTypeClass() => batch.Assert();

    [BatchE2ETest("""
        class Val {
            name: string;
            impl IValueType;
            fun new(mut this, name: string) {
                this.name = name;
            }
        }
        initial {
            let v = new Val("hello");
            let w: mut Val = v;  // IValueType → imm→mut works
            println(w.name);
        }
        """,
        "hello")]
    [Fact]
    public void ExplicitIValueTypeClass() => batch.Assert();

    [BatchE2ETest("""
        class Ref {
            x: i32;
            impl IReferenceType;
            fun new(mut this, x: i32) {
                this.x = x;
            }
        }
        initial {
            let a = new Ref(1);
            let b: mut Ref = a;
            println("error_expected");
        }
        """,
        "error_expected")]
    [Fact]
    public void ReferenceTypeRejectsImmToMut() => batch.Assert();
}
