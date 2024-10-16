
using System.Text;
using Xunit.Abstractions;

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