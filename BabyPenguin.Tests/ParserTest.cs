
using PenguinLangParser;

namespace BabyPenguin.Tests;

public class ParserTest : TestBase
{
    public ParserTest(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact]
    public void TestSExpGeneration()
    {
        var source = "let a: i32 = 1;";
        var reporter = new ErrorReporter(new StringWriter());
        var ast = PenguinParser.Parse(source, "test.penguin", reporter);
        var compiler = new SyntaxCompiler("test.penguin", ast, reporter);
        compiler.Compile();
        var sexp = SExpSerializer.Serialize(compiler.Namespaces);

        var expectedSexp = @"EXPECTED_SEXP"; 

        testOutputHelper.WriteLine(sexp);
        Assert.Equal(expectedSexp.Replace("\r\n", "\n"), sexp.Replace("\r\n", "\n"));
    }
}
