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

        [Option('v', "verbose", Default = 0, HelpText = "Verbose output level, 0-3, higher is more verbose")]
        public int Verbose { get; set; }

        [Option('c', "compile-only", Default = false, HelpText = "Only compile dont run")]
        public bool CompileOnly { get; set; }

        [Option('q', "quiet", Default = false, HelpText = "Quiet mode: suppress debug trace, only show program output")]
        public bool Quiet { get; set; }
    }

    public class Program
    {
        public static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args).MapResult(
                (options) => Run(options, args),
                _ => -1
            );
        }

        public static int RunNormal(Options options, string[] args)
        {
            // try
            // {
            var compiler = new SemanticCompiler(new ErrorReporter(Console.Out, (DiagnosticLevel)options.Verbose));

            // Find "--" separator: args before are files for BabyPenguin, args after are program args
            int separatorIndex = Array.IndexOf(args, "--");
            int fileCount = separatorIndex >= 0 ? separatorIndex : args.Length;
            // Filter out option flags from the file count
            var filesBeforeSeparator = args.Take(fileCount)
                .Where(a => !a.StartsWith("-"))
                .ToList();

            // Check if any input is a .penguins file
            var projectFiles = filesBeforeSeparator.Where(f => f.EndsWith(".penguins", StringComparison.OrdinalIgnoreCase)).ToList();
            var regularFiles = filesBeforeSeparator.Where(f => !f.EndsWith(".penguins", StringComparison.OrdinalIgnoreCase)).ToList();

            // Handle project files
            foreach (var projectFile in projectFiles)
            {
                compiler.AddProject(projectFile);
            }

            // Handle regular .penguin files
            foreach (var file in regularFiles)
            {
                compiler.AddFile(file);
            }

            // If no files specified, search for .penguins in current directory
            if (!projectFiles.Any() && !regularFiles.Any())
            {
                var currentDir = Directory.GetCurrentDirectory();
                var foundProjectFile = FindPenguinsProjectFile(currentDir);

                if (foundProjectFile != null)
                {
                    Console.WriteLine($"Found project file: {foundProjectFile}");
                    compiler.AddProject(foundProjectFile);
                }
                else
                {
                    Console.Error.WriteLine("No input files specified and no .penguins project file found.");
                    return 1;
                }
            }

            var model = compiler.Compile();

            if (!string.IsNullOrEmpty(options.Report))
            {
                model.WriteReport(options.Report);
            }

            var vm = new BabyPenguinVM(model);
            // Program args: everything after "--" separator, or empty if no separator
            if (separatorIndex >= 0)
            {
                vm.Global.CommandLineArgs = args.Skip(separatorIndex + 1).ToArray();
            }
            else
            {
                vm.Global.CommandLineArgs = args.Skip(filesBeforeSeparator.Count).ToArray();
            }
            vm.Global.EnableDebugPrint = !options.Quiet;

            if (!options.CompileOnly)
            {
                if (vm.Global.EnableDebugPrint)
                    Console.WriteLine("----------- Start Execution -----------");
                var code = vm.Run();

                if (options.Quiet)
                {
                    // In quiet mode, write program output directly to stdout
                    Console.Write(vm.CollectOutput());
                }
                else if (vm.Global.EnableDebugPrint)
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


        public static int Run(Options options, string[] args)
        {
            return RunNormal(options, args);
        }

        /// <summary>
        /// Searches for a .penguins file in the current directory and parent directories
        /// </summary>
        /// <param name="startingDirectory">Directory to start searching from</param>
        /// <returns>Path to .penguins file if found, null otherwise</returns>
        private static string? FindPenguinsProjectFile(string startingDirectory)
        {
            var currentDir = new DirectoryInfo(startingDirectory);

            // Search up to 10 directory levels
            for (int i = 0; i < 10; i++)
            {
                var files = Directory.GetFiles(currentDir.FullName, "*.penguins");

                if (files.Length > 0)
                {
                    return files[0]; // Return first .penguins file found
                }

                if (currentDir.Parent == null)
                {
                    break;
                }

                currentDir = currentDir.Parent;
            }

            return null;
        }
    }
}