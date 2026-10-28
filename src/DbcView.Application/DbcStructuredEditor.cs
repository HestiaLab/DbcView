using System.Globalization;
using System.Text;
using DbcView.Domain;

namespace DbcView.Application;

/// <summary>
/// Statement-span text editor. Only the target statement is rewritten; all
/// untouched bytes (including line endings and blank lines) are preserved.
/// </summary>
public static class DbcStructuredEditor
{
    public static DbcStructuredEditBuild Build(
        DbcFileEntry file,
        DbcStructuredEdit edit)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(edit);

        if (file.ParseResult.HasErrors)
        {
            return DbcStructuredEditBuild.Fail(
                "Fix the DBC errors before editing semantics.");
        }

        var content = file.Content;
        var spans = file.ParseResult.StatementSpans;
        if (!TryRender(edit, out var statement, out var renderError))
        {
            return DbcStructuredEditBuild.Fail(renderError);
        }

        var newline = DetectNewline(content);
        DbcStatementSpan? existing = null;
        if (edit.ExistingLine is int existingLine)
        {
            existing = FindSpan(spans, existingLine);
            if (existing is null)
            {
                return DbcStructuredEditBuild.Fail(
                    $"The statement at line {existingLine} was not found.");
            }
        }

        if (edit.Delete)
        {
            if (existing is not { } span)
            {
                return DbcStructuredEditBuild.Fail("Only an existing statement can be deleted.");
            }

            var statementEnd = FindStatementEnd(content, span);
            var oldText = content[span.Start..statementEnd];
            var deleteStart = span.Start;
            var deleteEnd = statementEnd;
            if (deleteEnd < content.Length)
            {
                if (content[deleteEnd] == '\r'
                    && deleteEnd + 1 < content.Length
                    && content[deleteEnd + 1] == '\n')
                {
                    deleteEnd += 2;
                }
                else if (content[deleteEnd] == '\n')
                {
                    deleteEnd++;
                }
            }
            else if (deleteStart > 0 && content[deleteStart - 1] == '\n')
            {
                deleteStart--;
                if (deleteStart > 0 && content[deleteStart - 1] == '\r')
                {
                    deleteStart--;
                }
            }

            var deleted = content[..deleteStart] + content[deleteEnd..];
            return new DbcStructuredEditBuild(
                true,
                null,
                deleted,
                new DbcStatementDiff(span.LineNumber, "deleted", oldText, null));
        }

        if (existing is { } target)
        {
            var statementEnd = FindStatementEnd(content, target);
            var oldText = content[target.Start..statementEnd];
            var updated = content[..target.Start] + statement + content[statementEnd..];
            return new DbcStructuredEditBuild(
                true,
                null,
                updated,
                new DbcStatementDiff(target.LineNumber, "modified", oldText, statement));
        }

        var anchor = FindAnchor(content, spans, edit);
        if (anchor is { } anchorSpan)
        {
            var insertAt = FindStatementEnd(content, anchorSpan);
            var insertion = newline + statement;
            if (insertAt == content.Length)
            {
                insertion += newline;
            }

            var updated = content[..insertAt] + insertion + content[insertAt..];
            return new DbcStructuredEditBuild(
                true,
                null,
                updated,
                new DbcStatementDiff(anchorSpan.LineNumber + 1, "added", null, statement));
        }

