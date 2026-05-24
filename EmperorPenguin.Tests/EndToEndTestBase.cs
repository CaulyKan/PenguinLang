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
        var tempDir = Path.Combine(Path.GetTempPath(), $"penguinlang_e2e_{testId}");
        Directory.CreateDirectory(tempDir);
        var srcFile = Path.Combine(tempDir, $"test_{testId}.penguin");
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
            var stdoutTask = compileProc.StandardOutput.ReadToEndAsync();
            var stderrTask = compileProc.StandardError.ReadToEndAsync();
            if (!compileProc.WaitForExit(300000))
            {
                compileProc.Kill();
                throw new Exception($"E2E compilation timed out after 300s");
            }
            string compileStdout = stdoutTask.Result;
            string compileStderr = stderrTask.Result;
            Assert.True(compileProc.ExitCode == 0, $"Compilation failed (exit={compileProc.ExitCode}):\n{compileStderr}\n{compileStdout}");

            var exePath = Path.Combine(empTmp, "out.exe");
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
            var runStdoutTask = runProc.StandardOutput.ReadToEndAsync();
            if (!runProc.WaitForExit(60000))
            {
                runProc.Kill();
                throw new Exception("E2E execution timed out after 60s");
            }
            string stdout = runStdoutTask.Result;

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
            try { Directory.Delete(empTmp, true); } catch { }
        }
    }
}
