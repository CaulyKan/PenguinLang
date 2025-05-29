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

    public enum DiagnosticLevel
    {
        Error,
        Warning,
        Info,
        Debug
    }

    public class BlackholeWriter : TextWriter
    {
        public override Encoding Encoding => throw new NotImplementedException();
    }

    public class ErrorReporter(TextWriter? writer = null, DiagnosticLevel diagnosticLevel = DiagnosticLevel.Debug)
    {
        private readonly TextWriter writer = writer ?? new BlackholeWriter();

        public List<DiagnosticMessage> Messages { get; set; } = [];

        StringBuilder stringBuilder = new StringBuilder();

        public DiagnosticLevel DiagnosticLevel { get; set; } = diagnosticLevel;

        public void Write(DiagnosticLevel level, string message, SourceLocation sourceLocation)
        {
            if ((int)level <= (int)DiagnosticLevel)
            {
                var msg = new DiagnosticMessage(level, message, sourceLocation);
                writer.WriteLine(msg.ToString());
                Messages.Add(msg);
            }
        }

        public void Write(DiagnosticLevel level, string message)
        {
            if ((int)level <= (int)DiagnosticLevel)
            {
                var msg = new DiagnosticMessage(level, message);
                writer.WriteLine(msg.ToString());
                Messages.Add(msg);
            }
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