using System.Diagnostics;
using CommandLine;
namespace BabyPenguin
{
    public class Options
    {

        [Value(0, HelpText = "Input files to process")]
        public required IEnumerable<string> Files { get; set; }

        [Option('r', "report", HelpText = "Generate a report file")]
        public required string Report { get; set; }

        [Option('v', "verbose", Default = 3, HelpText = "Verbose output level, 0-3, higher is more verbose")]
        public int Verbose { get; set; }

        [Option('c', "compile-only", Default = false, HelpText = "Only compile dont run")]
        public bool CompileOnly { get; set; }
    }

    public class Program
    {
        public static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args).MapResult(
                Run,
                _ => -1
            );
        }

        public static int RunNormal(Options options)
        {
            // try
            // {
            var compiler = new SemanticCompiler(new ErrorReporter(Console.Out, (DiagnosticLevel)options.Verbose));
            foreach (var file in options.Files)
            {
                compiler.AddFile(file);
            }

            var model = compiler.Compile();

            if (!string.IsNullOrEmpty(options.Report))
            {
                model.WriteReport(options.Report);
            }

            var vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;

            if (!options.CompileOnly)
            {
                if (vm.Global.EnableDebugPrint)
                    Console.WriteLine("----------- Start Execution -----------");
                var code = vm.Run();

                if (vm.Global.EnableDebugPrint)
                {
                    Console.WriteLine("----------- Console Output -----------");
                    Console.WriteLine(vm.CollectOutput());
                }

                if (vm.Global.EnableDebugPrint)
                    Console.WriteLine("Program exited with code: " + code);

                return code;
            }

            return 0;

            // }
            // catch (Exception e)
            // {
            //     Console.WriteLine(e.Message);
            //     return -1;
            // }
        }


        public static int Run(Options options)
        {
            return RunNormal(options);
        }
    }
}