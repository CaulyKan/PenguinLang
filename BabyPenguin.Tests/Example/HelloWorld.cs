namespace PenguinLangAntlr.Tests;

public class HelloWorldTest
{
    [Fact]
    public void SyntaxTest()
    {
        var parser = new PenguinParser(@"
        initial {
            print(""Hello, world!"");
        }
        ", "anonymous");
        Assert.True(parser.Parse());
    }

    [Fact]
    public void SyntaxErrorTest()
    {
        var parser = new PenguinParser(@"
        initial {
            aaa
        }
        ", "anonymous");
        Assert.False(parser.Parse());
        Assert.Contains("no viable alternative at input", parser.Reporter.GenerateReport());
    }
}