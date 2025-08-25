namespace PenguinLangSyntax
{
    public record SourceLocation(string FileName, string FileNameIdentifier, int RowStart, int RowEnd, int ColStart, int ColEnd) : IComparable<SourceLocation>
    {
        private static ulong count = 0;

        public static SourceLocation Empty() => new SourceLocation("_anonymous", $"anonymous_{count++}", 0, 0, 0, 0);

        public SourceLocation StartLocation => new SourceLocation(FileName, FileNameIdentifier, RowStart, RowStart, ColStart, ColStart);

        public SourceLocation EndLocation => new SourceLocation(FileName, FileNameIdentifier, RowEnd, RowEnd, ColEnd, ColEnd);

        public static SourceLocation From(string filename, ParserRuleContext context)
        {
            var identifier = $"{Path.GetFileNameWithoutExtension(filename)}_{((uint)filename.GetHashCode()) % 0xFFFF}";

            if (context.Start.Line == context.Stop.Line && context.Start.Column == context.Stop.Column && context.GetText() is string text)
            {
                var row = context.Start.Line;
                var col = context.Start.Column;
                var lines = text.Split(new string[] { @"\r\n", @"\n", @"\r" }, StringSplitOptions.None);
                row += lines.Length - 1;
                col = lines.Length == 1 ? col + lines[0].Length : lines[lines.Length - 1].Length;
                return new SourceLocation(filename, identifier, context.Start.Line, row, context.Start.Column, col);
            }

            return new SourceLocation(filename, identifier, context.Start.Line, context.Stop.Line, context.Start.Column, context.Stop.Column);
        }

        public bool Contains(SourceLocation other)
        {
            return Path.GetFullPath(FileName).ToLower() == Path.GetFullPath(other.FileName).ToLower() &&
                RowStart <= other.RowStart &&
                RowEnd >= other.RowEnd &&
                (RowStart != other.RowStart || ColStart <= other.ColStart) &&
                (RowEnd != other.RowEnd || ColEnd >= other.ColEnd);
        }

        public string GetText(string text)
        {
            var lines = text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            // Handle out of range cases
            if (RowStart > lines.Length || RowEnd < 1 || RowStart > RowEnd)
                return string.Empty;

            // Adjust to 0-based index
            var startRow = Math.Max(0, RowStart - 1);
            var endRow = Math.Min(lines.Length - 1, RowEnd - 1);

            var result = new System.Text.StringBuilder();

            for (int i = startRow; i <= endRow; i++)
            {
                var line = lines[i];
                var startCol = i == startRow ? Math.Max(0, ColStart - 1) : 0;
                var endCol = i == endRow ? Math.Min(line.Length, ColEnd) : line.Length;

                if (startCol < endCol)
                {
                    result.Append(line[startCol..endCol]);
                    if (i < endRow)
                        result.AppendLine();
                }
            }

            return result.ToString();
        }

        public override string ToString()
        {
            return $"{FileName}:{RowStart}";
        }

        public int CompareTo(SourceLocation? other)
        {
            if (other is null) return 1;

            var rowComparison = RowStart.CompareTo(other.RowStart);
            if (rowComparison != 0) return rowComparison;

            return ColStart.CompareTo(other.ColStart);
        }

        public static bool operator <(SourceLocation left, SourceLocation right) =>
            left.CompareTo(right) < 0;

        public static bool operator >(SourceLocation left, SourceLocation right) =>
            left.CompareTo(right) > 0;

        public static bool operator <=(SourceLocation left, SourceLocation right) =>
            left.CompareTo(right) <= 0;

        public static bool operator >=(SourceLocation left, SourceLocation right) =>
            left.CompareTo(right) >= 0;


    }

    public static class ParserRuleContextHelper
    {
        public static string GetRawText(this ParserRuleContext context)
        {
            return context.Start.InputStream.GetText(new Interval(context.Start.StartIndex, context.Stop.StopIndex));
        }
    }
}