using System;
using System.Linq;
using System.Reflection;
using BabyPenguin.CSharpBackend.Runtime;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>
    /// Invoked from Program.RunNormal when --backend cs: lowers the model to C#,
    /// compiles via DiskCompiler, loads, and runs __builtin__main against a shared
    /// RuntimeGlobal so I/O/exit are byte-identical to the interpreter.
    /// </summary>
    public static class CSharpBackendRunner
    {
        public static int Run(SemanticModel? model, Options options, string[] args, int separatorIndex)
        {
            // Standalone exe mode: lower to C# with a Main, build an Exe + copy BabyPenguin deps,
            // print the path, and exit. The resulting exe runs the program without BabyPenguin hosting.
            if (!string.IsNullOrEmpty(options.CsOut))
            {
                var program = new CSharpBackend().Lower(model!, standalone: true);
                var exePath = new DiskCompiler().CompileExe(program.Sources, options.CsOut, keepCs: options.KeepCs);
                Console.WriteLine($"[cs backend] standalone exe: {exePath}");
                Console.WriteLine($"[cs backend] run with: {exePath} <program args>");
                return 0;
            }

            Assembly assembly;

            if (options.RunOnly != "")
            {
                assembly = DiskCompiler.LoadAssembly(options.RunOnly);
            }
            else
            {
                var program = new CSharpBackend().Lower(model!);
                assembly = new DiskCompiler().CompileAndLoad(program.Sources, keepCs: options.KeepCs);
            }

            var global = new RuntimeGlobal();
            global.CommandLineArgs = ProgramArgs(args, separatorIndex);
            if (options.Quiet) global.PrintFunc = _ => { };
            GlobalState.Global = global;
            GlobalState.Args = global.CommandLineArgs;

            var generated = assembly.GetType("BabyPenguinCompiled.Generated")
                ?? throw new CSharpBackendException("Generated type not found in compiled assembly");
            var entry = generated.GetMethod("__builtin__main", BindingFlags.Static | BindingFlags.Public)
                ?? throw new CSharpBackendException("__builtin__main entry not found");

            int exitCode;
            try
            {
                entry.Invoke(null, null);
                exitCode = global.ExitCode;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is ProgramExitException)
            {
                exitCode = global.ExitCode;
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap so the real runtime error + stack surfaces (the lowered code has no PDB frames,
                // but the message + any partial output is far more useful than an opaque invocation error).
                var inner = tie.InnerException;
                Console.Error.WriteLine($"[cs backend runtime error] {inner?.GetType().Name}: {inner?.Message}");
                Console.Error.WriteLine(inner?.StackTrace);
                throw new CSharpBackendException($"compiled program threw {inner?.GetType().Name}: {inner?.Message}");
            }

            var output = global.Output.ToString();
            if (!string.IsNullOrEmpty(output))
                Console.Write(output);
            return exitCode;
        }

        private static string[] ProgramArgs(string[] args, int separatorIndex)
        {
            if (separatorIndex >= 0)
                return args.Skip(separatorIndex + 1).ToArray();
            return Array.Empty<string>();
        }
    }
}
