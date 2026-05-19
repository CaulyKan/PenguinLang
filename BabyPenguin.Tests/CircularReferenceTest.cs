namespace BabyPenguin.Tests
{
    public class CircularReferenceTest(ITestOutputHelper helper) : TestBase(helper)
    {
        /// <summary>
        /// Tests that cloning a class instance with circular references
        /// via ICopy does not cause a stack overflow.
        /// Two nodes point to each other via Option&lt;Node&gt; fields.
        /// </summary>
        [Fact]
        public void CircularOptionCopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    value: mut i64 = 0;
                    next: mut Option<Node> = new Option<Node>.none();
                    impl ICopy<Self>;
                }
                initial {
                    let a : mut Node = new Node();
                    a.value = 1;
                    let b : mut Node = new Node();
                    b.value = 2;
                    a.next = new Option<Node>.some(b);
                    b.next = new Option<Node>.some(a);

                    let c : mut Node = a.copy();
                    c.value = 10;

                    print(cast<string>(a.value));
                    print(cast<string>(c.value));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("110", vm.CollectOutput());
        }

        /// <summary>
        /// Tests that cloning a class that directly references itself
        /// does not cause a stack overflow.
        /// </summary>
        [Fact]
        public void SelfReferenceCopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    value: mut i64 = 0;
                    next: mut Option<Node> = new Option<Node>.none();
                    impl ICopy<Self>;
                }
                initial {
                    let a : mut Node = new Node();
                    a.value = 1;
                    a.next = new Option<Node>.some(a);

                    let b : mut Node = a.copy();
                    b.value = 10;

                    print(cast<string>(a.value));
                    print(cast<string>(b.value));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("110", vm.CollectOutput());
        }

        /// <summary>
        /// Tests a longer circular chain: A -> B -> C -> A
        /// </summary>
        [Fact]
        public void ThreeWayCircularCopyTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    value: mut i64 = 0;
                    next: mut Option<Node> = new Option<Node>.none();
                    impl ICopy<Self>;
                }
                initial {
                    let a : mut Node = new Node();
                    a.value = 1;
                    let b : mut Node = new Node();
                    b.value = 2;
                    let c : mut Node = new Node();
                    c.value = 3;
                    a.next = new Option<Node>.some(b);
                    b.next = new Option<Node>.some(c);
                    c.next = new Option<Node>.some(a);

                    let d : mut Node = a.copy();
                    d.value = 10;

                    print(cast<string>(a.value));
                    print(cast<string>(d.value));
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("110", vm.CollectOutput());
        }
    }
}
