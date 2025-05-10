using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace PenguinLangSyntax
{
    public class PenguinLangException(string message) : Exception(message)
    {
    }


    public record SourceLocation(string FileName, string FileNameIdentifier, int RowStart, int RowEnd, int ColStart, int ColEnd)
    {
        private static ulong count = 0;

        public static SourceLocation Empty() => new SourceLocation("_anonymous", $"anonymous_{count++}", 0, 0, 0, 0);

        public SourceLocation StartLocation => new SourceLocation(FileName, FileNameIdentifier, RowStart, RowStart, ColStart, ColStart);

        public SourceLocation EndLocation => new SourceLocation(FileName, FileNameIdentifier, RowEnd, RowEnd, ColEnd, ColEnd);

        public override string ToString()
        {
            return $"{FileName}:{RowStart}";
        }
    }

    public class ErrorReporter(TextWriter? writer = null)
    {
        private readonly TextWriter writer = writer ?? Console.Out;

        public List<DiagnosticMessage> Errors { get; set; } = [];

        StringBuilder stringBuilder = new StringBuilder();

        public void Write(DiagnosticLevel level, string message, SourceLocation sourceLocation)
        {
            var msg = new DiagnosticMessage(level, message, sourceLocation);
            writer.WriteLine(msg.ToString());
            Errors.Add(msg);
            stringBuilder.AppendLine(msg.ToString());
        }

        public void Write(DiagnosticLevel level, string message)
        {
            var msg = new DiagnosticMessage(level, message);
            writer.WriteLine(msg.ToString());
            Errors.Add(msg);
            stringBuilder.AppendLine(msg.ToString());
        }

        public string Report => stringBuilder.ToString();

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