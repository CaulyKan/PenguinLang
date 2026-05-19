using System.Diagnostics;
using System.Threading;

namespace EmperorPenguin.Tests;

/// <summary>
/// Base class for end-to-end test classes. Provides RunEndToEnd helper
/// and a shared lock to ensure sequential execution.
/// </summary>
[Collection("EndToEnd")]
public abstract class EndToEndTestBase
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    protected string RunEndToEnd(string source)
    {
        _lock.Wait();
        try
        {
            return RunEndToEndCore(source);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string RunEndToEndCore(string source)
    {
        var projectRoot = BatchCompiler.FindProjectRoot();
        var testId = Guid.NewGuid().ToString("N")[..8];
        var srcFile = Path.Combine(projectRoot, $"tmp_e2e_{testId}.penguin");
        File.WriteAllText(srcFile, source);

        var empTmp = Path.Combine(projectRoot, "tmp");
        if (Directory.Exists(empTmp))
            Directory.Delete(empTmp, true);
        Directory.CreateDirectory(empTmp);

        try
        {
            var compilePsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{Path.Combine(projectRoot, "BabyPenguin", "BabyPenguin.csproj")}\" -q -- \"{Path.Combine(projectRoot, "EmperorPenguin", "EmperorPenguin.penguins")}\" -- \"{srcFile}\"",
                WorkingDirectory = projectRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var compileProc = Process.Start(compilePsi)!;
            string compileErrors = compileProc.StandardError.ReadToEnd();
            compileProc.WaitForExit(120000);
            Assert.True(compileProc.ExitCode == 0, $"Compilation failed (exit={compileProc.ExitCode}):\n{compileErrors}");

            var exePath = Path.Combine(projectRoot, "tmp", "out.exe");
            Assert.True(File.Exists(exePath), $"Expected executable at {exePath}");
            var runPsi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var runProc = Process.Start(runPsi)!;
            string stdout = runProc.StandardOutput.ReadToEnd();
            runProc.WaitForExit(10000);

            return stdout;
        }
        finally
        {
            try { File.Delete(srcFile); } catch { }
            try { Directory.Delete(Path.Combine(projectRoot, "tmp"), true); } catch { }
        }
    }
}
