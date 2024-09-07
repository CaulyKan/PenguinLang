using System.Collections.Generic;

namespace PenguinLangAntlr
{
    public class ErrorReporter
    {
        public List<DiagnosticMessage> Errors { get; set; } = new List<DiagnosticMessage>();

        public void Write(DiagnosticLevel level, string message, int line, int column, string file)
        {
            this.Errors.Add(new DiagnosticMessage(level, message, line, column, file));
        }
        public void Write(DiagnosticLevel level, string message)
        {
            this.Errors.Add(new DiagnosticMessage(level, message));
        }

        public string GenerateReport()
        {
            string report = "";
            foreach (var error in this.Errors)
            {
                if (error.HasSource)
                    report += $"{error.Level}: {error.Message} (at {error.File}:{error.Line},{error.Column})\n";
                else report += $"{error.Level}: {error.Message}\n";
            }
            return report;
        }
    }

    public enum DiagnosticLevel
    {
        Error,
        Warning,
        Info
    }

    public class DiagnosticMessage
    {
        public DiagnosticMessage(DiagnosticLevel level, string message)
        {
            Level = level;
            Message = message;
            HasSource = false;
            File = "";
        }
        public DiagnosticMessage(DiagnosticLevel level, string message, int line, int column, string file)
        {
            Level = level;
            Message = message;
            Line = line;
            Column = column;
            File = file;
            HasSource = true;
        }

        public DiagnosticLevel Level { get; set; }
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string File { get; set; }
        public bool HasSource { get; set; }
    }
}