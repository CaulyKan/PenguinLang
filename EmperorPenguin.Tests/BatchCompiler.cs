using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BabyPenguin;

namespace EmperorPenguin.Tests;

[AttributeUsage(AttributeTargets.Method)]
public class BatchTestAttribute(string code, string expected) : Attribute
{
    public string Code { get; } = code;
    public string Expected { get; } = expected;
}

[AttributeUsage(AttributeTargets.Method)]
public class BatchParseTestAttribute(string source, string parseMethod, string expected) : Attribute
{
    public string Source { get; } = source;
    public string ParseMethod { get; } = parseMethod;
    public string Expected { get; } = expected;
}

[AttributeUsage(AttributeTargets.Method)]
public class BatchTokenizeTestAttribute(string source) : Attribute
{
    public string Source { get; } = source;
}

[AttributeUsage(AttributeTargets.Method)]
public class BatchBoundTestAttribute(string code, string expected) : Attribute
{
    public string Code { get; } = code;
    public string Expected { get; } = expected;
}

[AttributeUsage(AttributeTargets.Method)]
public class BatchE2ETestAttribute(string code, string expected) : Attribute
{
    public string Code { get; } = code;
    public string Expected { get; } = expected;
}

public class BatchResults
{
    private readonly Dictionary<string, (string Result, string Expected)> _data = new();

    internal void Add(string name, string result, string expected)
        => _data[name] = (result, expected);

    public void Assert([CallerMemberName] string name = "")
        => Xunit.Assert.Equal(_data[name].Expected, _data[name].Result);

    public void AssertSemantic([CallerMemberName] string name = "")
        => IRSemanticEqual.AssertSemanticallyEqual(_data[name].Expected, _data[name].Result);

    public string GetResult([CallerMemberName] string name = "")
        => _data[name].Result;
}

