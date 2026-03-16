namespace BabyPenguin.Tests
{
    public class EmperorPenguinTest(ITestOutputHelper helper) : TestBase(helper)
    {
        private string GetTestPath(string fileName) => System.IO.Path.Combine(System.Environment.CurrentDirectory, "../../../../", "EmperorPenguin.Tests", "TestFiles", fileName).Replace("\\", "/");

        private string GetBabyPenguinAst(string path)
        {
            var text = System.IO.File.ReadAllText(path);
            var reporter = new ErrorReporter(this);
            var ast = PenguinParser.Parse(text, path, reporter);
            var syntaxCompiler = new SyntaxCompiler(text, ast, reporter);
            syntaxCompiler.Compile();
            return syntaxCompiler.GenerateAstReport();
        }

        private (int, string) RunScript(string script)
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(script);
            var emperorPenguinPath = System.IO.Path.Combine(System.Environment.CurrentDirectory, "../../../../", "EmperorPenguin").Replace("\\", "/");
            var files = Directory.GetFiles(emperorPenguinPath, "*.penguin", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                compiler.AddFile(file);
            }
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            var code = vm.Run();
            return (code, vm.CollectOutput());
        }

        // [Fact]
        public void ParseAST_HelloWorld()
        {
            var inputPath = GetTestPath("hello_world.penguin");
            var script = "initial {\n"
                       + "  let compiler = new EmperorPenguin.Compiler();\n"
                       + $"  let code = file_read_text(\"{inputPath}\");\n"
                       + $"  let cu = compiler.parseAST(\"{inputPath}\", code);\n"
                       + "  println(EmperorPenguin.printAst(cu));\n"
                       + "}\n";

            var (code, output) = RunScript(script);
            var golden = GetBabyPenguinAst(inputPath);
            Assert.Equal(0, code);
            Assert.Equal(golden + EOL, output);
        }

    }
}
