using System.Text.RegularExpressions;

namespace EmperorPenguin.Tests;

/// <summary>
/// Compares two IR (or LLVM IR) text strings for semantic equality.
/// Temporary variables (prefixed with %) are normalized by mapping them
/// consistently so that structurally identical code with different temp names
/// compares as equal. Labels are also normalized.
/// </summary>
public static class IRSemanticEqual
{
    /// <summary>
    /// Asserts that two IR texts are semantically equal.
    /// Normalizes %temp variable names and compares the normalized forms.
    /// </summary>
    public static void AssertSemanticallyEqual(string expected, string actual)
    {
        var normalizedExpected = NormalizeWhitespace(NormalizeTempVars(expected));
        var normalizedActual = NormalizeWhitespace(NormalizeTempVars(actual));

        if (normalizedExpected != normalizedActual)
        {
            // Find first difference for better error message
            var minLen = Math.Min(normalizedExpected.Length, normalizedActual.Length);
            var diffPos = 0;
            for (int i = 0; i < minLen; i++)
            {
                if (normalizedExpected[i] != normalizedActual[i])
                {
                    diffPos = i;
                    break;
                }
                diffPos = minLen;
            }

            var expContext = GetContext(normalizedExpected, diffPos);
            var actContext = GetContext(normalizedActual, diffPos);

            Xunit.Assert.Equal(
                normalizedExpected,
                normalizedActual);
        }

        // Use xUnit's Assert.Equal for the actual comparison so test runners show proper diff
        Xunit.Assert.Equal(normalizedExpected, normalizedActual);
    }

    /// <summary>
    /// Normalizes temp variable names in IR text.
    /// Each unique %name gets a sequential canonical name like %v0, %v1, etc.
    /// The mapping is scoped per-function (reset between function definitions).
    /// </summary>
    public static string NormalizeTempVars(string ir)
    {
        var lines = ir.Split('\n');
        var result = new List<string>();
        var varMap = new Dictionary<string, string>();
        var varCounter = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Reset variable mapping at function/define boundaries
            if (trimmed.StartsWith("define ") || trimmed.StartsWith("declare "))
            {
                varMap.Clear();
                varCounter = 0;
            }

            // Reset at function-like IR headers too
            if (trimmed.StartsWith("fun ") || trimmed.StartsWith("initial "))
            {
                varMap.Clear();
                varCounter = 0;
            }

            var normalizedLine = NormalizeLine(line, varMap, ref varCounter);
            result.Add(normalizedLine);
        }

        return string.Join("\n", result).Trim();
    }

    private static string NormalizeLine(string line, Dictionary<string, string> varMap, ref int counter)
    {
        // Match %identifier patterns (temp vars, labels, params)
        // Use array wrapper to allow mutation inside lambda
        int[] ctr = [counter];
        var result = Regex.Replace(line, @"%([a-zA-Z_][a-zA-Z0-9_]*)", match =>
        {
            var name = match.Value; // includes the %
            if (!varMap.TryGetValue(name, out var canonical))
            {
                canonical = $"%v{ctr[0]}";
                varMap[name] = canonical;
                ctr[0]++;
            }
            return canonical;
        });
        counter = ctr[0];
        return result;
    }

    /// <summary>
    /// Normalizes whitespace: collapses runs of whitespace to a single space,
    /// strips leading/trailing whitespace from each line, and removes blank lines.
    /// </summary>
    private static string NormalizeWhitespace(string ir)
    {
        var lines = ir.Split('\n');
        var result = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim('\r', '\n', '\t', ' ');
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }
        return string.Join("\n", result);
    }

    private static string GetContext(string text, int pos, int contextChars = 40)
    {
        var start = Math.Max(0, pos - contextChars / 2);
        var end = Math.Min(text.Length, pos + contextChars / 2);
        return text.Substring(start, end - start).Replace("\n", "\\n");
    }
}
