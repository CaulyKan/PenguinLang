using System.Diagnostics;
using CommandLine;
namespace BabyPenguin
{
    public class Options
    {

        [Value(0, HelpText = "Input files to process")]
        public IEnumerable<string> Files { get; set; }
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

        static void RunNormal(Options options)
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

            // }
            // catch (Exception e)
            // {
            //     Console.WriteLine(e.Message);
            //     return -1;
            // }
        }

        static void RunDAP(Options options)
        {
            var dap = new DAP();
            dap.Protocol.LogMessage += (sender, e) => Debug.WriteLine(e.Message);
            dap.Protocol.Run();
        }

        static int Run(Options options)
        {
            if (options.Files.Count() == 0)
            {
                RunDAP(options);
            }
            else
            {
                RunNormal(options);
            }
            return 0;
        }
    }
}