using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PenguinLangAntlr
{
    public class PenguinLangException(string message) : Exception(message)
    {
    }


    public record SourceLocation(string FileName, string FileNameIdentifier, int RowStart, int RowEnd, int ColStart, int ColEnd)
    {
        private static ulong count = 0;

        public static SourceLocation Empty() => new SourceLocation("<anonymous>", $"anonymous_{count++}", 0, 0, 0, 0);
    }

    public class ErrorReporter
    {
        public List<DiagnosticMessage> Errors { get; set; } = new List<DiagnosticMessage>();

        public void Write(DiagnosticLevel level, string message, SourceLocation sourceLocation)
        {
            var msg = new DiagnosticMessage(level, message, sourceLocation);
            Console.WriteLine(msg.ToString());
            this.Errors.Add(msg);
        }

        [DoesNotReturn]
        public void Throw(string message, SourceLocation? sourceLocation = null)
        {
            var msg = new DiagnosticMessage(DiagnosticLevel.Error, message, sourceLocation ?? SourceLocation.Empty());
            Console.WriteLine(msg.ToString());
            this.Errors.Add(msg);
            throw new PenguinLangException(msg.ToString());
        }

        public void Write(DiagnosticLevel level, string message)
        {
            var msg = new DiagnosticMessage(level, message);
            Console.WriteLine(msg.ToString());
            this.Errors.Add(msg);
        }

        public string GenerateReport()
        {
            string report = "";
            foreach (var error in this.Errors)
            {
                report += error.ToString() + Environment.NewLine;
            }
            return report;
        }
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