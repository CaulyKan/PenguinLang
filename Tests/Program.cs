// PenguinTestRunner: a markdown-driven, cross-compiler test framework for PenguinLang.
//
// Each test is a *.md file under Tests/<Category>/<Name>.md describing a penguin
// program, the compilers it applies to (BabyPenguin, EmperorPenguin Pass1/2/3),
// and the expected compile/run exit codes and stdout. The runner compiles and
// (when relevant) runs each program against each applicable compiler, captures
// per-stage time and peak memory, collects all artifacts under one timestamped
// run folder, writes a summary (md + json), and diffs against the previous run.
//
// Bootstrap is MANUAL: this runner never bootstraps EmperorPenguin. If a Pass2/3
// binary is required but missing it errors out telling you to run ./emperor_penguin -b.
//
// Invoke:
//   dotnet run --project Tests/PenguinTestRunner -- [options] [filter]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PenguinTestRunner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts == null) return 1;
        if (opts.Help) { Options.PrintHelp(); return 0; }

        var repoRoot = LocateRepoRoot();
        var testsDir = Path.Combine(repoRoot, "Tests");

        if (opts.Migrate != null)
        {
            Console.WriteLine("--migrate is implemented in Phase B/C (not in this build).");
            return 1;
        }

        // Discover and parse test specs.
        var allFiles = DiscoverMdFiles(testsDir);
        if (opts.Filter != null)
            allFiles = allFiles.Where(f => MatchesFilter(f, testsDir, opts.Filter)).ToList();

        if (allFiles.Count == 0)
        {
            Console.WriteLine("No test files found under " + testsDir +
                (opts.Filter != null ? $" matching filter '{opts.Filter}'." : "."));
            return 0;
        }

        var tests = new List<MarkdownTestCase>();
        foreach (var f in allFiles)
        {
            try { tests.Add(MarkdownTestParser.Parse(f, testsDir)); }
            catch (Exception e)
            {
                Console.WriteLine($"[parse-error] {Path.GetRelativePath(testsDir, f)}: {e.Message}");
            }
        }

        // Decide the effective compiler set.
        var requested = opts.Compilers; // null = use each test's Apply To
        var probe = opts.Probe;

        // Build the set of work items (test x compiler).
        var work = new List<WorkItem>();
        foreach (var t in tests)
        {
            var targets = probe
                ? (requested ?? new HashSet<CompilerKind>(AllCompilers))
                : (requested == null
                    ? new HashSet<CompilerKind>(t.ApplyTo)
                    : new HashSet<CompilerKind>(t.ApplyTo.Intersect(requested)));
            foreach (var c in targets)
                work.Add(new WorkItem(t, c));
        }

        if (work.Count == 0)
        {
            Console.WriteLine("Nothing to run (no test x compiler combinations after Apply To filtering).");
            return 0;
        }

        // Bootstrap guard: ensure binaries exist for the compilers actually used.
        var usedCompilers = work.Select(w => w.Compiler).Distinct().ToList();
        var guardError = BootstrapGuard.Check(repoRoot, usedCompilers);
        if (guardError != null)
        {
            Console.Error.WriteLine(guardError);
            return 2;
        }

        // Ensure BabyPenguin is built (Pass1 runs on its VM; reuse the DLL).
        var bpDll = Path.Combine(repoRoot, "BabyPenguin", "bin", "Release", "net10.0", "BabyPenguin.dll");
        if (usedCompilers.Contains(CompilerKind.EmperorPenguinPass1) || usedCompilers.Contains(CompilerKind.BabyPenguin))
        {
            if (!File.Exists(bpDll))
            {
                Console.WriteLine("Building BabyPenguin (Release)...");
                var (code, _, _) = ProcessRunner.RunSync(
                    "dotnet", $"build \"{Path.Combine(repoRoot, "BabyPenguin", "BabyPenguin.csproj")}\" -c Release",
                    repoRoot, TimeSpan.FromMinutes(5));
                if (code != 0 || !File.Exists(bpDll))
                {
                    Console.Error.WriteLine($"Failed to build BabyPenguin (exit {code}). DLL not found at {bpDll}");
                    return 3;
                }
            }
        }

        // Prepare run folder.
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var runDir = Path.Combine(repoRoot, "tmp", "testruns", stamp);
        Directory.CreateDirectory(runDir);

        // Load baseline.
        var baselinePath = opts.Baseline == "none"
            ? null
            : (opts.Baseline == "latest" || opts.Baseline == null
                ? Path.Combine(repoRoot, "tmp", "testruns", "latest.json")
                : opts.Baseline);
        Dictionary<string, ComboResult>? baseline = null;
        if (baselinePath != null && File.Exists(baselinePath))
        {
            baseline = BaselineComparer.Load(baselinePath);
            Console.WriteLine($"Loaded baseline: {baselinePath} ({baseline.Count} entries)");
        }

        var backends = BuildBackends(repoRoot, bpDll);

        Console.WriteLine($"Running {work.Count} (test x compiler) combination(s) across {usedCompilers.Count} compiler(s)...");
        var sw = Stopwatch.StartNew();

        var results = new ConcurrentBag<ComboResult>();
        var parallel = Math.Max(1, opts.Parallel);
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (item, ct) =>
            {
                var r = await TestRunner.RunAsync(item.Test, item.Compiler, backends[item.Compiler],
                                                  repoRoot, runDir, opts, ct);
                results.Add(r);
                lock (Console.Out)
                {
                    Console.WriteLine($"  [{r.Status,-5}] {r.Compiler,-20} {r.Category}/{r.Name}" +
                                      (r.Message.Length > 0 ? "  — " + Truncate(r.Message, 100) : ""));
                }
            });

        sw.Stop();

        var list = results.OrderBy(r => r.Category).ThenBy(r => r.Name).ThenBy(r => r.Compiler).ToList();
        var summaryDir = runDir;
        var summaryPath = Path.Combine(summaryDir, "summary.md");
        var jsonPath = Path.Combine(summaryDir, "summary.json");

        BaselineDiff? diff = null;
        if (baseline != null)
            diff = BaselineComparer.Compare(baseline, list, opts.TimeRegressionPct, opts.MemRegressionPct);

        SummaryReporter.WriteMarkdown(summaryPath, list, sw.Elapsed, diff, repoRoot);
        SummaryReporter.WriteJson(jsonPath, list, sw.Elapsed, diff);
        File.Copy(jsonPath, Path.Combine(repoRoot, "tmp", "testruns", "latest.json"), overwrite: true);

        int failCount = list.Count(r => r.Status != Status.Pass && r.Status != Status.Skip);
        var totals = SummaryReporter.Totals(list);
        Console.WriteLine();
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s — " +
                          $"PASS {totals.Pass}, FAIL {totals.Fail}, ERROR {totals.Error}, SKIP {totals.Skip} " +
                          $"(of {list.Count} combos).");
        if (diff != null && (diff.NewFailures.Count > 0 || diff.NewPasses.Count > 0 ||
                             diff.TimeRegressions.Count > 0 || diff.MemoryRegressions.Count > 0))
        {
            Console.WriteLine($"vs baseline: +{diff.NewFailures.Count} new fail, +{diff.NewPasses.Count} new pass, " +
                              $"{diff.TimeRegressions.Count} time regressions, {diff.MemoryRegressions.Count} memory regressions.");
        }
        Console.WriteLine($"Summary: {Path.GetRelativePath(repoRoot, summaryPath)}");
        Console.WriteLine($"Artifacts: {Path.GetRelativePath(repoRoot, runDir)}/");

        return failCount == 0 ? 0 : 1;
    }

    public static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "EmperorPenguin")) &&
                Directory.Exists(Path.Combine(dir.FullName, "BabyPenguin")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (containing EmperorPenguin/ and BabyPenguin/).");
    }

    private static List<string> DiscoverMdFiles(string testsDir)
    {
        if (!Directory.Exists(testsDir)) return new();
        return Directory.GetFiles(testsDir, "*.md", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).StartsWith("README", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p).ToList();
    }

    private static bool MatchesFilter(string file, string testsDir, string filter)
    {
        var rel = Path.GetRelativePath(testsDir, file).Replace('\\', '/');
        if (filter.Contains('*') || filter.Contains('?'))
            return GlobMatch(rel, filter);
        return rel.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool GlobMatch(string path, string glob)
    {
        // Convert a simple glob (with * and ?) into a regex.
        var sb = new StringBuilder();
        sb.Append('^');
        foreach (var ch in glob)
        {
            if (ch == '*') sb.Append(".*");
            else if (ch == '?') sb.Append('.');
            else sb.Append(Regex.Escape(ch.ToString()));
        }
        sb.Append('$');
        return Regex.IsMatch(path, sb.ToString(), RegexOptions.IgnoreCase);
    }

    private static Dictionary<CompilerKind, ICompilerBackend> BuildBackends(string repoRoot, string bpDll)
    {
        return new()
        {
            [CompilerKind.BabyPenguin] = new BabyPenguinBackend(bpDll),
            [CompilerKind.EmperorPenguinPass1] = new EmperorOnVmBackend(bpDll, Path.Combine(repoRoot, "EmperorPenguin", "EmperorPenguin.penguins")),
            [CompilerKind.EmperorPenguinPass2] = new EmperorNativeBackend(Path.Combine(repoRoot, "tmp", "pass2"), CompilerKind.EmperorPenguinPass2),
            [CompilerKind.EmperorPenguinPass3] = new EmperorNativeBackend(Path.Combine(repoRoot, "tmp", "pass3"), CompilerKind.EmperorPenguinPass3),
        };
    }

    public static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    public static readonly CompilerKind[] AllCompilers =
        { CompilerKind.BabyPenguin, CompilerKind.EmperorPenguinPass1, CompilerKind.EmperorPenguinPass2, CompilerKind.EmperorPenguinPass3 };
}