        var prefix = content.Length > 0 && !content.EndsWith('\n') ? newline : string.Empty;
        var appended = content + prefix + statement + newline;
        return new DbcStructuredEditBuild(
            true,
            null,
            appended,
            new DbcStatementDiff(CountLines(content) + 1, "added", null, statement));
    }

    public static string RenderStatement(DbcStructuredEdit edit)
    {
        return TryRender(edit, out var statement, out _)
            ? statement
            : string.Empty;
    }

    private static bool TryRender(
        DbcStructuredEdit edit,
        out string statement,
        out string error)
    {
        statement = string.Empty;
        error = string.Empty;
        switch (edit.Kind)
        {
            case DbcStructuredEditKind.Comment:
                if (edit.Scope == DbcSemanticScope.Message
                    && edit.MessageId is not uint messageId)
                {
                    error = "A message id is required for a message comment.";
                    return false;
                }

                if (edit.Scope == DbcSemanticScope.Signal
                    && (edit.MessageId is not uint signalMessageId
                        || string.IsNullOrWhiteSpace(edit.SignalName)))
                {
                    error = "A message id and signal name are required for a signal comment.";
                    return false;
                }

                statement = edit.Scope switch
                {
                    DbcSemanticScope.File => $"CM_ \"{Escape(edit.CommentText ?? string.Empty)}\";",
                    DbcSemanticScope.Message => string.Create(
                        CultureInfo.InvariantCulture,
                        $"CM_ BO_ {edit.MessageId} \"{Escape(edit.CommentText ?? string.Empty)}\";"),
                    DbcSemanticScope.Signal => string.Create(
                        CultureInfo.InvariantCulture,
                        $"CM_ SG_ {edit.MessageId} {edit.SignalName} \"{Escape(edit.CommentText ?? string.Empty)}\";"),
                    _ => string.Empty,
                };
                if (statement.Length == 0)
                {
                    error = "Only file, message and signal comments are editable.";
                    return false;
                }

                return true;

            case DbcStructuredEditKind.Attribute:
                if (edit.Scope is not (DbcSemanticScope.Message or DbcSemanticScope.Signal)
                    || edit.MessageId is not uint attributeMessageId
                    || string.IsNullOrWhiteSpace(edit.AttributeName)
                    || (edit.Scope == DbcSemanticScope.Signal
                        && string.IsNullOrWhiteSpace(edit.SignalName)))
                {
                    error = "Message or signal attributes require a target and a name.";
                    return false;
                }

                var value = FormatAttributeValue(edit.AttributeValue, edit.AttributeValueType);
                if (value.Length == 0)
                {
                    error = "The attribute value is required.";
                    return false;
                }

                statement = edit.Scope == DbcSemanticScope.Message
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"BA_ \"{Escape(edit.AttributeName)}\" BO_ {attributeMessageId} {value};")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"BA_ \"{Escape(edit.AttributeName)}\" SG_ {attributeMessageId} {edit.SignalName} {value};");
                return true;

            case DbcStructuredEditKind.ValueTable:
                if (edit.MessageId is not uint valueMessageId
                    || string.IsNullOrWhiteSpace(edit.SignalName))
                {
                    error = "A value table requires a message id and signal name.";
                    return false;
                }

                var entries = edit.ValueEntries ?? Array.Empty<DbcValueDescription>();
                if (entries.Count == 0)
                {
                    error = "A value table requires at least one value.";
                    return false;
                }

                var builder = new StringBuilder();
                builder.Append("VAL_ ")
                    .Append(valueMessageId.ToString(CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(edit.SignalName);
                foreach (var entry in entries)
                {
                    var valueText = (entry.Value ?? string.Empty).Trim();
                    if (valueText.Length == 0)
                    {
                        error = "Value table entries need a value.";
                        return false;
                    }

                    builder.Append(' ')
                        .Append(valueText)
                        .Append(" \"")
                        .Append(Escape(entry.Description ?? string.Empty))
                        .Append('"');
                }

                builder.Append(';');
                statement = builder.ToString();
                return true;

            default:
                error = "Unsupported edit kind.";
                return false;
        }
    }

    private static string FormatAttributeValue(string? value, string? valueType)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!string.Equals(valueType, "STRING", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(valueType, "ENUM", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = Unescape(trimmed[1..^1]);
        }

        return $"\"{Escape(trimmed)}\"";
    }

    private static DbcStatementSpan? FindSpan(
        IReadOnlyList<DbcStatementSpan> spans,
        int lineNumber)
    {
        foreach (var span in spans)
        {
            if (span.LineNumber == lineNumber)
            {
                return span;
            }
        }

        return null;
    }

    private static int FindStatementEnd(string content, DbcStatementSpan span)
    {
        var end = span.Start + span.Length;
        while (end > span.Start && char.IsWhiteSpace(content[end - 1]))
        {
            end--;
        }

        return end;
    }

    private static DbcStatementSpan? FindAnchor(
        string content,
        IReadOnlyList<DbcStatementSpan> spans,
        DbcStructuredEdit edit)
    {
        DbcStatementSpan? anchor = null;
        foreach (var span in spans)
        {
            var end = FindStatementEnd(content, span);
            var text = content[span.Start..end];
            if (IsAnchor(text, edit))
            {
                anchor = span;
            }
        }

        return anchor;
    }

    private static bool IsAnchor(string text, DbcStructuredEdit edit)
    {
        if (edit.Kind == DbcStructuredEditKind.Comment)
        {
            return edit.Scope switch
            {
                DbcSemanticScope.File => text.StartsWith("CM_ \"", StringComparison.Ordinal),
                DbcSemanticScope.Message => text.StartsWith(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"CM_ BO_ {edit.MessageId} "),
                    StringComparison.Ordinal),
                DbcSemanticScope.Signal => text.StartsWith(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"CM_ SG_ {edit.MessageId} {edit.SignalName} "),
                    StringComparison.Ordinal),
                _ => false,
            };
        }

        if (edit.Kind == DbcStructuredEditKind.Attribute)
        {
            if (!text.StartsWith("BA_ ", StringComparison.Ordinal))
            {
                return false;
            }

            return edit.Scope == DbcSemanticScope.Message
                ? text.Contains(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $" BO_ {edit.MessageId} "),
                    StringComparison.Ordinal)
                : text.Contains(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $" SG_ {edit.MessageId} {edit.SignalName} "),
                    StringComparison.Ordinal);
        }

        return text.StartsWith("VAL_ ", StringComparison.Ordinal);
    }

    private static string DetectNewline(string content)
    {
        return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static int CountLines(string content)
    {
        var count = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                index++;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }
}
