using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace PenguinLangSyntax
{
    public class PenguinLangException(string message, string? currentContext) : Exception(message)
    {
        public string CurrentContext { get; set; } = currentContext ?? "";
    }


    public record SourceLocation(string FileName, string FileNameIdentifier, int RowStart, int RowEnd, int ColStart, int ColEnd)
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
            return Path.GetFullPath(FileName) == Path.GetFullPath(other.FileName) &&
                RowStart <= other.RowStart &&
                RowEnd >= other.RowEnd &&
                (RowStart != other.RowStart || ColStart <= other.ColStart) &&
                (RowEnd != other.RowEnd || ColEnd >= other.ColEnd);
        }

        public override string ToString()
        {
            return $"{FileName}:{RowStart}";
        }
    }

    public class ErrorReporter(TextWriter? writer = null)
    {
        private readonly TextWriter writer = writer ?? Console.Out;

        public List<DiagnosticMessage> Messages { get; set; } = [];

        StringBuilder stringBuilder = new StringBuilder();

        public void Write(DiagnosticLevel level, string message, SourceLocation sourceLocation)
        {
            var msg = new DiagnosticMessage(level, message, sourceLocation);
            writer.WriteLine(msg.ToString());
            Messages.Add(msg);
        }

        public void Write(DiagnosticLevel level, string message)
        {
            var msg = new DiagnosticMessage(level, message);
            writer.WriteLine(msg.ToString());
            Messages.Add(msg);
        }

        public enum DiagnosticLevel
        {
            Error,
            Warning,
            Info,
            Debug
        }

        public class DiagnosticMessage
        {
            public DiagnosticMessage(DiagnosticLevel level, string message, SourceLocation? sourceLocation = null)
            {
                Level = level;
                Message = message;
                SourceLocation = sourceLocation;
            }

            public DiagnosticLevel Level { get; set; }
            public string Message { get; set; }
            public SourceLocation? SourceLocation { get; set; }
            public override string ToString()
            {
                if (SourceLocation != null)
                    return $"{Level}: {Message} (at {SourceLocation.FileName}:{SourceLocation.RowStart},{SourceLocation.ColStart})";
                else
                    return $"{Level}: {Message}";
            }
        }
    }
}