// ───────────────────────── Options ─────────────────────────

public sealed class Options
{
    public bool Help;
    public HashSet<CompilerKind>? Compilers;
    public string? Filter;
    public bool Probe;
    public int Parallel = Math.Max(1, Environment.ProcessorCount - 1);
    public int TimeoutCompileSec = 600;
    public int TimeoutRunSec = 60;
    public string? Baseline; // null/"latest" => latest.json, "none" => disabled, else path
    public int TimeRegressionPct = 50;
    public int MemRegressionPct = 50;
    public string? Migrate;
    public bool MergeRegions;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Val()
            {
                if (i + 1 >= args.Length) { Console.Error.WriteLine($"Missing value for {a}"); return null; }
                return args[++i];
            }
            switch (a)
            {
                case "--help": case "-h": o.Help = true; break;
                case "--compilers":
                    {
                        var v = Val(); if (v == null) return null;
                        o.Compilers = ParseCompilers(v);
                        if (o.Compilers == null) return null;
                        break;
                    }
                case "--filter": o.Filter = Val(); if (o.Filter == null) return null; break;
                case "--probe": o.Probe = true; break;
                case "--parallel": { var v = Val(); if (v == null || !int.TryParse(v, out o.Parallel)) { Console.Error.WriteLine("bad --parallel"); return null; } break; }
                case "--timeout-compile": { var v = Val(); if (v == null || !int.TryParse(v, out o.TimeoutCompileSec)) { Console.Error.WriteLine("bad --timeout-compile"); return null; } break; }
                case "--timeout-run": { var v = Val(); if (v == null || !int.TryParse(v, out o.TimeoutRunSec)) { Console.Error.WriteLine("bad --timeout-run"); return null; } break; }
                case "--baseline": o.Baseline = Val(); if (o.Baseline == null) return null; break;
                case "--time-regression-pct": { var v = Val(); if (v == null || !int.TryParse(v, out o.TimeRegressionPct)) { Console.Error.WriteLine("bad --time-regression-pct"); return null; } break; }
                case "--mem-regression-pct": { var v = Val(); if (v == null || !int.TryParse(v, out o.MemRegressionPct)) { Console.Error.WriteLine("bad --mem-regression-pct"); return null; } break; }
                case "--migrate": o.Migrate = Val(); if (o.Migrate == null) return null; break;
                case "--merge-regions": o.MergeRegions = true; break;
                default:
                    if (a.StartsWith("-")) { o.Filter ??= a; }
                    else { o.Filter ??= a; }
                    break;
            }
        }
        return o;
    }

    private static HashSet<CompilerKind>? ParseCompilers(string v)
    {
        var set = new HashSet<CompilerKind>();
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                set.UnionWith(Program.AllCompilers); continue;
            }
            if (part.Contains("baby", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.BabyPenguin);
            else if (part.Contains("pass1") || part.Equals("pass-1", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.EmperorPenguinPass1);
            else if (part.Contains("pass2") || part.Equals("pass-2", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.EmperorPenguinPass2);
            else if (part.Contains("pass3") || part.Equals("pass-3", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.EmperorPenguinPass3);
            else { Console.Error.WriteLine($"Unknown compiler '{part}'"); return null; }
        }
        return set;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        PenguinTestRunner — markdown-driven cross-compiler test framework.

        Usage: dotnet run --project Tests/PenguinTestRunner -- [options] [filter]

        Options:
          --compilers babypenguin,pass1,pass2,pass3[,all]
                                  Limit to these compilers (default: each test's Apply To).
          --filter <glob|substr>  Select Tests/ files, e.g. CalculationTest/* or AddTest.
          --probe                 Ignore Apply To; run the selected compilers & report matches.
          --parallel <n>          Max concurrent combos (default: cores-1).
          --timeout-compile <s>   Per-case compile timeout (default 600).
          --timeout-run <s>       Per-case run timeout (default 60).
          --baseline latest|none|<path>
                                  Baseline for diff (default: tmp/testruns/latest.json).
          --time-regression-pct <pct>   Flag duration regressions > pct (default 50).
          --mem-regression-pct <pct>    Flag memory regressions > pct (default 50).
          --migrate ep-e2e|bp-behaviorial|all [--merge-regions]
                                  (Phase B/C) Migrate legacy C# tests into Tests/*.md.
          --help                  Show this help.
        """);
    }
}

// ───────────────────────── Model ─────────────────────────

public enum CompilerKind { BabyPenguin, EmperorPenguinPass1, EmperorPenguinPass2, EmperorPenguinPass3 }

public enum Status { Pass, Fail, Skip, Error }

public static class CompilerKindExtensions
{
    public static string Key(this CompilerKind c) => c switch
    {
        CompilerKind.BabyPenguin => "babypenguin",
        CompilerKind.EmperorPenguinPass1 => "pass1",
        CompilerKind.EmperorPenguinPass2 => "pass2",
        CompilerKind.EmperorPenguinPass3 => "pass3",
        _ => throw new InvalidOperationException(),
    };
    public static string Display(this CompilerKind c) => c switch
    {
        CompilerKind.BabyPenguin => "BabyPenguin",
        CompilerKind.EmperorPenguinPass1 => "EmperorPenguin Pass1",
        CompilerKind.EmperorPenguinPass2 => "EmperorPenguin Pass2",
        CompilerKind.EmperorPenguinPass3 => "EmperorPenguin Pass3",
        _ => throw new InvalidOperationException(),
    };
}

/// <summary>A single stream expectation: DISCARD, or EQUALS &lt;literal&gt;.</summary>
public sealed record Expectation(string Mode, string? Operand)
{
    public static readonly Expectation Discard = new("DISCARD", null);
    public bool IsDiscard => Mode == "DISCARD";

    public static Expectation Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Equals("DISCARD", StringComparison.OrdinalIgnoreCase))
            return Discard;
        // "EQUALS `literal`"  or  "EQUALS literal"
        var m = Regex.Match(text, @"^(\w+)\s+(.*)$");
        if (!m.Success) return Discard;
        var mode = m.Groups[1].Value.ToUpperInvariant();
        var operand = m.Groups[2].Value.Trim();
        // Strip surrounding backticks.
        if (operand.StartsWith('`') && operand.EndsWith('`') && operand.Length >= 2)
            operand = operand[1..^1];
        return new Expectation(mode, operand);
    }

    public bool Evaluate(string actual, out string reason)
    {
        if (IsDiscard) { reason = ""; return true; }
        if (Mode == "EQUALS")
        {
            if (actual == (Operand ?? "")) { reason = ""; return true; }
            reason = DiffReason(Operand ?? "", actual);
            return false;
        }
        reason = $"unknown match mode '{Mode}'";
        return false;
    }

    private static string DiffReason(string expected, string actual)
    {
        if (expected == actual) return "";
        // Show a compact diff.
        var exp = Render(expected);
        var act = Render(actual);
        return $"expected {exp} but got {act}";
    }

    public static string Render(string s)
    {
        if (s == "") return "(empty)";
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else sb.Append(ch);
        }
        sb.Append('"');
        return sb.ToString();
    }
}

/// <summary>Per-stage settings (Compile or Run).</summary>
public sealed class StageSpec
{
    public string Args = "";
    public Dictionary<string, string> Env = new();
    /// <summary>"0", "NONZERO", "ANY", or an integer. null = default (0 for compile, 0 for run).</summary>
    public string ExpectedExitCode = "0";
    public Expectation ExpectedStdout = Expectation.Discard;
    public Expectation ExpectedStderr = Expectation.Discard;
    public string? Stdin; // Run only
}

public sealed class MarkdownTestCase
{
    public string Title = "";
    public string Description = "";
    public List<CompilerKind> ApplyTo = new();
    public string Code = "";
    public StageSpec Compile = new();
    public StageSpec? Run;
    public string SourcePath = "";
    public string Category = "";
    public string Name => string.IsNullOrEmpty(Title) ? Path.GetFileNameWithoutExtension(SourcePath) : Title;
}

public sealed record WorkItem(MarkdownTestCase Test, CompilerKind Compiler);

// ───────────────────────── Markdown parser ─────────────────────────

public static class MarkdownTestParser
{
    public static MarkdownTestCase Parse(string path, string testsDir)
    {
        var raw = File.ReadAllText(path);
        var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var tc = new MarkdownTestCase { SourcePath = path, Category = CategoryFromPath(path, testsDir) };

        string? section = null;
        var description = new StringBuilder();
        var applyTo = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("# "))
            {
                tc.Title = trimmed[2..].Trim();
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                section = trimmed[3..].Trim().ToLowerInvariant();
                if (section == "run" && tc.Run == null) tc.Run = new StageSpec();
                continue;
            }

            switch (section)
            {
                case "description":
                    description.AppendLine(line);
                    break;
                case "apply to":
                    var b = trimmed.TrimStart('*', '-', ' ').Trim();
                    if (b.Length > 0) applyTo.Add(b);
                    break;
                case "test code":
                    // A fenced block: detect opening fence line.
                    if (IsFence(trimmed))
                    {
                        var fence = new StringBuilder();
                        i++;
                        while (i < lines.Length && !IsFence(lines[i].Trim()))
                        {
                            fence.AppendLine(lines[i]);
                            i++;
                        }
                        tc.Code = DeIndent(fence.ToString());
                    }
                    break;
                case "compile":
                case "run":
                    {
                        var stage = section == "run" ? tc.Run! : tc.Compile;
                        var km = Regex.Match(trimmed, @"^([A-Za-z]+):\s*(.*)$");
                        if (km.Success)
                        {
                            var key = km.Groups[1].Value.ToLowerInvariant();
                            var val = km.Groups[2].Value;
                            if (key == "expectedstdout" || key == "expectedstderr")
                            {
                                var (exp, lastIdx) = ParseStreamExpectation(val, lines, i);
                                if (key == "expectedstdout") stage.ExpectedStdout = exp;
                                else stage.ExpectedStderr = exp;
                                i = lastIdx; // skip consumed continuation lines (for-loop will ++ past the closing)
                                break;
                            }
                        }
                        ParseStageLine(trimmed, stage, isRun: section == "run");
                        break;
                    }
            }
        }

        tc.Description = description.ToString().Trim();
        tc.ApplyTo = MapCompilers(applyTo);
        if (tc.ApplyTo.Count == 0)
            throw new FormatException("No compilers listed under '## Apply To'.");
        if (string.IsNullOrWhiteSpace(tc.Code))
            throw new FormatException("No '## Test Code' fenced block found.");

        return tc;
    }

    private static bool IsFence(string s) => s.StartsWith("```");

    private static void ParseStageLine(string line, StageSpec stage, bool isRun = false)
    {
        if (line.Length == 0) return;
        var m = Regex.Match(line, @"^([A-Za-z]+):\s*(.*)$");
        if (!m.Success) return;
        var key = m.Groups[1].Value.ToLowerInvariant();
        var val = m.Groups[2].Value;
        // Strip surrounding backticks on value-bearing fields.
        string Stripped() => val.StartsWith('`') && val.EndsWith('`') && val.Length >= 2 ? val[1..^1] : val;

        switch (key)
        {
            case "args": stage.Args = Stripped(); break;
            case "env": stage.Env = ParseEnv(Stripped()); break;
            case "stdin": if (isRun) stage.Stdin = Stripped(); break;
            case "expectedexitcode": stage.ExpectedExitCode = Stripped().Trim(); break;
                // expectedstdout / expectedstderr are handled in the main loop (they may span lines).
        }
    }

    /// <summary>
    /// Parse an ExpectedStdout/ExpectedStderr value. Supports DISCARD, and
    /// EQUALS with a backtick-delimited literal that may span multiple lines
    /// (terminated by a closing backtick). Returns the parsed expectation and
    /// the index of the last line consumed.
    /// </summary>
    private static (Expectation exp, int lastIndex) ParseStreamExpectation(string firstLineValue, string[] allLines, int curIndex)
    {
        var v = firstLineValue.Trim();
        if (v.Length == 0 || v.Equals("DISCARD", StringComparison.OrdinalIgnoreCase))
            return (Expectation.Discard, curIndex);
        var m = Regex.Match(v, @"^(\w+)\s*(.*)$");
        string mode = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "EQUALS";
        string operand = m.Success ? m.Groups[2].Value : v;

        if (operand.StartsWith('`'))
        {
            operand = operand[1..]; // drop opening backtick
            if (operand.EndsWith('`'))
                return (new Expectation(mode, operand[..^1]), curIndex); // single-line literal
            // Multi-line literal: consume continuation lines until a closing backtick.
            var sb = new StringBuilder(operand);
            int j = curIndex + 1;
            while (j < allLines.Length)
            {
                var nl = allLines[j];
                if (nl.EndsWith('`'))
                {
                    sb.Append('\n');
                    sb.Append(nl, 0, nl.Length - 1);
                    return (new Expectation(mode, sb.ToString()), j);
                }
                sb.Append('\n');
                sb.Append(nl);
                j++;
            }
            return (new Expectation(mode, sb.ToString()), allLines.Length - 1); // unterminated — consume rest
        }
        return (new Expectation(mode, operand), curIndex);
    }

    private static Dictionary<string, string> ParseEnv(string s)
    {
        var dict = new Dictionary<string, string>();
        foreach (var tok in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = tok.IndexOf('=');
            if (eq > 0) dict[tok[..eq]] = tok[(eq + 1)..];
        }
        return dict;
    }

    private static List<CompilerKind> MapCompilers(List<string> names)
    {
        var result = new List<CompilerKind>();
        foreach (var n in names)
        {
            var l = n.ToLowerInvariant();
            if (l.Contains("baby")) result.Add(CompilerKind.BabyPenguin);
            else if (l.Contains("pass1") || l == "pass 1" || l.Contains("pass 1")) result.Add(CompilerKind.EmperorPenguinPass1);
            else if (l.Contains("pass2") || l.Contains("pass 2")) result.Add(CompilerKind.EmperorPenguinPass2);
            else if (l.Contains("pass3") || l.Contains("pass 3")) result.Add(CompilerKind.EmperorPenguinPass3);
        }
        return result.Distinct().ToList();
    }

    private static string DeIndent(string code)
    {
        var lines = code.Replace("\r", "").Split('\n');
        int minIndent = int.MaxValue;
        foreach (var ln in lines)
        {
            if (string.IsNullOrWhiteSpace(ln)) continue;
            int indent = ln.TakeWhile(c => c == ' ').Count();
            minIndent = Math.Min(minIndent, indent);
        }
        if (minIndent == int.MaxValue || minIndent == 0) return code.TrimEnd('\n');
        var sb = new StringBuilder();
        foreach (var ln in lines)
        {
            if (ln.Length >= minIndent && !string.IsNullOrWhiteSpace(ln))
                sb.AppendLine(ln[minIndent..]);
            else
                sb.AppendLine(ln.TrimStart());
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string CategoryFromPath(string path, string testsDir)
    {
        var rel = Path.GetRelativePath(testsDir, path).Replace('\\', '/');
        var slash = rel.IndexOf('/');
        return slash < 0 ? "Tests" : rel[..slash];
    }
}

// ───────────────────────── Process runner ─────────────────────────

public sealed record ProcResult(int ExitCode, string Stdout, string Stderr, TimeSpan Duration, long PeakBytes, bool TimedOut);

public static class ProcessRunner
{
    public static async Task<ProcResult> RunAsync(ProcessStartInfo psi, string? stdin, int timeoutMs)
    {
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = stdin != null;

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start process: " + psi.FileName);

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (stdin != null)
        {
            await p.StandardInput.WriteAsync(stdin);
            await p.StandardInput.FlushAsync();
            p.StandardInput.Close();
        }

        var sw = Stopwatch.StartNew();
        bool timedOut;

        // On Linux, PeakWorkingSet64 reads 0 after the process exits (its /proc entry
        // vanishes), so sample VmHWM (peak resident set) while it is still alive.
        // Take one immediate sample so even very short processes get a reading.
        long peak = OperatingSystem.IsLinux() ? Math.Max(0, ReadVmHwm(p.Id)) : 0;
        Func<Task> pollMem = async () =>
        {
            if (!OperatingSystem.IsLinux()) return;
            while (true)
            {
                try { if (p.HasExited) break; } catch { break; }
                var hwm = ReadVmHwm(p.Id);
                if (hwm > peak) peak = hwm;
                try { await Task.Delay(40, CancellationToken.None); } catch { }
            }
        };
        var pollTask = Task.Run(pollMem);

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            timedOut = false;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            try { p.WaitForExit(5000); } catch { }
            timedOut = true;
        }
        await pollTask;
        sw.Stop();
        if (!OperatingSystem.IsLinux())
        {
            p.Refresh();
            try { peak = p.PeakWorkingSet64; } catch { }
        }
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        int exit = timedOut ? -1 : p.ExitCode;
        return new ProcResult(exit, stdout, stderr, sw.Elapsed, peak, timedOut);
    }

    /// <summary>Synchronous helper for one-off commands (e.g. the bootstrap build).</summary>
    public static (int exit, string stdout, string stderr) RunSync(string file, string args, string workDir, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds)) { try { p.Kill(true); } catch { } }
        return (p.ExitCode, so.Result, se.Result);
    }

    /// <summary>Read VmHWM (peak resident set, bytes) from /proc/&lt;pid&gt;/status. Returns -1 if unavailable.</summary>
    public static long ReadVmHwm(int pid)
    {
        try
        {
            foreach (var line in File.ReadAllLines($"/proc/{pid}/status"))
            {
                if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                        return kb * 1024;
                }
            }
        }
        catch { }
        return -1;
    }
}

// ───────────────────────── Compiler backends ─────────────────────────

public interface ICompilerBackend
{
    CompilerKind Kind { get; }
    /// <summary>True if the compile process also runs the program (BabyPenguin interpreter).</summary>
    bool IsInterpreted { get; }
    /// <summary>Build the process that compiles (and, if interpreted, runs) the source.</summary>
    ProcessStartInfo BuildCompileProcess(string repoRoot, string srcFile, string exeFile, StageSpec compile);
    /// <summary>For non-interpreted backends, build the run process for the produced exe. Null for interpreted.</summary>
    ProcessStartInfo? BuildRunProcess(string exeFile, StageSpec run);
}

/// <summary>C# reference compiler/VM. Interprets directly; -q emits program stdout once (see BabyPenguin/Program.cs).</summary>
public sealed class BabyPenguinBackend : ICompilerBackend
{
    private readonly string _bpDll;
    public BabyPenguinBackend(string bpDll) { _bpDll = bpDll; }
    public CompilerKind Kind => CompilerKind.BabyPenguin;
    public bool IsInterpreted => true;

    public ProcessStartInfo BuildCompileProcess(string repoRoot, string srcFile, string exeFile, StageSpec compile)
    {
        // BabyPenguin MUST run in -q for clean single output; ignore Compile.Args (BP verbosity differs and would re-introduce noise).
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = ArgumentBuilder.Build(_bpDll, "-q", srcFile),
            WorkingDirectory = repoRoot,
        };
        EnvHelper.ApplyEnv(psi, compile.Env);
        return psi;
    }

    public ProcessStartInfo? BuildRunProcess(string exeFile, StageSpec run) => null;
}

/// <summary>EmperorPenguin compiler source running on the BabyPenguin VM (slow path).</summary>
public sealed class EmperorOnVmBackend : ICompilerBackend
{
    private readonly string _bpDll;
    private readonly string _empPenguins;
    public EmperorOnVmBackend(string bpDll, string empPenguins) { _bpDll = bpDll; _empPenguins = empPenguins; }
    public CompilerKind Kind => CompilerKind.EmperorPenguinPass1;
    public bool IsInterpreted => false;

    public ProcessStartInfo BuildCompileProcess(string repoRoot, string srcFile, string exeFile, StageSpec compile)
    {
        // dotnet <bpDll> -q <empPenguins> -- <compileArgs> <src> -o <exe>
        var extra = ArgumentBuilder.SplitArgs(compile.Args);
        var all = new List<string> { _bpDll, "-q", _empPenguins, "--" };
        all.AddRange(extra);
        all.Add(srcFile);
        all.Add("-o");
        all.Add(exeFile);
        var psi = new ProcessStartInfo { FileName = "dotnet", Arguments = ArgumentBuilder.Build(all), WorkingDirectory = repoRoot };
        EnvHelper.ApplyEnv(psi, compile.Env);
        return psi;
    }

    public ProcessStartInfo BuildRunProcess(string exeFile, StageSpec run)
    {
        var psi = new ProcessStartInfo { FileName = exeFile, Arguments = ArgumentBuilder.SplitArgs(run.Args).Any() ? ArgumentBuilder.Build(ArgumentBuilder.SplitArgs(run.Args)) : "", WorkingDirectory = Directory.GetParent(exeFile)?.FullName ?? exeFile };
        EnvHelper.ApplyEnv(psi, run.Env);
        return psi;
    }
}

/// <summary>Native EmperorPenguin binary (Pass2 = tmp/pass2, Pass3 = tmp/pass3).</summary>
public sealed class EmperorNativeBackend : ICompilerBackend
{
    private readonly string _binary;
    private readonly CompilerKind _kind;
    public EmperorNativeBackend(string binary, CompilerKind kind) { _binary = binary; _kind = kind; }
    public CompilerKind Kind => _kind;
    public bool IsInterpreted => false;

    public ProcessStartInfo BuildCompileProcess(string repoRoot, string srcFile, string exeFile, StageSpec compile)
    {
        var all = new List<string>();
        all.AddRange(ArgumentBuilder.SplitArgs(compile.Args));
        all.Add(srcFile);
        all.Add("-o");
        all.Add(exeFile);
        var psi = new ProcessStartInfo { FileName = _binary, Arguments = ArgumentBuilder.Build(all), WorkingDirectory = repoRoot };
        EnvHelper.ApplyEnv(psi, compile.Env);
        return psi;
    }

    public ProcessStartInfo BuildRunProcess(string exeFile, StageSpec run)
    {
        var psi = new ProcessStartInfo { FileName = exeFile, Arguments = ArgumentBuilder.SplitArgs(run.Args).Any() ? ArgumentBuilder.Build(ArgumentBuilder.SplitArgs(run.Args)) : "", WorkingDirectory = Directory.GetParent(exeFile)?.FullName ?? exeFile };
        EnvHelper.ApplyEnv(psi, run.Env);
        return psi;
    }
}

public static class ArgumentBuilder
{
    /// <summary>Build a shell-argument string, quoting args that contain spaces or quotes. Minimal, POSIX-style quoting.</summary>
    public static string Build(params string[] args) => Build((IEnumerable<string>)args);
    public static string Build(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length == 0) { sb.Append("\"\""); continue; }
            if (NeedsQuoting(a))
            {
                sb.Append('"');
                sb.Append(a.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            else sb.Append(a);
        }
        return sb.ToString();
    }

    private static bool NeedsQuoting(string a) =>
        a.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '\\' || c == '$' || c == '`');

    public static List<string> SplitArgs(string s)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return result;
        foreach (var part in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            result.Add(part);
        return result;
    }
}

public static class EnvHelper
{
    public static void ApplyEnv(ProcessStartInfo psi, Dictionary<string, string> env)
    {
        foreach (var kv in env)
            psi.Environment[kv.Key] = kv.Value;
    }
}

// ───────────────────────── Test runner ─────────────────────────

public sealed class ComboResult
{
    public string Category = "";
    public string Name = "";
    public string Test => Category + "/" + Name;
    public CompilerKind Compiler;
    public Status Status;
    public string Message = "";

    public StageResult? Compile;
    public StageResult? Run;

    public string ExpectedStdout = "";
    public string ActualStdout = "";
}

public sealed class StageResult
{
    public int ExitCode;
    public double DurationMs;
    public long PeakBytes;
    public bool TimedOut;
    public string Stdout = "";
    public string Stderr = "";
    public string[] Failures = Array.Empty<string>(); // expectation failures for this stage
}

public static class TestRunner
{
    public static async Task<ComboResult> RunAsync(
        MarkdownTestCase test, CompilerKind compiler, ICompilerBackend backend,
        string repoRoot, string runDir, Options opts, CancellationToken ct)
    {
        var result = new ComboResult
        {
            Category = test.Category,
            Name = test.Name,
            Compiler = compiler,
        };

        var workDir = Path.Combine(runDir, compiler.Key(), SafePath(test.Category), SafePath(test.Name));
        Directory.CreateDirectory(workDir);

        var srcFile = Path.Combine(workDir, "source.penguin");
        await File.WriteAllTextAsync(srcFile, test.Code, ct);

        var exeFile = Path.Combine(workDir, "out.exe");

        // ── Compile stage ──
        var compilePsi = backend.BuildCompileProcess(repoRoot, srcFile, exeFile, test.Compile);
        compilePsi.Environment["TMPDIR"] = workDir;
        var cproc = await ProcessRunner.RunAsync(compilePsi, null, opts.TimeoutCompileSec * 1000);

        var compileStage = new StageResult
        {
            ExitCode = cproc.ExitCode,
            DurationMs = cproc.Duration.TotalMilliseconds,
            PeakBytes = cproc.PeakBytes,
            TimedOut = cproc.TimedOut,
            Stdout = cproc.Stdout,
            Stderr = cproc.Stderr,
        };
        await WriteLogAsync(Path.Combine(workDir, "compile.log"), compilePsi, cproc);
        result.Compile = compileStage;

        bool compileExitOk = CheckExit(test.Compile.ExpectedExitCode, cproc.ExitCode, out var exitReason);
        bool isNegative = IsNegativeExpectation(test.Compile.ExpectedExitCode);

        var failures = new List<string>();
        if (cproc.TimedOut) failures.Add($"compile timed out after {opts.TimeoutCompileSec}s");
        if (!compileExitOk) failures.Add($"compile exit {cproc.ExitCode} (expected {test.Compile.ExpectedExitCode})");
        if (!cproc.TimedOut && !isNegative)
        {
            if (!test.Compile.ExpectedStdout.IsDiscard)
            {
                if (!test.Compile.ExpectedStdout.Evaluate(cproc.Stdout, out var r) && r.Length > 0) failures.Add("compile stdout: " + r);
            }
            if (!test.Compile.ExpectedStderr.IsDiscard)
            {
                if (!test.Compile.ExpectedStderr.Evaluate(cproc.Stderr, out var r) && r.Length > 0) failures.Add("compile stderr: " + r);
            }
        }
        compileStage.Failures = failures.ToArray();

        // Negative test: compile was expected to fail; no run step.
        if (isNegative)
        {
            result.Status = failures.Count == 0 ? Status.Pass : Status.Fail;
            result.Message = result.Status == Status.Pass
                ? $"compile failed as expected (exit {cproc.ExitCode})"
                : string.Join("; ", failures);
            await WriteResultJsonAsync(workDir, result);
            return result;
        }

        if (failures.Count > 0)
        {
            result.Status = cproc.TimedOut ? Status.Error : Status.Fail;
            result.Message = string.Join("; ", failures);
            await WriteResultJsonAsync(workDir, result);
            return result;
        }

        // ── Run stage ──
        if (test.Run == null)
        {
            // Compile-only test (success expected, no run).
            result.Status = Status.Pass;
            result.Message = "compile ok (no run step)";
            await WriteResultJsonAsync(workDir, result);
            return result;
        }

        StageResult runStage;
        string runStdout;
        if (backend.IsInterpreted)
        {
            // BabyPenguin already ran the program during the compile process; the
            // process's peak RSS is already accounted for in the compile stage, so
            // leave the run stage's peak at 0 to avoid double-counting.
            runStage = new StageResult
            {
                ExitCode = cproc.ExitCode,
                DurationMs = 0,
                PeakBytes = 0,
                Stdout = cproc.Stdout,
                Stderr = cproc.Stderr,
            };
            runStdout = cproc.Stdout;
        }
        else
        {
            if (!File.Exists(exeFile))
            {
                result.Status = Status.Error;
                result.Message = $"compile exited 0 but produced no executable at {exeFile}";
                await WriteResultJsonAsync(workDir, result);
                return result;
            }
            var runPsi = backend.BuildRunProcess(exeFile, test.Run)!;
            runPsi.Environment["TMPDIR"] = workDir;
            var rproc = await ProcessRunner.RunAsync(runPsi, test.Run.Stdin, opts.TimeoutRunSec * 1000);
            runStage = new StageResult
            {
                ExitCode = rproc.ExitCode,
                DurationMs = rproc.Duration.TotalMilliseconds,
                PeakBytes = rproc.PeakBytes,
                TimedOut = rproc.TimedOut,
                Stdout = rproc.Stdout,
                Stderr = rproc.Stderr,
            };
            await WriteLogAsync(Path.Combine(workDir, "run.log"), runPsi, rproc);
            runStdout = rproc.Stdout;
        }
        result.Run = runStage;
        result.ActualStdout = runStdout;
        result.ExpectedStdout = test.Run.ExpectedStdout.Operand ?? "";

        var runFailures = new List<string>();
        if (runStage.TimedOut) runFailures.Add($"run timed out after {opts.TimeoutRunSec}s");
        if (!CheckExit(test.Run.ExpectedExitCode, runStage.ExitCode, out var runExitReason))
            runFailures.Add($"run exit {runStage.ExitCode} (expected {test.Run.ExpectedExitCode})");
        if (!runStage.TimedOut)
        {
            if (!test.Run.ExpectedStdout.IsDiscard &&
                !test.Run.ExpectedStdout.Evaluate(runStdout, out var r1) && r1.Length > 0)
                runFailures.Add("run stdout: " + r1);
            if (!test.Run.ExpectedStderr.IsDiscard &&
                !test.Run.ExpectedStderr.Evaluate(runStage.Stderr, out var r2) && r2.Length > 0)
                runFailures.Add("run stderr: " + r2);
        }
        runStage.Failures = runFailures.ToArray();

        if (runFailures.Count == 0) { result.Status = Status.Pass; result.Message = "ok"; }
        else { result.Status = runStage.TimedOut ? Status.Error : Status.Fail; result.Message = string.Join("; ", runFailures); }

        await WriteResultJsonAsync(workDir, result);
        return result;
    }

    private static bool CheckExit(string expected, int actual, out string reason)
    {
        var e = expected.Trim();
        if (e.Equals("ANY", StringComparison.OrdinalIgnoreCase)) { reason = ""; return true; }
        if (e.Equals("NONZERO", StringComparison.OrdinalIgnoreCase)) { reason = ""; return actual != 0; }
        if (int.TryParse(e, out var code)) { reason = ""; return code == actual; }
        reason = $"unparseable ExpectedExitCode '{expected}'";
        return false;
    }

    private static bool IsNegativeExpectation(string expected)
    {
        var e = expected.Trim();
        if (e.Equals("NONZERO", StringComparison.OrdinalIgnoreCase)) return true;
        if (int.TryParse(e, out var code)) return code != 0;
        return false;
    }

    private static async Task WriteLogAsync(string path, ProcessStartInfo psi, ProcResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ " + psi.FileName + " " + psi.Arguments);
        sb.AppendLine($"working dir: {psi.WorkingDirectory}");
        foreach (var kv in psi.Environment.Where(x => x.Key == "TMPDIR")) sb.AppendLine($"env {kv.Key}={kv.Value}");
        sb.AppendLine($"exit: {r.ExitCode}   duration: {r.Duration.TotalMilliseconds:F0}ms   peakRSS: {r.PeakBytes} bytes   timedOut: {r.TimedOut}");
        sb.AppendLine("---- stdout ----");
        sb.AppendLine(r.Stdout);
        sb.AppendLine("---- stderr ----");
        sb.AppendLine(r.Stderr);
        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private static async Task WriteResultJsonAsync(string workDir, ComboResult r)
    {
        var dto = new
        {
            test = r.Test,
            category = r.Category,
            name = r.Name,
            compiler = r.Compiler.Key(),
            status = r.Status.ToString().ToUpperInvariant(),
            message = r.Message,
            expectedStdout = r.ExpectedStdout,
            actualStdout = r.ActualStdout,
            compile = r.Compile != null ? StageDto(r.Compile) : null,
            run = r.Run != null ? StageDto(r.Run) : null,
        };
        await File.WriteAllTextAsync(Path.Combine(workDir, "result.json"),
            JsonSerializer.Serialize(dto, JsonOpts));
    }

    private static object StageDto(StageResult s) => new
    {
        exitCode = s.ExitCode,
        durationMs = Math.Round(s.DurationMs, 1),
        peakBytes = s.PeakBytes,
        timedOut = s.TimedOut,
        failures = s.Failures,
        stdout = TruncateForJson(s.Stdout),
        stderr = TruncateForJson(s.Stderr),
    };

    private static string TruncateForJson(string s) => s.Length > 20000 ? s[..20000] + $"\n…[truncated, {s.Length} bytes total]" : s;

    internal static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string SafePath(string s)
    {
        foreach (var c in Path.GetInvalidPathChars()) s = s.Replace(c, '_');
        return s.Replace('/', '_').Replace('\\', '_');
    }
}

// ───────────────────────── Bootstrap guard ─────────────────────────

public static class BootstrapGuard
{
    public static string? Check(string repoRoot, IEnumerable<CompilerKind> used)
    {
        var set = used.ToHashSet();
        if (set.Contains(CompilerKind.EmperorPenguinPass2) && !File.Exists(Path.Combine(repoRoot, "tmp", "pass2")))
            return $"'tmp/pass2' not found. EmperorPenguin Pass2 requires a bootstrapped native binary.\nRun './emperor_penguin -b' first.";
        if (set.Contains(CompilerKind.EmperorPenguinPass3) && !File.Exists(Path.Combine(repoRoot, "tmp", "pass3")))
            return $"'tmp/pass3' not found. EmperorPenguin Pass3 requires a bootstrapped native binary.\nRun './emperor_penguin -b' first.";
        return null;
    }
}

// ───────────────────────── Baseline diff ─────────────────────────

public static class BaselineComparer
{
    public static Dictionary<string, ComboResult> Load(string path)
    {
        var dto = JsonSerializer.Deserialize<SummaryJsonDto>(File.ReadAllText(path), TestRunner.JsonOpts);
        var dict = new Dictionary<string, ComboResult>();
        if (dto?.results == null) return dict;
        foreach (var r in dto.results)
        {
            var cr = new ComboResult
            {
                Category = r.category ?? "",
                Name = r.name ?? "",
                Compiler = ParseKey(r.compiler ?? ""),
                Status = Enum.TryParse<Status>(r.status, ignoreCase: true, out var st) ? st : Status.Error,
                Compile = r.compile != null ? ToStage(r.compile) : null,
                Run = r.run != null ? ToStage(r.run) : null,
            };
            dict[Key(cr)] = cr;
        }
        return dict;
    }

    private static StageResult ToStage(StageJsonDto s) => new()
    {
        ExitCode = s.exitCode,
        DurationMs = s.durationMs,
        PeakBytes = s.peakBytes,
    };

    private static CompilerKind ParseKey(string s) => s switch
    {
        "babypenguin" => CompilerKind.BabyPenguin,
        "pass1" => CompilerKind.EmperorPenguinPass1,
        "pass2" => CompilerKind.EmperorPenguinPass2,
        "pass3" => CompilerKind.EmperorPenguinPass3,
        _ => CompilerKind.BabyPenguin,
    };

    public static string Key(ComboResult r) => $"{r.Test}|{r.Compiler.Key()}";

    public static BaselineDiff Compare(Dictionary<string, ComboResult> baseline, List<ComboResult> current, int timePct, int memPct)
    {
        var diff = new BaselineDiff();
        var curKeys = current.Select(Key).ToHashSet();
        foreach (var c in current)
        {
            if (!baseline.TryGetValue(Key(c), out var b))
            {
                diff.New.Add(c);
                continue;
            }
            bool wasPass = b.Status == Status.Pass;
            bool nowPass = c.Status == Status.Pass;
            if (wasPass && !nowPass) diff.NewFailures.Add((c, b));
            else if (!wasPass && nowPass) diff.NewPasses.Add((c, b));

            // Time/memory regressions on passing runs (compare compile + run combined).
            if (nowPass && wasPass)
            {
                CheckRegression(c, b, timePct, memPct, diff);
            }
        }
        foreach (var b in baseline)
        {
            if (!curKeys.Contains(b.Key)) diff.Removed.Add(b.Value);
        }
        return diff;
    }

    private static void CheckRegression(ComboResult c, ComboResult b, int timePct, int memPct, BaselineDiff diff)
    {
        double bc = (b.Compile?.DurationMs ?? 0) + (b.Run?.DurationMs ?? 0);
        double cc = (c.Compile?.DurationMs ?? 0) + (c.Run?.DurationMs ?? 0);
        if (bc > 0 && cc > bc * (1 + timePct / 100.0) && (cc - bc) > 1000)
            diff.TimeRegressions.Add((c, b));

        long bm = (b.Compile?.PeakBytes ?? 0) + (b.Run?.PeakBytes ?? 0);
        long cm = (c.Compile?.PeakBytes ?? 0) + (c.Run?.PeakBytes ?? 0);
        if (bm > 0 && cm > bm * (1 + memPct / 100.0))
            diff.MemoryRegressions.Add((c, b));
    }
}

public sealed class BaselineDiff
{
    public List<ComboResult> New = new();
    public List<ComboResult> Removed = new();
    public List<(ComboResult Cur, ComboResult Old)> NewFailures = new();
    public List<(ComboResult Cur, ComboResult Old)> NewPasses = new();
    public List<(ComboResult Cur, ComboResult Old)> TimeRegressions = new();
    public List<(ComboResult Cur, ComboResult Old)> MemoryRegressions = new();
}

// ───────────────────────── Summary reporter ─────────────────────────

public static class SummaryReporter
{
    public static (int Pass, int Fail, int Error, int Skip) Totals(List<ComboResult> list)
    {
        int p = 0, f = 0, e = 0, s = 0;
        foreach (var r in list)
        {
            switch (r.Status)
            {
                case Status.Pass: p++; break;
                case Status.Fail: f++; break;
                case Status.Error: e++; break;
                case Status.Skip: s++; break;
            }
        }
        return (p, f, e, s);
    }

    public static void WriteMarkdown(string path, List<ComboResult> list, TimeSpan total, BaselineDiff? diff, string repoRoot)
    {
        var sb = new StringBuilder();
        var (pass, fail, err, skip) = Totals(list);
        sb.AppendLine("# PenguinTestRunner — Summary");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTime.Now:O}");
        sb.AppendLine($"- Total duration: {total.TotalSeconds:F1}s");
        sb.AppendLine($"- Combos: **{pass} pass**, **{fail} fail**, **{err} error**, **{skip} skip** (of {list.Count})");
        sb.AppendLine();

        if (diff != null)
        {
            sb.AppendLine("## vs Baseline");
            sb.AppendLine();
            if (diff.NewFailures.Count == 0 && diff.NewPasses.Count == 0 &&
                diff.TimeRegressions.Count == 0 && diff.MemoryRegressions.Count == 0 &&
                diff.New.Count == 0 && diff.Removed.Count == 0)
            {
                sb.AppendLine("No changes vs baseline.");
            }
            else
            {
                if (diff.NewFailures.Count > 0) { sb.AppendLine($"### 🆕 New failures ({diff.NewFailures.Count})"); foreach (var (c, _) in diff.NewFailures) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}` — {c.Message}"); sb.AppendLine(); }
                if (diff.NewPasses.Count > 0) { sb.AppendLine($"### ✅ New passes ({diff.NewPasses.Count})"); foreach (var (c, _) in diff.NewPasses) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}`"); sb.AppendLine(); }
                if (diff.TimeRegressions.Count > 0) { sb.AppendLine($"### ⏱ Time regressions"); foreach (var (c, b) in diff.TimeRegressions) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}` — {MsOf(b):F0}ms → {MsOf(c):F0}ms"); sb.AppendLine(); }
                if (diff.MemoryRegressions.Count > 0) { sb.AppendLine($"### 💾 Memory regressions"); foreach (var (c, b) in diff.MemoryRegressions) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}` — {MemOf(b)} → {MemOf(c)}"); sb.AppendLine(); }
                if (diff.New.Count > 0) { sb.AppendLine($"### + New tests ({diff.New.Count})"); foreach (var c in diff.New) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}`"); sb.AppendLine(); }
                if (diff.Removed.Count > 0) { sb.AppendLine($"### − Removed ({diff.Removed.Count})"); foreach (var c in diff.Removed) sb.AppendLine($"- {c.Compiler.Display()} `{c.Test}`"); sb.AppendLine(); }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Per-compiler totals");
        sb.AppendLine();
        sb.AppendLine("| Compiler | Pass | Fail | Error | Skip |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var g in list.GroupBy(r => r.Compiler).OrderBy(g => g.Key))
        {
            var t = Totals(g.ToList());
            sb.AppendLine($"| {g.Key.Display()} | {t.Pass} | {t.Fail} | {t.Error} | {t.Skip} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Details");
        sb.AppendLine();
        sb.AppendLine("| Test | Compiler | Status | Compile ms | Compile RSS | Run ms | Run RSS | Message |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|");
        foreach (var r in list)
        {
            sb.AppendLine($"| `{r.Test}` | {r.Compiler.Display()} | {r.Status} | " +
                $"{(r.Compile?.DurationMs ?? 0):F0} | {FmtBytes(r.Compile?.PeakBytes ?? 0)} | " +
                $"{(r.Run?.DurationMs ?? 0):F0} | {FmtBytes(r.Run?.PeakBytes ?? 0)} | " +
                $"{EscapeMd(r.Message)} |");
        }
        sb.AppendLine();

        // Failures detail (full expected/actual).
        var fails = list.Where(r => r.Status != Status.Pass && r.Status != Status.Skip).ToList();
        if (fails.Count > 0)
        {
            sb.AppendLine("## Failures detail");
            sb.AppendLine();
            foreach (var r in fails)
            {
                sb.AppendLine($"### {r.Status} — {r.Compiler.Display()} `{r.Test}`");
                sb.AppendLine();
                sb.AppendLine($"- message: {r.Message}");
                if (r.Run != null)
                {
                    sb.AppendLine($"- expected stdout: {Expectation.Render(r.ExpectedStdout)}");
                    sb.AppendLine($"- actual stdout:   {Expectation.Render(r.ActualStdout)}");
                }
                sb.AppendLine($"- artifacts: `{Path.GetRelativePath(repoRoot, Path.Combine(repoRoot, "tmp", "testruns", "_", r.Compiler.Key(), r.Category, r.Name)).Replace("_\\" + Path.DirectorySeparatorChar, "")}`");
                sb.AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static double MsOf(ComboResult r) => (r.Compile?.DurationMs ?? 0) + (r.Run?.DurationMs ?? 0);
    private static string MemOf(ComboResult r) => FmtBytes((r.Compile?.PeakBytes ?? 0) + (r.Run?.PeakBytes ?? 0));

    public static string FmtBytes(long bytes) => bytes <= 0 ? "-" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):F1}MB" : $"{bytes / (double)(1 << 10):F0}KB";

    private static string EscapeMd(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    public static void WriteJson(string path, List<ComboResult> list, TimeSpan total, BaselineDiff? diff)
    {
        var dto = new SummaryJsonDto
        {
            generatedAt = DateTime.Now.ToString("O"),
            totalDurationSec = total.TotalSeconds,
            totals = new()
            {
                pass = list.Count(r => r.Status == Status.Pass),
                fail = list.Count(r => r.Status == Status.Fail),
                error = list.Count(r => r.Status == Status.Error),
                skip = list.Count(r => r.Status == Status.Skip),
            },
            results = list.Select(r => new ComboJsonDto
            {
                category = r.Category,
                name = r.Name,
                compiler = r.Compiler.Key(),
                status = r.Status.ToString().ToUpperInvariant(),
                message = r.Message,
                expectedStdout = r.ExpectedStdout,
                actualStdout = r.ActualStdout,
                compile = r.Compile != null ? ToDto(r.Compile) : null,
                run = r.Run != null ? ToDto(r.Run) : null,
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, TestRunner.JsonOpts));
    }

    private static StageJsonDto ToDto(StageResult s) => new()
    {
        exitCode = s.ExitCode,
        durationMs = Math.Round(s.DurationMs, 1),
        peakBytes = s.PeakBytes,
        timedOut = s.TimedOut,
        failures = s.Failures.ToList(),
    };
}

// JSON DTOs (kept loose for resilience when loading older baseline files).
public sealed class SummaryJsonDto
{
    public string? generatedAt { get; set; }
    public double totalDurationSec { get; set; }
    public TotalsJsonDto? totals { get; set; }
    public List<ComboJsonDto>? results { get; set; }
}
public sealed class TotalsJsonDto { public int pass; public int fail; public int error; public int skip; }
public sealed class ComboJsonDto
{
    public string? category { get; set; }
    public string? name { get; set; }
    public string? compiler { get; set; }
    public string? status { get; set; }
    public string? message { get; set; }
    public string expectedStdout { get; set; } = "";
    public string actualStdout { get; set; } = "";
    public StageJsonDto? compile { get; set; }
    public StageJsonDto? run { get; set; }
}
public sealed class StageJsonDto
{
    public int exitCode;
    public double durationMs;
    public long peakBytes;
    public bool timedOut;
    public List<string>? failures;
}
