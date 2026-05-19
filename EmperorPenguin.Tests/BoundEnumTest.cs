using static EmperorPenguin.Tests.BatchCompiler;

namespace EmperorPenguin.Tests;

/// <summary>
/// Tests for enum definition and variant creation in the bound tree.
/// </summary>
public class BoundEnumTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundEnumTest>());

    #region Enum Definition Tests

    [Fact]
    [BatchBoundTest(@"
	initial {
	    let source = ""enum Test { A; B; }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    println(""defs="" + cast<string>(cast<i64>(result.definitions.size())));
	}
	", @"defs=1")]
    public void TestEnumDefinition() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    let def = result.definitions.at(cast<u64>(0)).some;
	    if (def is bound.BoundDefinition.enum_def) {
	        let e = def.enum_def;
	        println(""members="" + cast<string>(cast<i64>(e.members.size())));
	        println(""name="" + e.name);
	    }
	}
	", @"members=2
name=Option")]
    public void TestEnumWithPayload() => _batch.Value.Assert();

    #endregion

    #region Enum Variant Access Tests

    [Fact]
    [BatchBoundTest(@"
	initial {
	    let source = ""enum Test { A; B; } fun test() -> Test { return Test.A; }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    println(""defs="" + cast<string>(cast<i64>(result.definitions.size())));
	}
	", @"defs=2")]
    public void TestEnumVariantAccess() => _batch.Value.Assert();

    #endregion

    #region Pattern Matching Tests (is expression)

    [Fact]
    [BatchBoundTest(@"
	initial {
	    let source = ""enum Option { some: i64; none; } fun test(o: Option) -> i64 { if (o is Option.some) { return 0; } else { return 1; } }"";
	    let mut compiler = new bound.EmperorPenguinCompiler();
	    let result = compiler.compile(source);
	    println(""defs="" + cast<string>(cast<i64>(result.definitions.size())));
	}
	", @"defs=2")]
    public void TestEnumPatternMatching() => _batch.Value.Assert();

    #endregion
}
