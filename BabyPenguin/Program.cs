using CommandLine;
namespace BabyPenguin
{
    public class Options
    {

        [Value(0, Required = true, HelpText = "Input files to process")]
        public required IEnumerable<string> Files { get; set; }
    }

    public class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args).MapResult(
                Run,
                _ => -1
            );
        }

        static int Run(Options options)
        {
            // try
            // {
            var compiler = new SemanticCompiler();
            foreach (var file in options.Files)
            {
                compiler.AddFile(file);
            }

            var model = compiler.Compile();

            var vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;

            Console.WriteLine("----------- Start Execution -----------");
            vm.Run();

            if (vm.Global.EnableDebugPrint)
            {
                Console.WriteLine("----------- Console Output -----------");
                Console.WriteLine(vm.CollectOutput());
            }

            return 0;
            // }
            // catch (Exception e)
            // {
            //     Console.WriteLine(e.Message);
            //     return -1;
            // }
        }
    }
}