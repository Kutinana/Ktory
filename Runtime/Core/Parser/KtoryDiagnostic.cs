namespace Ktory.Core.Parser
{
    public enum KtoryDiagnosticSeverity
    {
        Error = 1,
        Warning = 2,
        Information = 3,
        Hint = 4
    }

    public sealed class KtoryDiagnostic
    {
        public KtoryDiagnosticSeverity Severity { get; set; } = KtoryDiagnosticSeverity.Error;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }

        public KtoryDiagnostic()
        {
        }

        public KtoryDiagnostic(KtoryDiagnosticSeverity severity, string code, string message, int line, int column, int endLine = 0, int endColumn = 0)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Line = line;
            Column = column;
            EndLine = endLine > 0 ? endLine : line;
            EndColumn = endColumn > 0 ? endColumn : column;
        }
    }
}
