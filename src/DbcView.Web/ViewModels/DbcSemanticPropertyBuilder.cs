using System.Globalization;
using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Web.ViewModels;

public enum DbcAttributeSource
{
    Definition,
    Default,
    Explicit,
    Undefined,
}

public sealed record DbcPropertyComment(
    string Text,
    int LineNumber,
    bool IsHighlighted);

public sealed record DbcPropertyAttribute(
    string Name,
    string? Value,
    string? DefaultValue,
    string? ValueType,
    string? Range,
    IReadOnlyList<string> EnumValues,
    DbcAttributeSource Source,
    bool IsDuplicate,
    bool IsEffective,
    bool IsUndefined,
    int LineNumber);

public sealed record DbcPropertyValueTable(
    string Name,
    IReadOnlyList<DbcValueDescription> Entries,
    int LineNumber,
    bool IsMissingTable,
    int DuplicateCount = 1);

public sealed record DbcPropertyStatement(
    string Keyword,
    string Preview,
    int LineNumber,
    string? Code = null);

public sealed record DbcPropertyStatementGroup(
    string Keyword,
    IReadOnlyList<DbcPropertyStatement> Items);

public sealed record DbcPropertySections(
    bool Available,
    string? UnavailableReason,
    IReadOnlyList<DbcPropertyComment> Comments,
    IReadOnlyList<DbcPropertyAttribute> Attributes,
    DbcPropertyValueTable? ValueTable,
    bool HasExtendedMultiplexing,
    IReadOnlyList<DbcPropertyStatementGroup> NotDisplayed,
    IReadOnlyList<DbcPropertyStatementGroup> NotInterpreted)
{
    public static DbcPropertySections Unavailable(string reason)
    {
        return new DbcPropertySections(
            false,
            reason,
            Array.Empty<DbcPropertyComment>(),
            Array.Empty<DbcPropertyAttribute>(),
            null,
            false,
            Array.Empty<DbcPropertyStatementGroup>(),
            Array.Empty<DbcPropertyStatementGroup>());
    }
}

/// <summary>
/// Projects the semantic index into the read-only Properties sections. The
/// builder is UI-independent so source lines, duplicate rules and the
/// Not displayed / Not interpreted split stay testable.
/// </summary>
public static class DbcSemanticPropertyBuilder
{
    public static DbcPropertySections Build(
        DbcFileEntry? file,
        DbcMessage? message,
        DbcSignal? signal,
        int? highlightedCommentLine = null)
    {
        if (file is null)
        {
            return DbcPropertySections.Unavailable("Open a file to inspect its semantics.");
        }

        if (file.ParseResult.HasErrors)
        {
            return DbcPropertySections.Unavailable(
                $"{file.ParseResult.ErrorCount} DBC error(s) prevent semantic inspection.");
        }

        var semantics = file.ParseResult.Semantics;
        if (semantics is null)
        {
            return DbcPropertySections.Unavailable(
                "Semantic information is not available for this document.");
        }

        var scope = signal is not null
            ? DbcSemanticScope.Signal
            : message is not null
                ? DbcSemanticScope.Message
                : DbcSemanticScope.File;
        var comments = BuildComments(
            semantics,
            scope,
            message,
            signal,
            highlightedCommentLine);
        var attributes = BuildAttributes(semantics, scope, message, signal);
        var valueTable = signal is null || message is null
            ? null
            : BuildValueTable(semantics, message, signal);
        var extendedMultiplexing = message is not null
            && semantics.CommonStatements
                .OfType<DbcExtendedMultiplexStatement>()
                .Any(statement => statement.MessageId == message.Id);
        var notDisplayed = BuildNotDisplayed(semantics, message, signal);
        var notInterpreted = BuildNotInterpreted(file, semantics, signal is null && message is null);

        return new DbcPropertySections(
            true,
            null,
            comments,
            attributes,
            valueTable,
            extendedMultiplexing,
            Group(notDisplayed),
            Group(notInterpreted));
    }

    private static IReadOnlyList<DbcPropertyComment> BuildComments(
        DbcSemanticIndex semantics,
        DbcSemanticScope scope,
        DbcMessage? message,
        DbcSignal? signal,
        int? highlightedCommentLine)
    {
        var comments = scope switch
        {
            DbcSemanticScope.File => semantics.FileComments,
            DbcSemanticScope.Message => semantics.MessageComments.Where(
                comment => comment.MessageId == message?.Id),
            DbcSemanticScope.Signal => semantics.SignalComments.Where(
                comment => comment.MessageId == message?.Id
                    && string.Equals(
                        comment.SignalName,
                        signal?.Name,
                        StringComparison.Ordinal)),
            _ => Array.Empty<DbcCommentEntry>(),
        };

        return comments
            .Select(comment => new DbcPropertyComment(
                comment.Text,
                comment.LineNumber,
                comment.LineNumber == highlightedCommentLine))
            .ToArray();
    }

