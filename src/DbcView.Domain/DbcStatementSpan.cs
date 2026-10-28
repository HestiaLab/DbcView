namespace DbcView.Domain;

/// <summary>Source range of one parsed statement, with its 1-based start line.</summary>
public readonly record struct DbcStatementSpan(
    int Start,
    int Length,
    int LineNumber);
