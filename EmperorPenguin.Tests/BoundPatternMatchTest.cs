using static EmperorPenguin.Tests.BatchCompiler;

namespace EmperorPenguin.Tests;

/// <summary>
/// Tests for enum pattern matching with `is` expression in bound tree.
/// Verifies that `is` expressions are correctly bound and generate proper IR.
/// </summary>
public class BoundPatternMatchTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundPatternMatchTest>());

    #region Is Expression Binding Tests

    [Fact]
    [BatchBoundTest(@"
        initial {
            let source = ""enum Option { some: i64; none; } fun is_some(o: Option) -> bool { return o is Option.some; }"";
            let mut compiler = new bound.EmperorPenguinCompiler();
            let result = compiler.compile(source);
            let defs = result.definitions;
            println(""defs="" + cast<string>(cast<i64>(defs.size())));
        }
        ", @"defs=2")]
    public void TestIsExpressionCompiles() => _batch.Value.Assert();

    #endregion
}
