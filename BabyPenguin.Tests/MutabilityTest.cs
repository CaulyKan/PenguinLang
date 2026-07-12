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
            Assert.Equal(Mutability.Immutable, x!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Immutable, x!.IsMutable);
            Assert.Equal("!mut i32", x.TypeInfo.FullName());
            var y = model.ResolveSymbol("ns.y");
            Assert.Equal(Mutability.Mutable, y!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Mutable, y!.IsMutable);
            Assert.Equal("mut i32", y.TypeInfo.FullName());
        }

        [Fact]
        public void MutClassDefinition()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class A{
                        x: i32 = 10;
                        y: mut i32 = 20;
                        z: !mut i32 = 30;
                    }

                    let a1 : A;
                    let a2 : mut A;
                }
            ");
            var model = compiler.Compile();
            var a1x = model.ResolveSymbol("ns.a1.x");
            Assert.Equal(Mutability.Auto, a1x!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Immutable, a1x!.IsMutable);
            Assert.Equal("i32", a1x.TypeInfo.FullName());
            var a1y = model.ResolveSymbol("ns.a1.y");
            Assert.Equal(Mutability.Mutable, a1y!.IsMutable);
            Assert.Equal("mut i32", a1y.TypeInfo.FullName());
            var a1z = model.ResolveSymbol("ns.a1.z");
            Assert.Equal(Mutability.Immutable, a1z!.IsMutable);
            Assert.Equal("!mut i32", a1z.TypeInfo.FullName());
            var a2x = model.ResolveSymbol("ns.a2.x");
            Assert.Equal(Mutability.Mutable, a2x!.IsMutable);
            Assert.Equal("i32", a2x.TypeInfo.FullName());
        }

    }
}
