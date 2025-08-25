namespace BabyPenguin.Tests
{
    public class EnumTest(ITestOutputHelper helper) : TestBase(helper)
    {

        [Fact]
        public void EnumBasicTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test = new Test.a();
                        if (test is Test.a) {
                            print(""a"");
                        }
                        test = new Test.b(2);
                        if (test is Test.b) {
                            print(test.b as string);
                        } else if (test is Test.a) {
                            print(""not possible"");
                        }
                    }

                    enum Test {
                        a;
                        b : u8;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a2", vm.CollectOutput());
        }

        [Fact]
        public void EnumGenericTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test<u8> = new Test<u8>.a();
                        if (test is Test<u8>.a) {
                            print(""a"");
                        }
                        test = new Test<u8>.b(2);
                        if (test is Test<u8>.b) {
                            print(test.b as string);
                        } else if (test is Test<u8>.a) {
                            print(""not possible"");
                        }
                    }

                    enum Test <T> {
                        a;
                        b : T;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a2", vm.CollectOutput());
        }

        [Fact]
        public void EnumGenericCustomTypeTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    initial {
                        let test : mut Test<mut Foo> = new Test<mut Foo>.a();
                        if (test is Test<mut Foo>.a) {
                            print(""a"");
                        }
                        test = new Test<mut Foo>.b(new Foo());
                        if (test is Test<mut Foo>.b) {
                            print(test.b.x as string);
                            test.b.x = 1;
                            print(test.b.x as string);
                        } else if (test is Test<mut Foo>.a) {
                            print(""not possible"");
                        }
                    }

                    class Foo {
                        x: u8 = 0;
                    }

                    enum Test <T> {
                        a;
                        b : T;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("a01", vm.CollectOutput());
        }

    }
}