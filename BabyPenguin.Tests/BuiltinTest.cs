namespace BabyPenguin.Tests
{
    public class BuiltinTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void PrintTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    print(""hello, "");
                    println(""world!"");
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"hello, world!{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void OptionTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val a : Option<u32> = new Option<u32>.some(10);
                    println(a.is_some() as string);
                    println(a.is_none() as string);
                    println(a.value_or(9) as string);

                    val b : Option<u32> = new Option<u32>.none();
                    println(b.is_some() as string);
                    println(b.is_none() as string);
                    println(b.value_or(9) as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"true{EOL}false{EOL}10{EOL}false{EOL}true{EOL}9{EOL}", vm.CollectOutput());
        }

        [Fact]
        public void RangeTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    val rg : IIterator<i64> = range(0, 5);
                    while(true) {
                        val n : Option<i64> = rg.next();
                        if (n.is_none())
                            break;
                        else 
                            print(n.some as string);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"01234", vm.CollectOutput());
        }

        [Fact]
        public void CopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Foo {
                    var x : i64;
                    var y : i64;

                    impl ICopiable<Self>;
                }
                initial {
                    var a : Foo = new Foo();
                    a.x = 1;
                    a.y = 2;
                    var b : Foo = a.copy();
                    b.x = 3;
                    b.y = 4;
                    print(a.x as string);
                    print(a.y as string);
                    print(b.x as string);
                    print(b.y as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal($"1234", vm.CollectOutput());
        }
    }
}