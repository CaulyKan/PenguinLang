
global using System.Text;
global using Xunit.Abstractions;
global using PenguinLangSyntax;
global using BabyPenguin.VirtualMachine;
global using BabyPenguin.Symbol;
global using BabyPenguin.SemanticNode;

namespace BabyPenguin.Tests
{
    public class TestBase(ITestOutputHelper testOutputHelper) : TextWriter
    {
        protected readonly ITestOutputHelper testOutputHelper = testOutputHelper;

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            testOutputHelper.WriteLine(value);
        }
    }
}