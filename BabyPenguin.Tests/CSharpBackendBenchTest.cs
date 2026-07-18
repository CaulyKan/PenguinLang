using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BabyPenguin.CSharpBackend;
using BabyPenguin.CSharpBackend.Runtime;
using BabyPenguin.VirtualMachine;
using Xunit;
using Xunit.Abstractions;

namespace BabyPenguin.Tests
{
    public class CSharpBackendBenchTest(ITestOutputHelper helper) : TestBase(helper)
    {
        private const string FibSource = """
fun fib(n: i64) -> i64 {
    if (n < 2) { return n; }
    return fib(n - 1) + fib(n - 2);
}
initial {
    println(fib(28));
}
""";

        [Fact]
        public void FibCompiledVsInterpreter()
        {
            const int N = 28;
            const long Expected = 317811; // fib(28)

            // --- Compile + lower ---
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(FibSource, "fib_bench.penguin");
            var model = compiler.Compile();
            var ir = new IRGenerator(model).Generate();

            var fibFunc = ir.Functions.Values.FirstOrDefault(f => f.DisplayName.EndsWith(".fib"))
                ?? ir.Functions.Values.First(f => f.Name.EndsWith("_fib") && !f.Name.EndsWith("_new"));
            Assert.NotNull(fibFunc);

            var mangler = new NameMangler();
            var emitter = new CSharpEmitter(mangler, new System.Collections.Generic.HashSet<string>());
            var lowered = new FunctionLowerer(emitter).Lower(fibFunc);
            var mangledName = mangler.Mangle(fibFunc.Name);
            var source = $$"""
namespace BabyPenguinCompiled
{
    public static class Generated
    {
{{lowered}}
    }
}
""";

            // --- Build the lowered fib ---
            var assembly = new DiskCompiler().CompileAndLoad(new[] { ("Generated.cs", source) }, keepCs: false);
            var generated = assembly.GetType("BabyPenguinCompiled.Generated")!;
            var fibMethod = generated.GetMethod(mangledName, BindingFlags.Static | BindingFlags.Public)!;

            // Warm up + verify correctness on the compiled side.
            var compiledResult = (long)fibMethod.Invoke(null, new object[] { (long)N })!;
            Assert.Equal(Expected, compiledResult);

            // --- Time compiled (min over a few runs) ---
            double compiledMs = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                var r = (long)fibMethod.Invoke(null, new object[] { (long)N })!;
                sw.Stop();
                Assert.Equal(Expected, r);
                compiledMs = System.Math.Min(compiledMs, sw.Elapsed.TotalMilliseconds);
            }

            // --- Time interpreter (whole-program vm.Run, dominated by fib(N)) ---
            GlobalState.Global = null; // compiled side leaves this null; interpreter uses its own
            var vm = new BabyPenguinVM(model);
            var sw2 = Stopwatch.StartNew();
            vm.Run();
            sw2.Stop();
            var interpOutput = vm.CollectOutput().Trim();
            Assert.Equal(Expected.ToString(), interpOutput);
            double interpMs = sw2.Elapsed.TotalMilliseconds;

            var speedup = interpMs / compiledMs;
            helper.WriteLine($"fib({N}) = {Expected}");
            helper.WriteLine($"interpreter (vm.Run): {interpMs:F1} ms");
            helper.WriteLine($"compiled C#       : {compiledMs:F2} ms");
            helper.WriteLine($"speedup           : {speedup:F1}x");

            // De-risk gate: compiled must be meaningfully faster.
            Assert.True(speedup >= 5.0, $"expected >=5x speedup, got {speedup:F1}x (interp={interpMs:F1}ms compiled={compiledMs:F2}ms)");
        }
    }
}
