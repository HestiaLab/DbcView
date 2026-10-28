namespace DbcView.Domain;

/// <summary>Severity of a DBC parse diagnostic.</summary>
public enum DbcDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// A single problem found while parsing DBC content. Line numbers are 1-based;
/// zero means the problem applies to the document as a whole.
/// </summary>
public sealed record DbcDiagnostic(
    DbcDiagnosticSeverity Severity,
    string Code,
    string Message,
    int LineNumber)
{
    public override string ToString()
    {
        return LineNumber > 0 ? $"Line {LineNumber}: {Message}" : Message;
    }
}

/// <summary>Stable diagnostic codes used by tests and UI filtering.</summary>
public static class DbcDiagnosticCodes
{
    public const string MissingVersion = "DBC001";
    public const string MissingNodes = "DBC002";
    public const string MalformedMessageHeader = "DBC003";
    public const string MalformedSignal = "DBC004";
    public const string InvalidNumber = "DBC005";
    public const string InvalidBitRange = "DBC006";
    public const string InvalidMessageLength = "DBC007";
    public const string MalformedVersion = "DBC008";
    public const string SignalWithoutMessage = "DBC009";
    public const string DuplicateVersion = "DBC010";
}

/// <summary>
/// Result of parsing DBC text. <see cref="Document"/> always carries the
/// structurally parseable subset, so callers can inspect what was understood;
/// <see cref="HasErrors"/> decides whether that subset may be treated as a
/// trustworthy DBC document.
/// </summary>
public sealed class DbcParseResult
{
    public DbcParseResult(DbcDocument document, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    public DbcDocument Document { get; }

    public IReadOnlyList<DbcDiagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DbcDiagnosticSeverity.Error);

    public bool IsValid => !HasErrors;

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DbcDiagnosticSeverity.Error);
}
