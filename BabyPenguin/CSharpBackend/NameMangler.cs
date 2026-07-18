using System.Collections.Generic;
using System.Text;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>
    /// Produces C#-safe identifiers from Penguin symbol/IR names.
    /// The interpreter's SanitizeName only replaces '.' and leaves '&lt;&gt;,' intact
    /// (invalid in C#), so the C# backend uses its own mangling.
    /// </summary>
    public class NameMangler
    {
        private readonly Dictionary<string, string> _cache = new();
        private readonly Dictionary<string, int> _used = new();

        public string Mangle(string name)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;
            var sb = new StringBuilder();
            foreach (var ch in name)
                sb.Append(IsSafe(ch) ? ch : '_');
            var baseName = sb.ToString();
            if (!IsSafeStart(baseName.Length > 0 ? baseName[0] : '_'))
                baseName = "_" + baseName;
            var unique = baseName;
            if (_used.TryGetValue(baseName, out var count))
            {
                count++;
                _used[baseName] = count;
                unique = baseName + "_" + count;
            }
            else
            {
                _used[baseName] = 0;
            }
            _cache[name] = unique;
            return unique;
        }

        private static bool IsSafe(char ch) =>
            (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
            (ch >= '0' && ch <= '9') || ch == '_';

        private static bool IsSafeStart(char ch) =>
            (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_';
    }
}
