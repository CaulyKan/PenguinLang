namespace BabyPenguin.VirtualMachine
{
    public class IRSourceLocation
    {
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }

        public IRSourceLocation(string filePath, int line, int column)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
        }

        public static IRSourceLocation Empty => new("", 0, 0);

        public override string ToString() => string.IsNullOrEmpty(FilePath) ? "" : $"{FilePath}:{Line}:{Column}";
    }
}
