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
// binary is required but missing it errors out telling you to run ./penguin -b.
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
        Environment.SetEnvironmentVariable("PENGUIN_ROOT", repoRoot);
        var testsDir = Path.Combine(repoRoot, "Tests");

        if (opts.Migrate != null)
        {
            //return await Migrator.RunAsync(opts.Migrate, repoRoot, testsDir, opts.MergeRegions);
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
            IEnumerable<ApplyTarget> targets;
            if (probe)
            {
                var set = requested ?? new HashSet<CompilerKind>(AllCompilers);
                targets = set.Select(c => new ApplyTarget(c, null));
            }
            else
            {
                targets = requested == null
                    ? t.ApplyTo
                    : t.ApplyTo.Where(a => requested.Contains(a.Compiler)).ToList();
            }
            foreach (var a in targets)
                work.Add(new WorkItem(t, a.Compiler, a.SkipIfPass));
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
        var byTest = work.GroupBy(w => w.Test).Select(g => g.ToList()).ToList();
        await Parallel.ForEachAsync(byTest, new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (group, ct) => { await RunTestGroup(group, backends, repoRoot, runDir, opts, results, work.Count, ct); });

        sw.Stop();

        var list = results.OrderBy(r => r.Category).ThenBy(r => r.Name).ThenBy(r => r.Compiler).ToList();
        var specs = new Dictionary<string, MarkdownTestCase>();
        foreach (var t in tests) specs[t.Category + "/" + t.Name] = t;
        var commit = GitShort(repoRoot);
        var summaryDir = runDir;
        var summaryPath = Path.Combine(summaryDir, "summary.html");
        var jsonPath = Path.Combine(summaryDir, "summary.json");

        BaselineDiff? diff = null;
        if (baseline != null)
            diff = BaselineComparer.Compare(baseline, list, opts.TimeRegressionPct, opts.MemRegressionPct);

        SummaryReporter.WriteHtml(summaryPath, list, sw.Elapsed, diff, repoRoot, runDir, specs, commit);
        SummaryReporter.WriteJson(jsonPath, list, sw.Elapsed, diff);
        // Only update the baseline (latest.json) on a full run (no filter),
        // so subsequent filtered runs always compare against a complete baseline.
        if (opts.Filter == null)
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

    private static async Task RunTestGroup(List<WorkItem> items,
        Dictionary<CompilerKind, ICompilerBackend> backends, string repoRoot, string runDir,
        Options opts, ConcurrentBag<ComboResult> results, int totalWork, CancellationToken ct)
    {
        // Per-test waves: run guard compilers first; for a compiler with a
        // "skip if <guard> PASS" condition, skip it when the guard passed (else run it).
        var decided = new Dictionary<CompilerKind, ComboResult>();
        var pending = items.ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(w => w.SkipIfPass == null || decided.ContainsKey(w.SkipIfPass!.Value)).ToList();
            if (ready.Count == 0) ready = pending.ToList();
            var toRun = new List<WorkItem>();
            foreach (var w in ready)
            {
                if (w.SkipIfPass is CompilerKind g && decided.TryGetValue(g, out var gr) && gr.Status == Status.Pass)
                {
                    var skip = new ComboResult
                    {
                        Category = w.Test.Category,
                        Name = w.Test.Name,
                        Compiler = w.Compiler,
                        Status = Status.Skip,
                        Message = $"skipped: {g.Display()} passed",
                    };
                    results.Add(skip);
                    lock (decided) decided[w.Compiler] = skip;
                    LogCombo(skip, results.Count, totalWork);
                }
                else toRun.Add(w);
            }
            await Task.WhenAll(toRun.Select(async w =>
            {
                var r = await TestRunner.RunAsync(w.Test, w.Compiler, backends[w.Compiler], repoRoot, runDir, opts, ct);
                results.Add(r);
                lock (decided) decided[w.Compiler] = r;
                LogCombo(r, results.Count, totalWork);
            }));
            foreach (var w in ready) pending.Remove(w);
        }
    }

    private static void LogCombo(ComboResult r, int done, int total)
    {
        lock (Console.Out)
        {
            Console.WriteLine($"  [{r.Status,-5}] {r.Compiler.Key(),-12} {r.Category}/{r.Name} ({done}/{total})" +
                              (r.Message.Length > 0 ? "  — " + Truncate(r.Message, 100) : ""));
        }
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
            [CompilerKind.BabyPenguinCs] = new BabyPenguinCsBackend(bpDll),
            [CompilerKind.EmperorPenguinPass1] = new EmperorOnVmBackend(bpDll, Path.Combine(repoRoot, "EmperorPenguin", "EmperorPenguin.penguins")),
            [CompilerKind.EmperorPenguinPass2] = new EmperorNativeBackend(Path.Combine(repoRoot, "tmp", "pass2"), CompilerKind.EmperorPenguinPass2),
            [CompilerKind.EmperorPenguinPass3] = new EmperorNativeBackend(Path.Combine(repoRoot, "tmp", "pass3"), CompilerKind.EmperorPenguinPass3),
        };
    }

    public static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    public static string GitShort(string repoRoot)
    {
        try
        {
            var (code, hash, _) = ProcessRunner.RunSync("git", "rev-parse --short HEAD", repoRoot, TimeSpan.FromSeconds(5));
            if (code != 0) return "no-git";
            hash = hash.Trim();
            var (dc, dout, _) = ProcessRunner.RunSync("git", "status --porcelain", repoRoot, TimeSpan.FromSeconds(5));
            bool dirty = dc == 0 && dout.Trim().Length > 0;
            return dirty ? hash + "*" : hash;
        }
        catch { return "no-git"; }
    }

    public static readonly CompilerKind[] AllCompilers =
        { CompilerKind.BabyPenguin, CompilerKind.BabyPenguinCs, CompilerKind.EmperorPenguinPass1, CompilerKind.EmperorPenguinPass2, CompilerKind.EmperorPenguinPass3 };
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
            if (part.Contains("cs", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.BabyPenguinCs);
            else if (part.Contains("baby", StringComparison.OrdinalIgnoreCase)) set.Add(CompilerKind.BabyPenguin);
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

public enum CompilerKind { BabyPenguin, BabyPenguinCs, EmperorPenguinPass1, EmperorPenguinPass2, EmperorPenguinPass3 }

public enum Status { Pass, Fail, Skip, Error }

public static class CompilerKindExtensions
{
    public static string Key(this CompilerKind c) => c switch
    {
        CompilerKind.BabyPenguin => "babypenguin",
        CompilerKind.BabyPenguinCs => "babypenguincs",
        CompilerKind.EmperorPenguinPass1 => "pass1",
        CompilerKind.EmperorPenguinPass2 => "pass2",
        CompilerKind.EmperorPenguinPass3 => "pass3",
        _ => throw new InvalidOperationException(),
    };
    public static string Display(this CompilerKind c) => c switch
    {
        CompilerKind.BabyPenguin => "BabyPenguin",
        CompilerKind.BabyPenguinCs => "BabyPenguin CS",
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
        if (Mode == "CONTAINS")
        {
            var op = Operand ?? "";
            if (op.Length > 0 && actual.Contains(op, StringComparison.Ordinal)) { reason = ""; return true; }
            reason = $"expected to contain '{op}' but got {Render(actual)}";
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

public sealed record ApplyTarget(CompilerKind Compiler, CompilerKind? SkipIfPass);

public sealed class MarkdownTestCase
{
    public string Title = "";
    public string Description = "";
    public List<ApplyTarget> ApplyTo = new();
    public string Code = "";
    public StageSpec Compile = new();
    public StageSpec? Run;
    public string SourcePath = "";
    public string Category = "";
    public string Name => string.IsNullOrEmpty(Title) ? Path.GetFileNameWithoutExtension(SourcePath) : Title;
}

public sealed record WorkItem(MarkdownTestCase Test, CompilerKind Compiler, CompilerKind? SkipIfPass);

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
        tc.ApplyTo = ParseApplyTo(applyTo);
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

    private static List<ApplyTarget> ParseApplyTo(List<string> bullets)
    {
        var result = new List<ApplyTarget>();
        foreach (var raw in bullets)
        {
            // Match the compiler on the name only (before any "(SKIP if ...)" condition),
            // so a guard name inside the condition can't reclassify the entry.
            var l = raw.Split('(')[0].ToLowerInvariant();
            CompilerKind? kind = null;
            if (l.Contains("babypenguin cs")) kind = CompilerKind.BabyPenguinCs;
            else if (l.Contains("babypenguin")) kind = CompilerKind.BabyPenguin;
            else if (l.Contains("pass1") || l.Contains("pass 1")) kind = CompilerKind.EmperorPenguinPass1;
            else if (l.Contains("pass2") || l.Contains("pass 2")) kind = CompilerKind.EmperorPenguinPass2;
            else if (l.Contains("pass3") || l.Contains("pass 3")) kind = CompilerKind.EmperorPenguinPass3;
            if (kind == null) continue;
            // Optional "(SKIP if '<compiler>' PASS)" — skip this compiler when the guard passes.
            CompilerKind? skipIf = null;
            var m = Regex.Match(raw, @"SKIP\s+if\s+'([^']+)'\s+PASS", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var g = m.Groups[1].Value.ToLowerInvariant();
                if (g.Contains("baby")) skipIf = CompilerKind.BabyPenguin;
                else if (g.Contains("pass1") || g.Contains("pass 1")) skipIf = CompilerKind.EmperorPenguinPass1;
                else if (g.Contains("pass2") || g.Contains("pass 2")) skipIf = CompilerKind.EmperorPenguinPass2;
                else if (g.Contains("pass3") || g.Contains("pass 3")) skipIf = CompilerKind.EmperorPenguinPass3;
            }
            result.Add(new ApplyTarget(kind.Value, skipIf));
        }
        return result.GroupBy(a => a.Compiler)
            .Select(g => new ApplyTarget(g.Key, g.FirstOrDefault(a => a.SkipIfPass != null)?.SkipIfPass))
            .ToList();
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

// BabyPenguin with the experimental C# lowering backend (--backend=cs). Same I/O model as
// BabyPenguin (in-process compile+run); used to find divergences vs the interpreter oracle.
public sealed class BabyPenguinCsBackend : ICompilerBackend
{
    private readonly string _bpDll;
    public BabyPenguinCsBackend(string bpDll) { _bpDll = bpDll; }
    public CompilerKind Kind => CompilerKind.BabyPenguinCs;
    public bool IsInterpreted => true;

    public ProcessStartInfo BuildCompileProcess(string repoRoot, string srcFile, string exeFile, StageSpec compile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            // --backend=cs (equals form: CommandLineParser otherwise treats 'cs' as a positional file).
            Arguments = ArgumentBuilder.Build(_bpDll, "-q", "--backend=cs", srcFile),
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
        var extra = ArgumentBuilder.SplitArgs(EnvHelper.Expand(compile.Args));
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
        var psi = new ProcessStartInfo { FileName = exeFile, Arguments = ArgumentBuilder.SplitArgs(EnvHelper.Expand(run.Args)).Any() ? ArgumentBuilder.Build(ArgumentBuilder.SplitArgs(EnvHelper.Expand(run.Args))) : "", WorkingDirectory = Directory.GetParent(exeFile)?.FullName ?? exeFile };
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
        all.AddRange(ArgumentBuilder.SplitArgs(EnvHelper.Expand(compile.Args)));
        all.Add(srcFile);
        all.Add("-o");
        all.Add(exeFile);
        var psi = new ProcessStartInfo { FileName = _binary, Arguments = ArgumentBuilder.Build(all), WorkingDirectory = repoRoot };
        EnvHelper.ApplyEnv(psi, compile.Env);
        return psi;
    }

    public ProcessStartInfo BuildRunProcess(string exeFile, StageSpec run)
    {
        var psi = new ProcessStartInfo { FileName = exeFile, Arguments = ArgumentBuilder.SplitArgs(EnvHelper.Expand(run.Args)).Any() ? ArgumentBuilder.Build(ArgumentBuilder.SplitArgs(EnvHelper.Expand(run.Args))) : "", WorkingDirectory = Directory.GetParent(exeFile)?.FullName ?? exeFile };
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
            psi.Environment[kv.Key] = Expand(kv.Value);
    }

    /// <summary>
    /// Expand ${VAR} tokens against the process environment. ${PENGUIN_ROOT}
    /// (the workspace top-level) is always available — it is set in Main before
    /// any test runs. Unset variables expand to the empty string.
    /// </summary>
    public static string Expand(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return Regex.Replace(s, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",
            m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
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
    public string WorkDir = "";

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
    public string Command = "";
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
        result.WorkDir = workDir;
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
            Command = "$ " + compilePsi.FileName + " " + compilePsi.Arguments,
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
        }
        // Always check stderr, even for negative tests (they assert via CONTAINS).
        if (!cproc.TimedOut && !test.Compile.ExpectedStderr.IsDiscard)
        {
            if (!test.Compile.ExpectedStderr.Evaluate(cproc.Stderr, out var r) && r.Length > 0) failures.Add("compile stderr: " + r);
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
                Command = compileStage.Command,
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
                Command = "$ " + runPsi.FileName + " " + runPsi.Arguments,
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
            return $"'tmp/pass2' not found. EmperorPenguin Pass2 requires a bootstrapped native binary.\nRun './penguin -b' first.";
        if (set.Contains(CompilerKind.EmperorPenguinPass3) && !File.Exists(Path.Combine(repoRoot, "tmp", "pass3")))
            return $"'tmp/pass3' not found. EmperorPenguin Pass3 requires a bootstrapped native binary.\nRun './penguin -b' first.";
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

    public static void WriteHtml(string path, List<ComboResult> list, TimeSpan total, BaselineDiff? diff, string repoRoot, string runDir, IReadOnlyDictionary<string, MarkdownTestCase> specs, string commit)
    {
        var (_p, _f, _e, _s) = Totals(list);
        var marks = BuildBaselineMarks(diff);
        var byTest = list.GroupBy(r => r.Test)
            .Select(g => new { Test = g.Key, Items = g.OrderBy(r => r.Compiler).ToList() })
            .OrderBy(g => g.Test).ToList();

        var sb = new StringBuilder();
        sb.Append(HtmlHead);

        sb.Append("<header class='top'><h1>PenguinLang Test Report</h1>");
        sb.Append($"<span class='meta'>commit <span class='mono'>{HE(commit)}</span>  ·  Generated {HE(DateTime.Now.ToString("O"))}  ·  {total.TotalSeconds:F1}s  ·  {byTest.Count} tests / {list.Count} combos</span>");
        sb.Append("</header>");

        sb.Append("<div class='stats'>");
        sb.Append(StatCard("pass", _p.ToString(), "Pass"));
        sb.Append(StatCard("fail", _f.ToString(), "Fail"));
        sb.Append(StatCard("error", _e.ToString(), "Error"));
        sb.Append(StatCard("skip", _s.ToString(), "Skip"));

        // Per-compiler pass rate (green at 100%, otherwise red).
        foreach (var g in list.GroupBy(r => r.Compiler).OrderBy(x => x.Key))
        {
            int cnt = g.Count();
            int skipped = g.Count(r => r.Status == Status.Skip);
            int runCnt = cnt - skipped;
            int passed = g.Count(r => r.Status == Status.Pass);
            string cls, value, label = HE(g.Key.Display());
            if (runCnt == 0) { cls = "skip"; value = "skip"; label += " (all skipped)"; }
            else
            {
                int pct = (int)Math.Round(100.0 * passed / runCnt);
                cls = pct >= 100 ? "pass" : "fail";
                value = $"{pct}%";
                var skip_pct = (int)Math.Round(100.0 * skipped / cnt);
                if (skipped > 0) value += $" ({skip_pct}% SKIP)";
            }
            sb.Append(StatCard(cls, value, label));
        }
        sb.Append("</div>");

        // Filter controls + tests table.
        sb.Append("<div class='card'><div class='controls'>");
        sb.Append("<input type='text' id='search' placeholder='Filter by test name…'>");
        sb.Append("<div class='badges' id='statusBtns'>");
        foreach (var st in new[] { "PASS", "FAIL", "ERROR", "SKIP" })
            sb.Append($"<button class='btn' data-s='{st}'>{st}</button>");
        sb.Append("</div><div class='badges' id='compilerBtns'>");
        foreach (var c in list.Select(r => r.Compiler).Distinct().OrderBy(x => x))
            sb.Append($"<button class='btn' data-name='{HE(c.Key())}'>{HE(c.Display())}</button>");
        sb.Append("</div></div>");

        sb.Append("<div class='tblwrap'><table class='tests' id='testtable'>");
        sb.Append("<thead><tr><th>Compiler</th><th>Status</th><th class='num'>Compile</th><th class='num'>Compile RSS</th><th class='num'>Run</th><th class='num'>Run RSS</th><th>Summary</th></tr></thead>");
        int idx = 0;
        foreach (var g in byTest)
        {
            string overall = RollupStatus(g.Items);
            var compilers = string.Join(" ", g.Items.Select(r => r.Compiler.Key()).Distinct());
            sb.Append($"<tbody class='tgroup' data-idx='{idx}' data-status='{overall}' data-compilers='{HE(compilers)}' data-search='{HE(g.Test)}'>");
            sb.Append($"<tr class='tnamerow'><td colspan='7'><span class='mono tname'>{HE(g.Test)}</span></td></tr>");
            foreach (var r in g.Items)
            {
                var status = r.Status.ToString().ToUpperInvariant();
                sb.Append($"<tr class='crow' data-compiler='{HE(r.Compiler.Key())}' data-status='{status}'>");
                sb.Append($"<td class='mono'>{HE(r.Compiler.Display())}</td>");
                sb.Append($"<td><span class='pill {status}'>{status}</span>{BaselineMarkBadgeInline(r, marks)}</td>");
                sb.Append($"<td class='num'>{FmtMs(r.Compile?.DurationMs)}</td>");
                sb.Append($"<td class='num'>{FmtBytes(r.Compile?.PeakBytes ?? 0)}</td>");
                sb.Append($"<td class='num'>{(r.Run != null ? FmtMs(r.Run.DurationMs) : "—")}</td>");
                sb.Append($"<td class='num'>{(r.Run != null ? FmtBytes(r.Run.PeakBytes) : "—")}</td>");
                sb.Append($"<td class='summary'>{HE(Program.Truncate(r.Message, 100))}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody>");
            idx++;
        }
        sb.Append("</table></div></div>");

        // vs Baseline summary (after the table).
        if (diff != null) sb.Append(RenderBaselineHtml(diff));

        // Hidden per-test detail payloads.
        sb.Append("<div id='casedetails' style='display:none'>");
        idx = 0;
        foreach (var g in byTest)
        {
            var spec = specs.TryGetValue(g.Test, out var sp) ? sp : null;
            sb.Append($"<div id='cd-{idx}' data-name='{HE(g.Test)}'>");
            sb.Append(RenderTestDetail(g.Items, runDir, marks, spec));
            sb.Append("</div>");
            idx++;
        }
        sb.Append("</div>");

        // Full-page detail overlay.
        sb.Append("<div id='page' class='page'>");
        sb.Append("<div class='page-bar'><div class='t mono' id='pageTitle'></div><button class='closebtn' id='pageClose' aria-label='Close'>Close ✕</button></div>");
        sb.Append("<div class='page-body'><div class='inner' id='pageBody'></div></div>");
        sb.Append("</div>");

        sb.Append("</div>"); // close container
        sb.Append(HtmlApp);
        sb.Append("</body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static string RollupStatus(IEnumerable<ComboResult> cs)
    {
        var arr = cs.ToList();
        if (arr.Count == 0) return "SKIP";
        if (arr.Any(r => r.Status == Status.Error)) return "ERROR";
        if (arr.Any(r => r.Status == Status.Fail)) return "FAIL";
        if (arr.All(r => r.Status == Status.Pass)) return "PASS";
        return "SKIP";
    }

    private static Dictionary<string, (string Kind, string Detail)> BuildBaselineMarks(BaselineDiff? diff)
    {
        var d = new Dictionary<string, (string, string)>();
        if (diff == null) return d;
        foreach (var p in diff.NewFailures) d[KeyOf(p.Cur)] = ("newfail", "Regressed vs baseline: was PASS, now FAIL — " + p.Cur.Message);
        foreach (var p in diff.NewPasses) d[KeyOf(p.Cur)] = ("newpass", "Improved vs baseline: was FAIL, now PASS");
        foreach (var p in diff.TimeRegressions) d[KeyOf(p.Cur)] = ("timereg", $"Slower vs baseline: {MsOf(p.Old):F0} ms → {MsOf(p.Cur):F0} ms");
        foreach (var p in diff.MemoryRegressions) d[KeyOf(p.Cur)] = ("memreg", $"More memory vs baseline: {MemOf(p.Old)} → {MemOf(p.Cur)}");
        foreach (var c in diff.New) d.TryAdd(KeyOf(c), ("new", "New vs baseline (not present last run)"));
        return d;
    }

    private static string KeyOf(ComboResult r) => r.Test + "|" + r.Compiler.Key();

    private static string BaselineMarkBadgeInline(ComboResult r, Dictionary<string, (string Kind, string Detail)> marks)
    {
        if (!marks.TryGetValue(KeyOf(r), out var m)) return "";
        var label = m.Kind switch
        {
            "newfail" => "regress",
            "newpass" => "fixed",
            "timereg" => "slower",
            "memreg" => "+mem",
            "new" => "new",
            _ => m.Kind,
        };
        return $"<span class='bm {m.Kind}' title='{HE(m.Detail)}'>{label}</span>";
    }

    private static string RenderTestDetail(List<ComboResult> items, string runDir, Dictionary<string, (string Kind, string Detail)> marks, MarkdownTestCase? spec)
    {
        var sb = new StringBuilder();
        var first = items.First();
        sb.Append("<h3 class='blk'>Source</h3>");
        sb.Append(Pre(ReadSource(first.WorkDir)));
        sb.Append(ExpectationsHtml(spec));
        foreach (var r in items)
        {
            var status = r.Status.ToString().ToUpperInvariant();
            sb.Append("<section class='csec'>");
            sb.Append($"<h3 class='csec-h'><span class='mono'>{HE(r.Compiler.Display())}</span><span class='pill {status}'>{status}</span>{BaselineMarkBadgeInline(r, marks)}</h3>");
            sb.Append(StageHtml("Compile", r.Compile));
            sb.Append(StageHtml("Run", r.Run));
            var rel = Rel(runDir, r.WorkDir);
            sb.Append("<div class='lbl'>Artifacts</div>");
            sb.Append($"<a class='fl' href='{HE(rel)}/compile.log' target='_blank'>compile.log</a>");
            if (r.Run != null) sb.Append($"<a class='fl' href='{HE(rel)}/run.log' target='_blank'>run.log</a>");
            sb.Append($"<a class='fl' href='{HE(rel)}/result.json' target='_blank'>result.json</a>");
            sb.Append($"<div class='mono' style='font-size:11px;color:var(--muted);margin-top:6px'>{HE(rel)}/</div>");
            sb.Append("</section>");
        }
        return sb.ToString();
    }

    private static string ExpectationsHtml(MarkdownTestCase? spec)
    {
        if (spec == null) return "";
        var sb = new StringBuilder();
        sb.Append("<h3 class='blk'>Expectations</h3>");
        sb.Append(StageExpectHtml("Compile", spec.Compile, false));
        if (spec.Run != null) sb.Append(StageExpectHtml("Run", spec.Run, true));
        return sb.ToString();
    }

    private static string StageExpectHtml(string title, StageSpec s, bool isRun)
    {
        var sb = new StringBuilder();
        sb.Append($"<div class='expstage'><div class='exph'>{HE(title)}</div>");
        sb.Append("<div class='kv'>");
        sb.Append($"<span class='k'>expected exit</span><span><code>{HE(s.ExpectedExitCode)}</code></span>");
        sb.Append($"<span class='k'>stdout</span><span>{HE(ExpMode(s.ExpectedStdout))}</span>");
        sb.Append($"<span class='k'>stderr</span><span>{HE(ExpMode(s.ExpectedStderr))}</span>");
        if (!string.IsNullOrEmpty(s.Args)) sb.Append($"<span class='k'>args</span><span><code>{HE(s.Args)}</code></span>");
        if (isRun && !string.IsNullOrEmpty(s.Stdin)) sb.Append($"<span class='k'>stdin</span><span><code>{HE(s.Stdin)}</code></span>");
        sb.Append("</div>");
        if (!s.ExpectedStdout.IsDiscard && !string.IsNullOrEmpty(s.ExpectedStdout.Operand))
            sb.Append("<div class='lbl'>expected stdout</div>").Append(Pre(s.ExpectedStdout.Operand));
        if (!s.ExpectedStderr.IsDiscard && !string.IsNullOrEmpty(s.ExpectedStderr.Operand))
            sb.Append("<div class='lbl'>expected stderr</div>").Append(Pre(s.ExpectedStderr.Operand));
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string ExpMode(Expectation e) => e.IsDiscard ? "DISCARD" : e.Mode;

    private static string StatCard(string cls, string n, string label) =>
        $"<div class='stat {cls}'><div class='n'>{n}</div><div class='l'>{label}</div></div>";

    private static string FmtMs(double? ms)
    {
        if (ms == null) return "—";
        if (ms < 1) return $"{ms} ms";
        if (ms < 1000) return $"{Math.Round(ms.Value)} ms";
        return $"{ms.Value / 1000:F2} s";
    }

    private static string HE(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string Pre(string? s) => $"<pre>{HE(string.IsNullOrEmpty(s) ? "" : s)}</pre>";

    private static string StageHtml(string title, StageResult? s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        sb.Append($"<h3 class='blk'>{HE(title)}</h3>");
        sb.Append("<div class='kv'>");
        sb.Append($"<span class='k'>exit</span><span>{s.ExitCode}{(s.TimedOut ? "  (timed out)" : "")}</span>");
        sb.Append($"<span class='k'>duration</span><span>{FmtMs(s.DurationMs)}</span>");
        sb.Append($"<span class='k'>peak RSS</span><span>{FmtBytes(s.PeakBytes)}</span>");
        sb.Append("</div>");
        if (!string.IsNullOrEmpty(s.Command)) sb.Append($"<pre>{HE(s.Command)}</pre>");
        if (s.Failures.Length > 0)
        {
            sb.Append("<ul class='dlist'>");
            foreach (var f in s.Failures) sb.Append($"<li class='mono'>{HE(f)}</li>");
            sb.Append("</ul>");
        }
        if (!string.IsNullOrEmpty(s.Stdout)) sb.Append("<div class='lbl'>stdout</div>").Append(Pre(s.Stdout));
        if (!string.IsNullOrEmpty(s.Stderr)) sb.Append("<div class='lbl'>stderr</div>").Append(Pre(s.Stderr));
        return sb.ToString();
    }

    private static string ReadSource(string workDir)
    {
        try { var p = Path.Combine(workDir, "source.penguin"); return File.Exists(p) ? File.ReadAllText(p) : ""; }
        catch { return ""; }
    }

    private static string Rel(string fromDir, string toPath)
    {
        try { return Path.GetRelativePath(fromDir, toPath).Replace('\\', '/'); }
        catch { return toPath.Replace('\\', '/'); }
    }

    private static string RenderBaselineHtml(BaselineDiff d)
    {
        if (d.NewFailures.Count == 0 && d.NewPasses.Count == 0 && d.TimeRegressions.Count == 0 &&
            d.MemoryRegressions.Count == 0 && d.New.Count == 0 && d.Removed.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append("<div class='card'><h2>vs Baseline</h2>");
        if (d.NewFailures.Count > 0)
            sb.Append(Blist("🆕 New failures", d.NewFailures.Select(p =>
                HE(p.Cur.Compiler.Display() + " " + p.Cur.Test) + (string.IsNullOrEmpty(p.Cur.Message) ? "" : " — " + HE(p.Cur.Message)))));
        if (d.NewPasses.Count > 0)
            sb.Append(Blist("✅ New passes", d.NewPasses.Select(p => HE(p.Cur.Compiler.Display() + " " + p.Cur.Test))));
        if (d.TimeRegressions.Count > 0)
            sb.Append(Blist("⏱ Time regressions", d.TimeRegressions.Select(p =>
                HE(p.Cur.Compiler.Display() + " " + p.Cur.Test) + $" <span class='mono'>{MsOf(p.Old):F0} ms → {MsOf(p.Cur):F0} ms</span>")));
        if (d.MemoryRegressions.Count > 0)
            sb.Append(Blist("💾 Memory regressions", d.MemoryRegressions.Select(p =>
                HE(p.Cur.Compiler.Display() + " " + p.Cur.Test) + $" <span class='mono'>{MemOf(p.Old)} → {MemOf(p.Cur)}</span>")));
        if (d.New.Count > 0)
            sb.Append(Blist("+ New", d.New.Select(c => HE(c.Compiler.Display() + " " + c.Test))));
        if (d.Removed.Count > 0)
            sb.Append(Blist("− Removed", d.Removed.Select(c => HE(c.Compiler.Display() + " " + c.Test))));
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Blist(string title, IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        sb.Append($"<h3 class='blk'>{HE(title)}</h3><ul class='dlist'>");
        foreach (var i in items) sb.Append($"<li>{i}</li>");
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static double MsOf(ComboResult r) => (r.Compile?.DurationMs ?? 0) + (r.Run?.DurationMs ?? 0);
    private static string MemOf(ComboResult r) => FmtBytes((r.Compile?.PeakBytes ?? 0) + (r.Run?.PeakBytes ?? 0));

    public static string FmtBytes(long bytes) => bytes <= 0 ? "—" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):F1}MB" : $"{bytes / (double)(1 << 10):F0}KB";

    private const string HtmlHead = @"<!doctype html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>PenguinLang Test Report</title>
<style>
:root{
  --bg:#f4f5f7;--card:#ffffff;--border:#e4e6ea;--text:#1f2328;--muted:#6a737d;
  --accent:#3b6ee8;--pass:#1a7f37;--fail:#cf222e;--error:#bc4c00;--skip:#6e7781;--code-bg:#f6f8fa;--hover:#f1f3f5;
}
@media (prefers-color-scheme: dark){
  :root{--bg:#0d1117;--card:#161b22;--border:#30363d;--text:#e6edf3;--muted:#8b949e;--code-bg:#0d1117;--hover:#1c2128;}
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;font-size:14px;line-height:1.5}
.container{max-width:1320px;margin:0 auto;padding:24px 20px 90px}
header.top{display:flex;align-items:baseline;gap:14px;flex-wrap:wrap;margin-bottom:4px}
h1{font-size:20px;margin:0;font-weight:650}
.meta{color:var(--muted);font-size:13px}
.stats{display:flex;gap:10px;flex-wrap:wrap;margin:18px 0}
.stat{background:var(--card);border:1px solid var(--border);border-radius:10px;padding:10px 16px;min-width:88px}
.stat .n{font-size:22px;font-weight:750;line-height:1.1}
.stat .l{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
.stat.pass .n{color:var(--pass)} .stat.fail .n{color:var(--fail)} .stat.error .n{color:var(--error)} .stat.skip .n{color:var(--skip)}
.compstats{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 16px}
.crate{display:inline-flex;align-items:center;gap:7px;padding:6px 12px;border-radius:8px;font-size:12px;font-weight:600;border:1px solid var(--border);background:var(--card)}
.crate .dot{width:8px;height:8px;border-radius:50%;display:inline-block}
.crate.green{color:var(--pass)} .crate.green .dot{background:var(--pass)}
.crate.red{color:var(--fail)} .crate.red .dot{background:var(--fail)}
.card{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:16px 18px;margin:16px 0}
.card h2{margin:0 0 12px;font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted)}
h3.blk{margin:18px 0 6px;font-size:12px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted)}
h3.blk:first-child{margin-top:0}
.controls{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-bottom:14px}
.controls input{flex:1;min-width:200px;padding:8px 12px;border:1px solid var(--border);border-radius:8px;background:var(--card);color:var(--text);font-size:13px}
.badges{display:flex;gap:6px;flex-wrap:wrap}
.btn{padding:5px 12px;border:1px solid var(--border);border-radius:999px;background:var(--card);color:var(--muted);cursor:pointer;font-size:12px;font-weight:600;transition:all .12s}
.btn:hover{border-color:var(--accent)}
.btn.active{background:var(--accent);color:#fff;border-color:var(--accent)}
.tblwrap{overflow:auto;border:1px solid var(--border);border-radius:12px}
.summary{max-width:300px;}
table.tests{width:100%;border-collapse:separate;border-spacing:0;font-size:13px}
table.tests thead th{position:sticky;top:0;background:var(--card);text-align:left;padding:9px 12px;border-bottom:1px solid var(--border);font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);white-space:nowrap;z-index:2}
tbody.tgroup{cursor:pointer}
tbody.tgroup>tr{transition:background .08s}
tbody.tgroup:hover>tr{background:var(--hover)}
tbody.tgroup:hover>tr.tnamerow>td{background:var(--hover)}
tr.tnamerow>td{padding:14px 14px 8px;font-weight:650;font-size:13.5px;background:var(--card);border-top:1px solid var(--border)}
tr.tnamerow:first-child>td{border-top:none}
.tname{margin:0}
.rollpill{margin-left:10px;vertical-align:middle}
tr.crow>td{padding:8px 12px;border-bottom:1px solid var(--border)}
.mono,pre,code{font-family:ui-monospace,'SFMono-Regular',Menlo,Consolas,monospace}
code{background:var(--code-bg);padding:1px 5px;border-radius:4px;font-size:12px}
.num{text-align:right;font-variant-numeric:tabular-nums;color:var(--muted);white-space:nowrap}
.pill{display:inline-block;padding:2px 9px;border-radius:999px;font-size:11px;font-weight:700;letter-spacing:.03em}
.pill.PASS{background:rgba(26,127,55,.13);color:var(--pass)}
.pill.FAIL{background:rgba(207,34,46,.13);color:var(--fail)}
.pill.ERROR{background:rgba(188,76,0,.15);color:var(--error)}
.pill.SKIP{background:rgba(110,120,129,.17);color:var(--skip)}
.bm{display:inline-block;padding:1px 7px;border-radius:6px;font-size:10px;font-weight:700;letter-spacing:.03em;cursor:help;margin-left:8px;vertical-align:middle}
.bm.newfail{background:rgba(207,34,46,.15);color:var(--fail)}
.bm.newpass{background:rgba(26,127,55,.15);color:var(--pass)}
.bm.timereg{background:rgba(188,76,0,.17);color:var(--error)}
.bm.memreg{background:rgba(188,76,0,.17);color:var(--error)}
.bm.new{background:rgba(59,110,232,.16);color:var(--accent)}
ul.dlist{margin:6px 0;padding-left:18px}
ul.dlist li{margin:3px 0}
.kv{display:grid;grid-template-columns:max-content 1fr;gap:5px 14px;margin:6px 0 4px}
.kv .k{color:var(--muted)}
.lbl{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em;margin:10px 0 2px}
.expstage{margin:8px 0 12px;padding:10px 14px;border:1px solid var(--border);border-radius:8px;background:var(--code-bg)}
.expstage .exph{font-size:12px;font-weight:600;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;margin-bottom:6px}
pre{background:var(--card);border:1px solid var(--border);border-radius:8px;padding:10px 12px;overflow:auto;font-size:12px;white-space:pre-wrap;word-break:break-word;margin:6px 0;max-height:360px}
a.fl{color:var(--accent);text-decoration:none;font-size:12px;margin-right:14px;white-space:nowrap}
a.fl:hover{text-decoration:underline}
.page{position:fixed;inset:0;background:var(--bg);z-index:100;display:none;flex-direction:column}
.page.open{display:flex}
.page-bar{flex:0 0 auto;display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 20px;background:var(--card);border-bottom:1px solid var(--border)}
.page-bar .t{font-weight:650;font-size:15px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.page-body{flex:1 1 auto;overflow:auto;padding:20px}
.page-body .inner{max-width:1000px;margin:0 auto}
.closebtn{border:1px solid var(--border);background:var(--card);color:var(--muted);font-size:13px;font-weight:600;padding:6px 14px;border-radius:8px;cursor:pointer}
.closebtn:hover{color:var(--text);border-color:var(--accent)}
section.csec{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:14px 18px;margin:14px 0}
section.csec>h3.csec-h{margin:0 0 10px;font-size:14px;display:flex;align-items:center;gap:10px}
</style>
</head>
<body>
<div class='container'>
";

    private const string HtmlApp = @"<script>
(function(){
  function openPage(idx){
    var src=document.getElementById('cd-'+idx);
    if(!src)return;
    document.getElementById('pageBody').innerHTML=src.innerHTML;
    document.getElementById('pageTitle').textContent=src.getAttribute('data-name')||'';
    var pg=document.getElementById('page');
    pg.classList.add('open');
    document.body.style.overflow='hidden';
    pg.scrollTop=0;
  }
  function closePage(){var pg=document.getElementById('page');pg.classList.remove('open');document.getElementById('pageBody').innerHTML='';document.body.style.overflow='';}
  var tbl=document.getElementById('testtable');
  tbl.addEventListener('click',function(e){var g=e.target.closest('tbody.tgroup');if(!g)return;openPage(g.getAttribute('data-idx'));});
  document.getElementById('pageClose').addEventListener('click',closePage);
  document.addEventListener('keydown',function(e){if(e.key==='Escape')closePage();});
  var activeS={},activeC={};
  function apply(){
    var st=[],cp=[];for(var k in activeS)if(activeS[k])st.push(k);for(var c in activeC)if(activeC[c])cp.push(c);
    var q=document.getElementById('search').value.toLowerCase();
    var groups=tbl.querySelectorAll('tbody.tgroup');
    for(var i=0;i<groups.length;i++){
      var g=groups[i];
      var gstatus=g.getAttribute('data-status');
      var gcomps=g.getAttribute('data-compilers').split(' ');
      var gsearch=g.getAttribute('data-search').toLowerCase();
      var gshow=true;
      if(st.length&&st.indexOf(gstatus)<0)gshow=false;
      if(gshow&&cp.length){var hit=false;for(var j=0;j<gcomps.length;j++)if(cp.indexOf(gcomps[j])>=0){hit=true;break;}if(!hit)gshow=false;}
      if(gshow&&q&&gsearch.indexOf(q)<0)gshow=false;
      var crows=g.querySelectorAll('tr.crow');
      var anyVisible=false;
      for(var m=0;m<crows.length;m++){
        var cr=crows[m];var cshow=true;
        if(cp.length&&cp.indexOf(cr.getAttribute('data-compiler'))<0)cshow=false;
        if(st.length&&st.indexOf(cr.getAttribute('data-status'))<0)cshow=false;
        cr.style.display=cshow?'':'none';
        if(cshow)anyVisible=true;
      }
      g.style.display=(gshow&&anyVisible)?'':'none';
    }
  }
  function bind(boxId,attr,map){var b=document.getElementById(boxId).getElementsByClassName('btn');for(var i=0;i<b.length;i++){(function(btn){btn.addEventListener('click',function(){var s=btn.getAttribute(attr);map[s]=!map[s];btn.classList.toggle('active',map[s]);apply();});})(b[i]);}}
  bind('statusBtns','data-s',activeS);
  bind('compilerBtns','data-name',activeC);
  document.getElementById('search').addEventListener('input',apply);
})();
</script>";

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
