namespace BabyPenguin.Tests
{
    public class MutabilityTest
    {
        [Fact]
        public void MutDefinition()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    let x: i32 = 10;
                    let y: mut i32 = 20;
                }
            ");
            var model = compiler.Compile();
            var x = model.ResolveSymbol("ns.x");
            Assert.False(x!.TypeInfo.IsMutable);
            Assert.Equal("i32", x.TypeInfo.FullName());
            var y = model.ResolveSymbol("ns.y");
            Assert.True(y!.TypeInfo.IsMutable);
            Assert.Equal("mut i32", y.TypeInfo.FullName());
        }
    }
}