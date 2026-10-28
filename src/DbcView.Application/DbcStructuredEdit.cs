using DbcView.Domain;

namespace DbcView.Application;

public enum DbcStructuredEditKind
{
    Comment,
    Attribute,
    ValueTable,
}

/// <summary>One semantic edit targeting a single DBC statement.</summary>
public sealed record DbcStructuredEdit(
    DbcStructuredEditKind Kind,
    DbcSemanticScope Scope,
    uint? MessageId = null,
    string? SignalName = null,
    int? ExistingLine = null,
    bool Delete = false,
    string? CommentText = null,
    string? AttributeName = null,
    string? AttributeValue = null,
    string? AttributeValueType = null,
    IReadOnlyList<DbcValueDescription>? ValueEntries = null);

public sealed record DbcStatementDiff(
    int LineNumber,
    string Change,
    string? OldText,
    string? NewText);

public sealed record DbcStructuredEditBuild(
    bool Success,
    string? Error,
    string Content,
    DbcStatementDiff? Diff)
{
    public static DbcStructuredEditBuild Fail(string error) =>
        new(false, error, string.Empty, null);
}

public sealed record DbcStructuredEditOutcome(
    bool Success,
    string? Error,
    DbcStatementDiff? Diff,
    DbcParseResult? ParseResult,
    IReadOnlyList<DbcDiagnostic> Diagnostics)
{
    public static DbcStructuredEditOutcome Fail(
        string error,
        IReadOnlyList<DbcDiagnostic>? diagnostics = null) =>
        new(false, error, null, null, diagnostics ?? Array.Empty<DbcDiagnostic>());
}
