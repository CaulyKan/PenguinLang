using BabyPenguin;

namespace EmperorPenguin.Tests;

public class ASTTokenizationTest
{
    private static readonly BatchResults Batch = BatchCompiler.InitTokenizeBatch<ASTTokenizationTest>();

    [Fact]
    [BatchTokenizeTest("42")]
    public void TokenizeDecimalInteger()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 42", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("0xFF")]
    public void TokenizeHexInteger()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 0xFF", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("0b1010")]
    public void TokenizeBinaryInteger()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 0b1010", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("1e10")]
    public void TokenizeFloatWithExponent()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 1e10", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("3.14e+2")]
    public void TokenizeFloatWithFractionAndExponent()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 3.14e+2", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("3.14f")]
    public void TokenizeFloatWithSuffix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 3.14f", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("42u")]
    public void TokenizeIntegerWithSuffix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 42u", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("100LL")]
    public void TokenizeLongLongSuffix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 100LL", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("'a'")]
    public void TokenizeCharacterConstant()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 'a'", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("L'x'")]
    public void TokenizeCharacterConstantWithLEncoding()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: L'x'", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("\"hello\"")]
    public void TokenizeStringLiteral()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Contains("Stringliteral: \"hello\"", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("u8\"hello\"")]
    public void TokenizeStringLiteralWithU8Prefix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Contains("Stringliteral: u8\"hello\"", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("L\"hello\"")]
    public void TokenizeStringLiteralWithLPrefix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Contains("Stringliteral: L\"hello\"", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("42 // comment\n43")]
    public void TokenizeLineComment()
    {
        var result = Batch.GetResult();
        Assert.Contains("Constant: 42", result);
        Assert.Contains("Constant: 43", result);
        Assert.DoesNotContain("comment", result);
    }

    [Fact]
    [BatchTokenizeTest("42 /* block\ncomment */ 43")]
    public void TokenizeBlockComment()
    {
        var result = Batch.GetResult();
        Assert.Contains("Constant: 42", result);
        Assert.Contains("Constant: 43", result);
        Assert.DoesNotContain("block", result);
    }

    [Fact]
    [BatchTokenizeTest("0xFF + 0b1010")]
    public void TokenizeMixedExpression()
    {
        var result = Batch.GetResult();
        Assert.Contains("Constant: 0xFF", result);
        Assert.Contains("Plus: +", result);
        Assert.Contains("Constant: 0b1010", result);
    }

    [Fact]
    [BatchTokenizeTest("0xDEADu")]
    public void TokenizeHexWithSuffix()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 0xDEADu", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("0777")]
    public void TokenizeOctalInteger()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: 0777", lines[0]);
    }

    [Fact]
    [BatchTokenizeTest("'\\n'")]
    public void TokenizeEscapedCharConstant()
    {
        var lines = Batch.GetResult().Split('\n');
        Assert.Equal("Constant: '\\n'", lines[0]);
    }
}