    private static IReadOnlyList<DbcPropertyAttribute> BuildAttributes(
        DbcSemanticIndex semantics,
        DbcSemanticScope scope,
        DbcMessage? message,
        DbcSignal? signal)
    {
        var definitions = semantics.AttributeDefinitions
            .Where(definition => definition.Scope == scope)
            .ToArray();
        var defaults = new Dictionary<string, DbcAttributeDefault>(StringComparer.Ordinal);
        foreach (var attributeDefault in semantics.AttributeDefaults)
        {
            defaults[attributeDefault.Name] = attributeDefault;
        }

        var values = semantics.AttributeValues
            .Where(value => value.Scope == scope && MatchesTarget(value, message, signal))
            .ToArray();
        var definitionNames = new HashSet<string>(
            definitions.Select(definition => definition.Name),
            StringComparer.Ordinal);
        var rows = new List<DbcPropertyAttribute>();

        foreach (var definition in definitions)
        {
            defaults.TryGetValue(definition.Name, out var attributeDefault);
            var matchingValues = values
                .Where(value => string.Equals(value.Name, definition.Name, StringComparison.Ordinal))
                .ToArray();
            var defaultValue = attributeDefault?.RawValue;
            var range = FormatRange(definition);

            if (matchingValues.Length == 0)
            {
                rows.Add(new DbcPropertyAttribute(
                    definition.Name,
                    defaultValue,
                    defaultValue,
                    definition.ValueType,
                    range,
                    definition.EnumValues,
                    defaultValue is null ? DbcAttributeSource.Definition : DbcAttributeSource.Default,
                    false,
                    false,
                    false,
                    attributeDefault?.LineNumber ?? definition.LineNumber));
                continue;
            }

            for (var index = 0; index < matchingValues.Length; index++)
            {
                var value = matchingValues[index];
                rows.Add(new DbcPropertyAttribute(
                    definition.Name,
                    value.RawValue,
                    defaultValue,
                    definition.ValueType,
                    range,
                    definition.EnumValues,
                    DbcAttributeSource.Explicit,
                    matchingValues.Length > 1,
                    index == matchingValues.Length - 1,
                    false,
                    value.LineNumber));
            }
        }

        foreach (var value in values.Where(
                     value => !definitionNames.Contains(value.Name)))
        {
            rows.Add(new DbcPropertyAttribute(
                value.Name,
                value.RawValue,
                null,
                null,
                null,
                Array.Empty<string>(),
                DbcAttributeSource.Undefined,
                false,
                true,
                true,
                value.LineNumber));
        }

        return rows;
    }

