using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace PenguinLangParser
{
    public class PenguinLangException(string message, string? currentContext, ErrorCode code = ErrorCode.E_PARSE) : Exception(message)
    {
        public string CurrentContext { get; set; } = currentContext ?? "";
        public ErrorCode Code { get; } = code;
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

    public class ErrorReporter(TextWriter? writer = null, DiagnosticLevel diagnosticLevel = DiagnosticLevel.Debug, TextWriter? errorWriter = null)
    {
        private readonly TextWriter writer = writer ?? new BlackholeWriter();
        private readonly TextWriter errorWriter = errorWriter ?? new BlackholeWriter();

        public List<DiagnosticMessage> Messages { get; set; } = [];

        StringBuilder stringBuilder = new StringBuilder();

        public DiagnosticLevel DiagnosticLevel { get; set; } = diagnosticLevel;

        public void Write(DiagnosticLevel level, string message, SourceLocation sourceLocation, ErrorCode code = ErrorCode.E_INTERNAL)
        {
            if ((int)level <= (int)DiagnosticLevel)
            {
                var msg = new DiagnosticMessage(level, message, sourceLocation, code);
                var target = level == DiagnosticLevel.Error ? errorWriter : writer;
                target.WriteLine(msg.ToString());
                Messages.Add(msg);
            }
        }

        public void Write(DiagnosticLevel level, string message, ErrorCode code = ErrorCode.E_INTERNAL)
        {
            if ((int)level <= (int)DiagnosticLevel)
            {
                var msg = new DiagnosticMessage(level, message, null, code);
                var target = level == DiagnosticLevel.Error ? errorWriter : writer;
                target.WriteLine(msg.ToString());
                Messages.Add(msg);
            }
        }

        public class DiagnosticMessage
        {
            public DiagnosticMessage(DiagnosticLevel level, string message, SourceLocation? sourceLocation = null, ErrorCode code = ErrorCode.E_INTERNAL)
            {
                Level = level;
                Message = message;
                SourceLocation = sourceLocation;
                Code = code;
            }

            public DiagnosticLevel Level { get; set; }
            public string Message { get; set; }
            public SourceLocation? SourceLocation { get; set; }
            public ErrorCode Code { get; set; }

            public override string ToString()
            {
                if (Level == DiagnosticLevel.Error)
                {
                    var loc = SourceLocation != null
                        ? $" (at {SourceLocation.FileName}:{SourceLocation.RowStart},{SourceLocation.ColStart})"
                        : "";
                    return $"error[{Code}]: {Message}{loc}";
                }
                else
                {
                    if (SourceLocation != null)
                        return $"{Level}: {Message} (at {SourceLocation.FileName}:{SourceLocation.RowStart},{SourceLocation.ColStart})";
                    else
                        return $"{Level}: {Message}";
                }
            }
        }
    }
}