public static class BatchCompiler
{
    public static string AstDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "ast"));

    public static string BoundDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "bound"));

    public static string IRDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "ir"));

    public static string LLVMDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "llvm"));

    public static BatchResults InitBatch<T>()
    {
        var testData = CollectMethods<T, BatchTestAttribute>(
            (m, a) => (m.Name, a.Code, a.Expected));
        var results = CompileAndRun(testData.Select(t => (t.Name, t.Code)).ToArray());
        return ToBatchResults(testData, results);
    }

    public static BatchResults InitParseBatch<T>()
    {
        var testData = CollectMethods<T, BatchParseTestAttribute>(
            (m, a) => (m.Name, GenerateParseUserCode(a.Source, a.ParseMethod), a.Expected));
        var results = CompileAndRun(testData.Select(t => (t.Name, t.Code)).ToArray());
        return ToBatchResults(testData, results);
    }

    public static BatchResults InitTokenizeBatch<T>()
    {
        var testData = CollectMethods<T, BatchTokenizeTestAttribute>(
            (m, a) => (m.Name, GenerateTokenizeCode(a.Source), ""));
        var results = CompileAndRun(testData.Select(t => (t.Name, t.Code)).ToArray());
        return ToBatchResults(testData, results);
    }

    public static BatchResults InitBoundBatch<T>(string? prefixSource = null)
    {
        var testData = CollectMethods<T, BatchBoundTestAttribute>(
            (m, a) => (m.Name, a.Code, a.Expected));
        var results = CompileAndRunBound(testData.Select(t => (t.Name, t.Code)).ToArray(), prefixSource: prefixSource);
        return ToBatchResults(testData, results);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class BatchIRTestAttribute(string code, string expected) : Attribute
    {
        public string Code { get; } = code;
        public string Expected { get; } = expected;
    }

    public static BatchResults InitIRBatch<T>()
    {
        var testData = typeof(T)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<BatchIRTestAttribute>()
                .Select(a => (m.Name, a.Code, a.Expected)))
            .ToList();
        var results = CompileAndRunIR(testData.Select(t => (t.Name, t.Code)).ToArray());
        return ToBatchResults(testData, results);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class BatchLLVMTestAttribute(string code, string expected) : Attribute
    {
        public string Code { get; } = code;
        public string Expected { get; } = expected;
    }

    public static BatchResults InitLLVMBatch<T>()
    {
        var testData = typeof(T)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<BatchLLVMTestAttribute>()
                .Select(a => (m.Name, a.Code, a.Expected)))
            .ToList();
        var results = CompileAndRunLLVM(testData.Select(t => (t.Name, t.Code)).ToArray());
        return ToBatchResults(testData, results);
    }

    public static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "EmperorPenguin")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Project root not found");
    }

    private static readonly Lock _e2eLock = new();

    public static BatchResults InitE2EBatch<T>(int chunkSize = 10, string? prefixSource = null)
    {
        var testData = CollectMethods<T, BatchE2ETestAttribute>(
            (m, a) => (m.Name, a.Code, a.Expected));
        lock (_e2eLock)
        {
            var allResults = new Dictionary<string, string>();
            for (int i = 0; i < testData.Count; i += chunkSize)
            {
                var chunk = testData.Skip(i).Take(chunkSize).Select(t => (t.Name, t.Code)).ToArray();
                var chunkResults = CompileAndRunE2E(chunk, prefixSource: prefixSource);
                foreach (var kv in chunkResults)
                    allResults[kv.Key] = kv.Value;
            }
            return ToBatchResults(testData, allResults);
        }
    }

    public static Dictionary<string, string> CompileAndRunE2E(
        (string Name, string Code)[] tests,
        int timeoutSeconds = 900,
        string? prefixSource = null)
    {
        var projectRoot = FindProjectRoot();
        var combinedCode = new StringBuilder();
        if (prefixSource != null)
            combinedCode.AppendLine(prefixSource);
        foreach (var test in tests)
        {
            var taggedCode = test.Code.Replace("println(", $"println(\"@@{test.Name}@@\" + ");
            combinedCode.AppendLine($"namespace __test_{test.Name} {{");
            combinedCode.AppendLine(taggedCode);
            combinedCode.AppendLine("}");
        }

        var testId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), $"penguinlang_e2e_{testId}");
        Directory.CreateDirectory(tempDir);
        var srcFile = Path.Combine(tempDir, $"batch_{testId}.penguin");
        File.WriteAllText(srcFile, combinedCode.ToString());

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
            if (!compileProc.WaitForExit(timeoutSeconds * 1000))
            {
                compileProc.Kill();
                throw new Exception($"E2E batch compilation timed out after {timeoutSeconds}s");
            }
            string compileStdout = stdoutTask.Result;
            string compileStderr = stderrTask.Result;
            if (compileProc.ExitCode != 0)
            {
                var logPath = Path.Combine(empTmp, "emperor.log");
                var logContent = File.Exists(logPath) ? File.ReadAllText(logPath) : "(no log)";
                var sourceSnippet = combinedCode.ToString().Length > 2000
                    ? combinedCode.ToString()[..2000] + "\n... (truncated)"
                    : combinedCode.ToString();
                throw new Exception(
                    $"E2E batch compilation failed (exit={compileProc.ExitCode}):\n" +
                    $"STDOUT: {compileStdout}\nSTDERR: {compileStderr}\n" +
                    $"LOG:\n{logContent}\nSOURCE:\n{sourceSnippet}");
            }

            var exePath = Path.Combine(empTmp, "out.exe");
            if (!File.Exists(exePath))
                throw new Exception($"Expected executable at {exePath}");

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

            return ParseTaggedOutput(stdout);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
            try { Directory.Delete(empTmp, true); } catch { }
        }
    }

    /// <summary>
    /// Parse tagged output where @@tag@@ marks the START of a section,
    /// and all subsequent lines (until the next @@tag@@ or end) belong to that section.
    /// </summary>
    private static Dictionary<string, string> ParseTaggedOutputMultiline(string output)
    {
        var results = new Dictionary<string, string>();
        string? currentTag = null;
        var currentLines = new List<string>();

        foreach (var line in output.Split('\n'))
        {
            var match = Regex.Match(line, @"^@@(.+?)@@(.*)$");
            if (match.Success)
            {
                // Save previous tag's content
                if (currentTag != null)
                    results[currentTag] = string.Join("\n", currentLines).Trim();

                currentTag = match.Groups[1].Value;
                currentLines = new List<string>();
                var rest = match.Groups[2].Value;
                if (!string.IsNullOrEmpty(rest))
                    currentLines.Add(rest);
            }
            else if (currentTag != null)
            {
                currentLines.Add(line);
            }
        }

        // Save last tag's content
        if (currentTag != null)
            results[currentTag] = string.Join("\n", currentLines).Trim();

        return results;
    }

    private static List<(string Name, string Code, string Expected)> CollectMethods<T, TAttr>(
        Func<MethodInfo, TAttr, (string Name, string Code, string Expected)> selector)
        where TAttr : Attribute
    {
        return typeof(T)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<TAttr>() != null)
            .Select(m => selector(m, m.GetCustomAttribute<TAttr>()!))
            .ToList();
    }

    private static BatchResults ToBatchResults(
        List<(string Name, string Code, string Expected)> testData,
        Dictionary<string, string> results)
    {
        var batch = new BatchResults();
        foreach (var (name, _, expected) in testData)
            batch.Add(name, results.GetValueOrDefault(name, ""), expected);
        return batch;
    }

    public static Dictionary<string, string> CompileAndRunIR(
        (string Name, string Code)[] tests,
        int timeoutSeconds = 300)
    {
        var combinedCode = new StringBuilder();
        foreach (var test in tests)
        {
            var taggedCode = test.Code.Replace("println(", $"println(\"@@{test.Name}@@\" + ");
            combinedCode.AppendLine($"namespace __test_{test.Name} {{");
            combinedCode.AppendLine(taggedCode);
            combinedCode.AppendLine("}");
        }

        var compiler = new SemanticCompiler(new ErrorReporter());
        foreach (var f in Directory.GetFiles(AstDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(BoundDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(IRDir, "*.penguin"))
            compiler.AddFile(f);
        compiler.AddSource(combinedCode.ToString());

        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("VM timed out in IR batch compilation");

        return ParseTaggedOutput(vm.CollectOutput());
    }

    public static Dictionary<string, string> CompileAndRunLLVM(
        (string Name, string Code)[] tests,
        int timeoutSeconds = 300)
    {
        var combinedCode = new StringBuilder();
        foreach (var test in tests)
        {
            var taggedCode = test.Code.Replace("println(", $"println(\"@@{test.Name}@@\" + ");
            combinedCode.AppendLine($"namespace __test_{test.Name} {{");
            combinedCode.AppendLine(taggedCode);
            combinedCode.AppendLine("}");
        }

        var compiler = new SemanticCompiler(new ErrorReporter());
        foreach (var f in Directory.GetFiles(AstDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(BoundDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(IRDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(LLVMDir, "*.penguin"))
            compiler.AddFile(f);
        compiler.AddSource(combinedCode.ToString());

        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("VM timed out in LLVM batch compilation");

        return ParseTaggedOutputMultiline(vm.CollectOutput());
    }

    public static Dictionary<string, string> CompileAndRunBound(
        (string Name, string Code)[] tests,
        int timeoutSeconds = 300,
        string? prefixSource = null)
    {
        var combinedCode = new StringBuilder();
        if (prefixSource != null)
            combinedCode.AppendLine(prefixSource);
        foreach (var test in tests)
        {
            var taggedCode = test.Code.Replace("println(", $"println(\"@@{test.Name}@@\" + ");
            combinedCode.AppendLine($"namespace __test_{test.Name} {{");
            combinedCode.AppendLine(taggedCode);
            combinedCode.AppendLine("}");
        }

        var compiler = new SemanticCompiler(new ErrorReporter());
        foreach (var f in Directory.GetFiles(BoundDir, "*.penguin"))
            compiler.AddFile(f);
        foreach (var f in Directory.GetFiles(AstDir, "*.penguin"))
            compiler.AddFile(f);
        compiler.AddSource(combinedCode.ToString());

        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("VM timed out in bound batch compilation");

        return ParseTaggedOutput(vm.CollectOutput());
    }

    private static string GenerateParseUserCode(string source, string parseMethod)
    {
        var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"")
                            .Replace("\n", "\\n").Replace("\r", "\\r")
                            .Replace("\t", "\\t");
        return @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let p = new parser.Parser(tokens);
    let result = p.{parseMethod}();
    println(result.build_text());
}}";
    }

    private static string GenerateTokenizeCode(string source)
    {
        var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"")
                            .Replace("\n", "\\n").Replace("\r", "\\r")
                            .Replace("\t", "\\t");
        return @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let i: mut i64 = 0;
    while (i < cast<i64>(tokens.size())) {{
        let t = tokens.at(cast<u64>(i)).some;
        println(cast<string>(t.token_type) + "": "" + t.text);
        i = i + 1;
    }}
}}";
    }

    public static Dictionary<string, string> CompileAndRun(
        (string Name, string Code)[] tests,
        int timeoutSeconds = 60)
    {
        var combinedCode = new StringBuilder();
        foreach (var test in tests)
        {
            var taggedCode = test.Code.Replace("println(", $"println(\"@@{test.Name}@@\" + ");
            combinedCode.AppendLine($"namespace __test_{test.Name} {{");
            combinedCode.AppendLine(taggedCode);
            combinedCode.AppendLine("}");
        }

        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddFile(Path.Combine(AstDir, "Token.penguin"));
        compiler.AddFile(Path.Combine(AstDir, "Lexer.penguin"));
        compiler.AddFile(Path.Combine(AstDir, "AST.penguin"));
        compiler.AddFile(Path.Combine(AstDir, "Parser.penguin"));
        compiler.AddSource(combinedCode.ToString());

        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("VM timed out in batch compilation");

        return ParseTaggedOutput(vm.CollectOutput());
    }

    private static Dictionary<string, string> ParseTaggedOutput(string output)
    {
        var results = new Dictionary<string, string>();
        foreach (var line in output.Split('\n'))
        {
            var match = Regex.Match(line, @"^@@(.+?)@@(.*)$");
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                if (results.TryGetValue(name, out var existing))
                    results[name] = existing + "\n" + value;
                else
                    results[name] = value;
            }
        }

        foreach (var name in results.Keys.ToList())
            results[name] = results[name].TrimEnd();

        return results;
    }
}
