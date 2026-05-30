namespace EmperorPenguin.Tests;

/// <summary>
/// Tests for the pure PenguinLang linked list List/Queue implementation
/// in EmperorPenguin/std/penguin/utils.penguin. The namespace is remapped
/// from _utils to _test so we test the EmperorPenguin code directly
/// (compiled through EmperorPenguin → LLVM IR → native binary).
/// </summary>
[Collection("EndToEnd")]
public class EndToEndUtilsTest : EndToEndTestBase
{
    private static readonly string UtilsSource;

    static EndToEndUtilsTest()
    {
        var utilsPath = Path.Combine(BatchCompiler.FindProjectRoot(),
            "EmperorPenguin", "std", "penguin", "utils.penguin");
        UtilsSource = File.ReadAllText(utilsPath)
            .Replace("namespace _utils", "namespace _test");
    }

    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndUtilsTest>(prefixSource: UtilsSource);

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            println(cast<string>(cast<i64>(list.size())));
            list.push("hello");
            list.push("world");
            println(cast<string>(cast<i64>(list.size())));
            println(list.at(0).some);
            println(list.at(1).some);
        }
        """,
        "0\n2\nhello\nworld")]
    [Fact]
    public void ListPushAt() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<i64> = new _test.List<i64>();
            list.push(10);
            list.push(20);
            list.push(30);
            list.set(1, 99);
            println(cast<string>(list.at(0).some));
            println(cast<string>(list.at(1).some));
            println(cast<string>(list.at(2).some));
        }
        """,
        "10\n99\n30")]
    [Fact]
    public void ListSet() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("a");
            list.push("b");
            list.push("c");
            let popped: __builtin.Option<string> = list.pop();
            println(cast<string>(cast<i64>(list.size())));
            println(popped.some);
            println(list.at(0).some);
            println(list.at(1).some);
        }
        """,
        "2\nc\na\nb")]
    [Fact]
    public void ListPop() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("a");
            list.push("b");
            list.push("c");
            list.remove(1);
            println(cast<string>(cast<i64>(list.size())));
            println(list.at(0).some);
            println(list.at(1).some);
        }
        """,
        "2\na\nc")]
    [Fact]
    public void ListRemoveMiddle() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("only");
            let popped: __builtin.Option<string> = list.pop();
            println(cast<string>(cast<i64>(list.size())));
            println(popped.some);
        }
        """,
        "0\nonly")]
    [Fact]
    public void ListPopSingleElement() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            let result: __builtin.Option<string> = list.at(0);
            println(cast<string>(result.is_none()));
            let popped: __builtin.Option<string> = list.pop();
            println(cast<string>(popped.is_none()));
        }
        """,
        "true\ntrue")]
    [Fact]
    public void ListEmptyOperations() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<i64> = new _test.List<i64>();
            let i: mut i64 = 0;
            while (i < 5) {
                list.push(i * 10);
                i = i + 1;
            }
            let j: mut i64 = 0;
            while (j < cast<i64>(list.size())) {
                println(cast<string>(list.at(cast<u64>(j)).some));
                j = j + 1;
            }
        }
        """,
        "0\n10\n20\n30\n40")]
    [Fact]
    public void ListMultiplePush() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let q: mut _test.Queue<string> = new _test.Queue<string>();
            println(cast<string>(cast<i64>(q.size())));
            q.enqueue("first");
            q.enqueue("second");
            q.enqueue("third");
            println(cast<string>(cast<i64>(q.size())));
            println(q.peek().some);
            println(q.dequeue().some);
            println(q.dequeue().some);
            println(cast<string>(cast<i64>(q.size())));
            println(q.peek().some);
        }
        """,
        "0\n3\nfirst\nfirst\nsecond\n1\nthird")]
    [Fact]
    public void QueueEnqueueDequeue() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let q: mut _test.Queue<i64> = new _test.Queue<i64>();
            let result: __builtin.Option<i64> = q.dequeue();
            println(cast<string>(result.is_none()));
            let peeked: __builtin.Option<i64> = q.peek();
            println(cast<string>(peeked.is_none()));
        }
        """,
        "true\ntrue")]
    [Fact]
    public void QueueEmptyOperations() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let q: mut _test.Queue<string> = new _test.Queue<string>();
            q.enqueue("a");
            q.dequeue();
            println(cast<string>(cast<i64>(q.size())));
            let result: __builtin.Option<string> = q.peek();
            println(cast<string>(result.is_none()));
        }
        """,
        "0\ntrue")]
    [Fact]
    public void QueueDequeueLastElement() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("head");
            list.remove(0);
            println(cast<string>(cast<i64>(list.size())));
        }
        """,
        "0")]
    [Fact]
    public void ListRemoveHead() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("a");
            list.push("b");
            list.push("c");
            list.remove(2);
            println(cast<string>(cast<i64>(list.size())));
            println(list.at(0).some);
            println(list.at(1).some);
        }
        """,
        "2\na\nb")]
    [Fact]
    public void ListRemoveTail() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let list: mut _test.List<string> = new _test.List<string>();
            list.push("x");
            list.push("y");
            list.push("z");
            list.pop();
            list.pop();
            list.pop();
            let popped: __builtin.Option<string> = list.pop();
            println(cast<string>(cast<i64>(list.size())));
            println(cast<string>(popped.is_none()));
        }
        """,
        "0\ntrue")]
    [Fact]
    public void ListPopAllThenPop() => batch.Assert();

    [BatchE2ETest("""
        initial {
            let q: mut _test.Queue<i64> = new _test.Queue<i64>();
            q.enqueue(1);
            q.enqueue(2);
            q.enqueue(3);
            println(cast<string>(q.dequeue().some));
            println(cast<string>(q.dequeue().some));
            println(cast<string>(q.dequeue().some));
            println(cast<string>(cast<i64>(q.size())));
        }
        """,
        "1\n2\n3\n0")]
    [Fact]
    public void QueueFIFOOrder() => batch.Assert()
;
}
