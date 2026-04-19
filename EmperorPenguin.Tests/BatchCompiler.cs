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

public class BatchResults
{
    private readonly Dictionary<string, (string Result, string Expected)> _data = new();

    internal void Add(string name, string result, string expected)
        => _data[name] = (result, expected);

    public void Assert([CallerMemberName] string name = "")
        => Xunit.Assert.Equal(_data[name].Expected, _data[name].Result);

    public string GetResult([CallerMemberName] string name = "")
        => _data[name].Result;
}

public static class BatchCompiler
{
    public static string AstDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "ast"));

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
            results[name] = results[name].Trim();

        return results;
    }
}