    private static bool MatchesTarget(
        DbcAttributeValue value,
        DbcMessage? message,
        DbcSignal? signal)
    {
        return value.Scope switch
        {
            DbcSemanticScope.File => true,
            DbcSemanticScope.Message => value.MessageId == message?.Id,
            DbcSemanticScope.Signal => value.MessageId == message?.Id
                && string.Equals(value.SignalName, signal?.Name, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static DbcPropertyValueTable? BuildValueTable(
        DbcSemanticIndex semantics,
        DbcMessage message,
        DbcSignal signal)
    {
        var matches = semantics.SignalValueTables
            .Where(candidate => candidate.MessageId == message.Id
                && string.Equals(candidate.SignalName, signal.Name, StringComparison.Ordinal))
            .ToArray();
        var table = matches.LastOrDefault();
        if (table is null)
        {
            return null;
        }

        if (table.ReferencedTable is { } tableName)
        {
            var definition = semantics.ValueTables.LastOrDefault(
                candidate => string.Equals(candidate.Name, tableName, StringComparison.Ordinal));
            return new DbcPropertyValueTable(
                tableName,
                definition?.Entries ?? Array.Empty<DbcValueDescription>(),
                table.LineNumber,
                definition is null,
                matches.Length);
        }

        return new DbcPropertyValueTable(
            "Inline values",
            table.Entries,
            table.LineNumber,
            false,
            matches.Length);
    }

    private static IEnumerable<DbcPropertyStatement> BuildNotDisplayed(
        DbcSemanticIndex semantics,
        DbcMessage? message,
        DbcSignal? signal)
    {
        if (signal is not null)
        {
            foreach (var statement in semantics.CommonStatements)
            {
                if (MatchesSignalCommon(statement, message, signal))
                {
                    yield return ToStatement(statement);
                }
            }

            yield break;
        }

        if (message is not null)
        {
            foreach (var statement in semantics.CommonStatements)
            {
                if (MessageIdOf(statement) == message.Id)
                {
                    yield return ToStatement(statement);
                }
            }

            yield break;
        }

        foreach (var statement in semantics.CommonStatements)
        {
            yield return ToStatement(statement);
        }

        foreach (var comment in semantics.NodeComments.Concat(semantics.EnvironmentComments))
        {
            yield return new DbcPropertyStatement(
                comment.Scope == DbcSemanticScope.Node ? "CM_ BU_" : "CM_ EV_",
                comment.Text,
                comment.LineNumber);
        }

        foreach (var definition in semantics.AttributeDefinitions.Where(
                     definition => definition.Scope is DbcSemanticScope.Node
                         or DbcSemanticScope.EnvironmentVariable))
        {
            yield return new DbcPropertyStatement(
                definition.Scope == DbcSemanticScope.Node ? "BA_DEF_ BU_" : "BA_DEF_ EV_",
                $"{definition.Name} {definition.ValueType} {FormatRange(definition)}".Trim(),
                definition.LineNumber);
        }

        foreach (var value in semantics.AttributeValues.Where(
                     value => value.Scope is DbcSemanticScope.Node
                         or DbcSemanticScope.EnvironmentVariable))
        {
            yield return new DbcPropertyStatement(
                value.Scope == DbcSemanticScope.Node ? "BA_ BU_" : "BA_ EV_",
                $"{value.Name} {value.TargetName} {value.RawValue}".Trim(),
                value.LineNumber);
        }
    }

    private static bool MatchesSignalCommon(
        DbcCommonStatement statement,
        DbcMessage? message,
        DbcSignal signal)
    {
        return statement switch
        {
            DbcSignalValueTypeStatement valueType
                => valueType.MessageId == message?.Id
                    && string.Equals(valueType.SignalName, signal.Name, StringComparison.Ordinal),
            DbcExtendedMultiplexStatement multiplex
                => multiplex.MessageId == message?.Id
                    && (string.Equals(multiplex.SignalName, signal.Name, StringComparison.Ordinal)
                        || string.Equals(
                            multiplex.MultiplexerSignalName,
                            signal.Name,
                            StringComparison.Ordinal)),
            DbcSignalGroupStatement group
                => group.MessageId == message?.Id
                    && group.SignalNames.Contains(signal.Name, StringComparer.Ordinal),
            DbcRelationAttributeValue relation
                => relation.MessageId == message?.Id
                    && string.Equals(relation.SignalName, signal.Name, StringComparison.Ordinal),
            DbcRelationStatement relation
                => relation.MessageId == message?.Id
                    && string.Equals(relation.SignalName, signal.Name, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static uint? MessageIdOf(DbcCommonStatement statement)
    {
        return statement switch
        {
            DbcTransmitterStatement value => value.MessageId,
            DbcSignalGroupStatement value => value.MessageId,
            DbcSignalValueTypeStatement value => value.MessageId,
            DbcExtendedMultiplexStatement value => value.MessageId,
            DbcRelationAttributeValue value => value.MessageId,
            DbcRelationStatement value => value.MessageId,
            _ => null,
        };
    }

    private static DbcPropertyStatement ToStatement(DbcCommonStatement statement)
    {
        return new DbcPropertyStatement(
            statement.Keyword,
            Preview(statement.RawText),
            statement.LineNumber);
    }

    private static IEnumerable<DbcPropertyStatement> BuildNotInterpreted(
        DbcFileEntry file,
        DbcSemanticIndex semantics,
        bool includeDocumentScope)
    {
        if (!includeDocumentScope)
        {
            yield break;
        }

        foreach (var statement in semantics.OpaqueStatements)
        {
            yield return new DbcPropertyStatement(
                statement.Keyword,
                Preview(statement.RawText),
                statement.LineNumber);
        }

        foreach (var diagnostic in file.ParseResult.Diagnostics.Where(
                     diagnostic => diagnostic.LineNumber > 0))
        {
            yield return new DbcPropertyStatement(
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.LineNumber,
                diagnostic.Code);
        }
    }

    private static IReadOnlyList<DbcPropertyStatementGroup> Group(
        IEnumerable<DbcPropertyStatement> statements)
    {
        return statements
            .GroupBy(statement => statement.Keyword, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new DbcPropertyStatementGroup(
                group.Key,
                group.OrderBy(statement => statement.LineNumber).ToArray()))
            .ToArray();
    }

    private static string? FormatRange(DbcAttributeDefinition definition)
    {
        if (definition.EnumValues.Count > 0)
        {
            return string.Join(" | ", definition.EnumValues);
        }

        if (definition.Minimum is null && definition.Maximum is null)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatNumber(definition.Minimum)} … {FormatNumber(definition.Maximum)}");
    }

    private static string FormatNumber(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture) ?? "?";
    }

    private static string Preview(string raw, int maxLength = 120)
    {
        var newline = raw.IndexOf('\n');
        var line = (newline < 0 ? raw : raw[..newline]).Trim();
        return line.Length <= maxLength ? line : line[..maxLength] + "…";
    }
}
