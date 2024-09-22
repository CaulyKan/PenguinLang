using System;
using System.Collections.Generic;

namespace PenguinLangAntlr
{
    public record SourceLocation(string FileName, string FileNameIdentifier, int RowStart, int RowEnd, int ColStart, int ColEnd)
    {
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

        public void Throw(string message, SourceLocation sourceLocation)
        {
            var msg = new DiagnosticMessage(DiagnosticLevel.Error, message, sourceLocation);
            Console.WriteLine(msg.ToString());
            this.Errors.Add(msg);
            throw new Exception(msg.ToString());
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