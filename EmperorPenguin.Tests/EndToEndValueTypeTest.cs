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

    // Regression: a class that is recursive through an enum payload (e.g. a
    // linked-list node `class Node { next: Option<Node>; }`) must be classified
    // as a *reference* type. If it were a value type the emitter would
    // stack-allocate each `new Node()`, and once the building function returned,
    // every node address stored in `head`/`tail`/`next` would dangle — exactly
    // the crash that broke the bootstrapped compiler (its own `args()` builtin
    // builds a `_utils.List` of the command-line arguments). Here the list is
    // constructed in a separate function and returned, so the nodes' stack
    // frames are gone by the time we traverse; only heap-allocated nodes survive.
    [BatchE2ETest("""
        fun build_list() -> _utils.List<i64> {
            let list: mut _utils.List<i64> = new _utils.List<i64>();
            list.push(cast<i64>(10));
            list.push(cast<i64>(20));
            list.push(cast<i64>(30));
            return list;
        }
        initial {
            let list: _utils.List<i64> = build_list();
            let sum: mut i64 = 0;
            let i: mut i64 = 0;
            while (i < cast<i64>(list.size())) {
                sum = sum + list.at(cast<u64>(i)).some;
                i = i + 1;
            }
            println(cast<string>(sum));
        }
        """,
        "60")]
    [Fact]
    public void RecursiveClassIsReferenceType() => batch.Assert();
}
