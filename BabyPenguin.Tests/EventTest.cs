using BabyPenguin;
using BabyPenguin.SemanticInterface;
using PenguinLangSyntax;
using Xunit;

namespace BabyPenguin.Tests
{
    public class EventTest
    {
        [Fact]
        public void TestEmitAndWaitEvent()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event;

                initial {
                    wait test_event;
                    print(""2"");
                }
                
                initial {
                    print(""1"");
                    emit test_event();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }
    }
}