using System.Globalization;
using System.Text;
using DbcView.Domain;

namespace DbcView.Infrastructure.Syntax;

internal static class DbcSemanticIndexBuilder
{
    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        "BO_TX_BU_",
        "SIG_GROUP_",
        "SIG_VALTYPE_",
        "SIGTYPE_VALTYPE_",
        "SG_MUL_VAL_",
        "EV_",
        "EV_DATA_",
        "ENVVAR_DATA_",
        "SGTYPE_",
        "SGTYPE_VAL_",
        "SIG_TYPE_REF_",
        "BA_DEF_REL_",
        "BA_REL_",
        "BA_DEF_DEF_REL_",
        "BA_DEF_SGTYPE_",
        "BA_SGTYPE_",
        "BU_SG_REL_",
        "BU_EV_REL_",
        "BU_BO_REL_",
        "CAT_DEF_",
        "CAT_",
        "FILTER",
        "NS_DESC_",
        "VECTOR__INDEPENDENT_SIG_MSG",
        "VECTOR__XXX",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> CommonLookup =
        CommonKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

    public static DbcSemanticIndex Build(DbcSyntaxTree tree, DbcDocument document)
    {
        var fileComments = new List<DbcCommentEntry>();
        var nodeComments = new List<DbcCommentEntry>();
        var messageComments = new List<DbcCommentEntry>();
        var signalComments = new List<DbcCommentEntry>();
        var environmentComments = new List<DbcCommentEntry>();
        var attributeDefinitions = new List<DbcAttributeDefinition>();
        var attributeDefaults = new List<DbcAttributeDefault>();
        var attributeValues = new List<DbcAttributeValue>();
        var valueTables = new List<DbcValueTableDefinition>();
        var signalValueTables = new List<DbcSignalValueTable>();
        var commonStatements = new List<DbcCommonStatement>();
        var opaqueStatements = new List<DbcOpaqueStatementEntry>();
        var unresolved = new List<DbcSemanticReference>();

        var messages = new Dictionary<uint, DbcMessage>();
        foreach (var message in document.Messages)
        {
            messages.TryAdd(message.Id, message);
        }

        var signals = new HashSet<(uint MessageId, string Name)>();
        foreach (var message in document.Messages)
        {
            foreach (var signal in message.Signals)
            {
                signals.Add((message.Id, signal.Name));
            }
        }

        var nodes = new HashSet<string>(document.Nodes, StringComparer.Ordinal);
        var line = 0;

        for (var index = 0; index < tree.StatementCount; index++)
        {
            var statement = tree.GetStatement(index);
            var raw = statement.RawText;
            var startLine = line + 1;
            line += CountNewlines(raw);
            var trimmed = FirstLine(raw).TrimStart().TrimStart('\uFEFF');
            var firstWord = FirstWord(trimmed);

            switch (firstWord)
            {
                case "CM_":
                    ParseComment(
                        raw,
                        startLine,
                        messages,
                        signals,
                        nodes,
                        fileComments,
                        nodeComments,
                        messageComments,
                        signalComments,
                        environmentComments,
                        unresolved);
                    break;
                case "BA_DEF_":
                    ParseAttributeDefinition(raw, startLine, attributeDefinitions);
                    break;
                case "BA_DEF_DEF_":
                    ParseAttributeDefault(raw, startLine, attributeDefaults);
                    break;
                case "BA_":
                    ParseAttributeValue(raw, startLine, messages, signals, nodes, attributeValues, unresolved);
                    break;
                case "VAL_":
                    ParseSignalValues(raw, startLine, messages, signals, signalValueTables, unresolved);
                    break;
                case "VAL_TABLE_":
                    ParseValueTable(raw, startLine, valueTables);
                    break;
                default:
                    if (CommonLookup.Contains(firstWord))
                    {
                        commonStatements.Add(ParseCommon(
                            firstWord.ToString(),
                            raw,
                            startLine,
                            messages,
                            signals,
                            nodes,
                            unresolved));
                    }
                    else if (statement.Kind == DbcStatementKind.Raw
                             && firstWord.Length > 0
                             && !IsHeaderKeyword(firstWord))
                    {
                        opaqueStatements.Add(new DbcOpaqueStatementEntry(
                            firstWord.ToString(),
                            raw,
                            startLine));
                    }

                    break;
            }
        }

        var tableNames = new HashSet<string>(
            valueTables.Select(table => table.Name),
            StringComparer.Ordinal);
        foreach (var signalTable in signalValueTables)
        {
            if (signalTable.ReferencedTable is not null
                && !tableNames.Contains(signalTable.ReferencedTable))
            {
                unresolved.Add(new DbcSemanticReference(
                    "VAL_TABLE_",
                    signalTable.ReferencedTable,
                    signalTable.LineNumber));
            }
        }

        var definitionNames = new HashSet<string>(
            attributeDefinitions.Select(definition => definition.Name),
            StringComparer.Ordinal);
        foreach (var attributeDefault in attributeDefaults)
        {
            if (!definitionNames.Contains(attributeDefault.Name))
            {
                unresolved.Add(new DbcSemanticReference(
                    "BA_DEF_",
                    attributeDefault.Name,
                    attributeDefault.LineNumber));
            }
        }

        foreach (var attributeValue in attributeValues)
        {
            if (!definitionNames.Contains(attributeValue.Name))
            {
                unresolved.Add(new DbcSemanticReference(
                    "BA_DEF_",
                    attributeValue.Name,
                    attributeValue.LineNumber));
            }
        }

        return new DbcSemanticIndex(
            fileComments,
            nodeComments,
            messageComments,
            signalComments,
            environmentComments,
            attributeDefinitions,
            attributeDefaults,
            attributeValues,
            valueTables,
            signalValueTables,
            commonStatements,
            opaqueStatements,
            unresolved);
    }

    private static void ParseComment(
        string raw,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcCommentEntry> fileComments,
        List<DbcCommentEntry> nodeComments,
        List<DbcCommentEntry> messageComments,
        List<DbcCommentEntry> signalComments,
        List<DbcCommentEntry> environmentComments,
        List<DbcSemanticReference> unresolved)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 3;
        SkipWhitespace(span, ref index);
        if (index >= span.Length)
        {
            unresolved.Add(new DbcSemanticReference("CM_", raw, line));
            return;
        }

        if (span[index] == '"')
        {
            if (DbcSyntaxTree.TryParseComment(raw, out var fileText) && fileText is not null)
            {
                fileComments.Add(new DbcCommentEntry(
                    DbcSemanticScope.File,
                    null,
                    null,
                    null,
                    fileText,
                    line));
            }

            return;
        }

        var scopeToken = ReadToken(span, ref index);
        SkipWhitespace(span, ref index);
        switch (scopeToken)
        {
            case "BO_":
                if (!TryReadUInt(span, ref index, out var messageId))
                {
                    unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                    return;
                }

                if (!messages.ContainsKey(messageId))
                {
                    unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                }

                if (DbcSyntaxTree.TryParseComment(raw, out var messageText) && messageText is not null)
                {
                    messageComments.Add(new DbcCommentEntry(
                        DbcSemanticScope.Message,
                        messageId,
                        null,
                        null,
                        messageText,
                        line));
                }

                break;
            case "SG_":
                if (!TryReadUInt(span, ref index, out var signalMessageId))
                {
                    unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                    return;
                }

                SkipWhitespace(span, ref index);
                var signalName = ReadToken(span, ref index).ToString();
                if (signalName.Length == 0
                    || !signals.Contains((signalMessageId, signalName)))
                {
                    unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                }

                if (DbcSyntaxTree.TryParseComment(raw, out var signalText) && signalText is not null)
                {
                    signalComments.Add(new DbcCommentEntry(
                        DbcSemanticScope.Signal,
                        signalMessageId,
                        signalName,
                        null,
                        signalText,
                        line));
                }

                break;
            case "BU_":
                SkipWhitespace(span, ref index);
                var nodeName = ReadToken(span, ref index).ToString();
                if (nodeName.Length == 0 || !nodes.Contains(nodeName))
                {
                    unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                }

                if (DbcSyntaxTree.TryParseComment(raw, out var nodeText) && nodeText is not null)
                {
                    nodeComments.Add(new DbcCommentEntry(
                        DbcSemanticScope.Node,
                        null,
                        null,
                        nodeName,
                        nodeText,
                        line));
                }

                break;
            case "EV_":
                SkipWhitespace(span, ref index);
                var environmentName = ReadToken(span, ref index).ToString();
                if (DbcSyntaxTree.TryParseComment(raw, out var environmentText)
                    && environmentText is not null)
                {
                    environmentComments.Add(new DbcCommentEntry(
                        DbcSemanticScope.EnvironmentVariable,
                        null,
                        null,
                        environmentName,
                        environmentText,
                        line));
                }

                break;
            default:
                unresolved.Add(new DbcSemanticReference("CM_", raw, line));
                break;
        }
    }

    private static void ParseAttributeDefinition(
        string raw,
        int line,
        List<DbcAttributeDefinition> definitions)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 7;
        SkipWhitespace(span, ref index);
        var scope = DbcSemanticScope.File;
        if (index < span.Length && span[index] != '"')
        {
            var scopeToken = ReadToken(span, ref index);
            scope = ScopeForToken(scopeToken);
            SkipWhitespace(span, ref index);
        }

        if (!ReadQuoted(span, ref index, out var name))
        {
            return;
        }

        SkipWhitespace(span, ref index);
        var valueType = ReadToken(span, ref index).ToString();
        var enumValues = new List<string>();
        ReadAttributeConstraints(
            span,
            ref index,
            valueType,
            out var minimum,
            out var maximum,
            enumValues);

        definitions.Add(new DbcAttributeDefinition(
            name,
            scope,
            valueType,
            minimum,
            maximum,
            enumValues,
            line));
    }

    private static void ParseAttributeDefault(
        string raw,
        int line,
        List<DbcAttributeDefault> defaults)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 11;
        SkipWhitespace(span, ref index);
        if (!ReadQuoted(span, ref index, out var name))
        {
            return;
        }

        var value = ReadRemainingValue(span, index);
        defaults.Add(new DbcAttributeDefault(name, value, line));
    }

    private static void ParseAttributeValue(
        string raw,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcAttributeValue> values,
        List<DbcSemanticReference> unresolved)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 3;
        SkipWhitespace(span, ref index);
        if (!ReadQuoted(span, ref index, out var name))
        {
            return;
        }

        SkipWhitespace(span, ref index);
        var scope = DbcSemanticScope.File;
        uint? messageId = null;
        string? signalName = null;
        string? targetName = null;

        if (index < span.Length && span[index] != ';' && span[index] != '"')
        {
            var tokenStart = index;
            var scopeToken = ReadToken(span, ref index);
            var mappedScope = ScopeForToken(scopeToken);
            if (mappedScope == DbcSemanticScope.File)
            {
                index = tokenStart;
            }
            else
            {
                scope = mappedScope;
                SkipWhitespace(span, ref index);
                if (scope is DbcSemanticScope.Message or DbcSemanticScope.Signal)
                {
                    if (!TryReadUInt(span, ref index, out var id))
                    {
                        unresolved.Add(new DbcSemanticReference("BA_", raw, line));
                        return;
                    }

                    messageId = id;
                    if (scope == DbcSemanticScope.Signal)
                    {
                        SkipWhitespace(span, ref index);
                        signalName = ReadToken(span, ref index).ToString();
                        if (!signals.Contains((id, signalName)))
                        {
                            unresolved.Add(new DbcSemanticReference("BA_", raw, line));
                        }
                    }
                    else if (!messages.ContainsKey(id))
                    {
                        unresolved.Add(new DbcSemanticReference("BA_", raw, line));
                    }
                }
                else
                {
                    targetName = ReadToken(span, ref index).ToString();
                    if (scope == DbcSemanticScope.Node && !nodes.Contains(targetName))
                    {
                        unresolved.Add(new DbcSemanticReference("BA_", raw, line));
                    }
                }
            }
        }

        var value = ReadRemainingValue(span, index);
        values.Add(new DbcAttributeValue(
            name,
            scope,
            messageId,
            signalName,
            targetName,
            value,
            line));
    }

    private static void ParseSignalValues(
        string raw,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        List<DbcSignalValueTable> tables,
        List<DbcSemanticReference> unresolved)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 4;
        SkipWhitespace(span, ref index);
        if (!TryReadUInt(span, ref index, out var messageId))
        {
            unresolved.Add(new DbcSemanticReference("VAL_", raw, line));
            return;
        }

        SkipWhitespace(span, ref index);
        var signalName = ReadToken(span, ref index).ToString();
        if (signalName.Length == 0 || !signals.Contains((messageId, signalName)))
        {
            unresolved.Add(new DbcSemanticReference("VAL_", raw, line));
        }

        SkipWhitespace(span, ref index);
        string? referencedTable = null;
        var entries = new List<DbcValueDescription>();
        var next = ReadToken(span, ref index);
        if (next.SequenceEqual("VAL_TABLE_"))
        {
            SkipWhitespace(span, ref index);
            referencedTable = ReadToken(span, ref index).ToString();
        }
        else if (next.Length > 0)
        {
            entries = ReadValuePairs(span, index, next);
        }
        else
        {
            entries = ReadValuePairs(span, index, ReadOnlySpan<char>.Empty);
        }

        tables.Add(new DbcSignalValueTable(
            messageId,
            signalName,
            entries,
            referencedTable,
            line));
    }

    private static void ParseValueTable(
        string raw,
        int line,
        List<DbcValueTableDefinition> tables)
    {
        var span = raw.AsSpan().TrimStart();
        var index = 10;
        SkipWhitespace(span, ref index);
        var name = ReadToken(span, ref index).ToString();
        if (name.Length == 0)
        {
            return;
        }

        SkipWhitespace(span, ref index);
        var first = ReadToken(span, ref index);
        var entries = ReadValuePairs(span, index, first);
        tables.Add(new DbcValueTableDefinition(name, entries, line));
    }

    private static List<DbcValueDescription> ReadValuePairs(
        ReadOnlySpan<char> span,
        int index,
        ReadOnlySpan<char> firstValue)
    {
        var entries = new List<DbcValueDescription>();
        var pending = firstValue;
        while (index < span.Length || pending.Length > 0)
        {
            SkipWhitespace(span, ref index);
            if (pending.Length == 0)
            {
                if (index >= span.Length || span[index] == ';')
                {
                    break;
                }

                var tokenStart = index;
                while (index < span.Length
                       && !char.IsWhiteSpace(span[index])
                       && span[index] is not (';' or ','))
                {
                    index++;
                }

                pending = span[tokenStart..index];
                SkipWhitespace(span, ref index);
            }

            if (pending.Length == 0)
            {
                break;
            }

            if (!ReadQuoted(span, ref index, out var description))
            {
                break;
            }

            entries.Add(new DbcValueDescription(pending.ToString(), description));
            pending = ReadOnlySpan<char>.Empty;
        }

        return entries;
    }

    private static DbcCommonStatement ParseCommon(
        string keyword,
        string raw,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcSemanticReference> unresolved)
    {
        var span = raw.AsSpan().TrimStart();
        var index = keyword.Length;
        SkipWhitespace(span, ref index);

        switch (keyword)
        {
            case "BO_TX_BU_":
                {
                    if (!TryReadUInt(span, ref index, out var messageId))
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    if (!messages.ContainsKey(messageId))
                    {
                        unresolved.Add(new DbcSemanticReference(keyword, raw, line));
                    }

                    SkipWhitespace(span, ref index);
                    if (index < span.Length && span[index] == ':')
                    {
                        index++;
                    }

                    var transmitters = ReadNameList(span, ref index);
                    if (transmitters.Any(transmitter => !nodes.Contains(transmitter)))
                    {
                        unresolved.Add(new DbcSemanticReference(keyword, raw, line));
                    }

                    return new DbcTransmitterStatement(messageId, transmitters, raw, line);
                }

            case "SIG_GROUP_":
                {
                    if (!TryReadUInt(span, ref index, out var messageId))
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    var groupName = ReadToken(span, ref index).ToString();
                    if (groupName.Length == 0)
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    int? repetitions = null;
                    SkipWhitespace(span, ref index);
                    var repetitionsStart = index;
                    while (index < span.Length && char.IsDigit(span[index]))
                    {
                        index++;
                    }

                    if (index > repetitionsStart
                        && int.TryParse(
                            span[repetitionsStart..index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsedRepetitions))
                    {
                        repetitions = parsedRepetitions;
                    }

                    SkipWhitespace(span, ref index);
                    if (index < span.Length && span[index] == ':')
                    {
                        index++;
                    }

                    var signalNames = ReadNameList(span, ref index);
                    if (!messages.ContainsKey(messageId)
                        || signalNames.Any(name => !signals.Contains((messageId, name))))
                    {
                        unresolved.Add(new DbcSemanticReference(keyword, raw, line));
                    }

                    return new DbcSignalGroupStatement(
                        messageId,
                        groupName,
                        repetitions,
                        signalNames,
                        raw,
                        line);
                }

            case "SIG_VALTYPE_":
                {
                    if (!TryReadUInt(span, ref index, out var messageId))
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    var signalName = ReadToken(span, ref index).ToString();
                    if (signalName.Length == 0)
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    SkipWhitespace(span, ref index);
                    if (index < span.Length && span[index] == ':')
                    {
                        index++;
                    }

                    SkipWhitespace(span, ref index);
                    int? valueType = null;
                    var typeStart = index;
                    while (index < span.Length && char.IsDigit(span[index]))
                    {
                        index++;
                    }

                    if (index > typeStart
                        && int.TryParse(
                            span[typeStart..index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsedValueType))
                    {
                        valueType = parsedValueType;
                    }

                    if (!signals.Contains((messageId, signalName)))
                    {
                        unresolved.Add(new DbcSemanticReference(keyword, raw, line));
                    }

                    return new DbcSignalValueTypeStatement(messageId, signalName, valueType, raw, line);
                }

            case "SG_MUL_VAL_":
                {
                    if (!TryReadUInt(span, ref index, out var messageId))
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    var signalName = ReadToken(span, ref index).ToString();
                    var multiplexerSignalName = ReadToken(span, ref index).ToString();
                    if (signalName.Length == 0 || multiplexerSignalName.Length == 0)
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    var ranges = new List<DbcMultiplexRange>();
                    foreach (var token in ReadNameList(span, ref index))
                    {
                        if (TryParseRange(token, out var range))
                        {
                            ranges.Add(range);
                        }
                    }

                    if (!signals.Contains((messageId, signalName))
                        || !signals.Contains((messageId, multiplexerSignalName)))
                    {
                        unresolved.Add(new DbcSemanticReference(keyword, raw, line));
                    }

                    return new DbcExtendedMultiplexStatement(
                        messageId,
                        signalName,
                        multiplexerSignalName,
                        ranges,
                        raw,
                        line);
                }

            case "EV_":
                return ParseEnvironmentVariable(raw, span, index, line);

            case "EV_DATA_":
            case "ENVVAR_DATA_":
                {
                    var name = ReadToken(span, ref index).ToString();
                    if (name.Length == 0)
                    {
                        return new DbcOpaqueCommonStatement(keyword, raw, line);
                    }

                    SkipWhitespace(span, ref index);
                    if (index < span.Length && span[index] == ':')
                    {
                        index++;
                    }

                    SkipWhitespace(span, ref index);
                    uint? dataSize = TryReadUInt(span, ref index, out var size) ? size : null;
                    return new DbcEnvironmentVariableDataStatement(keyword, name, dataSize, raw, line);
                }

            case "SIGTYPE_VALTYPE_":
                return ParseSignalTypeValue(raw, span, index, line);

            case "BA_DEF_SGTYPE_":
                return ParseSignalTypeAttributeDefinition(raw, span, index, line);

            case "BA_SGTYPE_":
                return ParseSignalTypeAttributeValue(raw, span, index, line);

            case "BA_DEF_REL_":
                return ParseRelationAttributeDefinition(raw, span, index, line);

            case "BA_REL_":
                return ParseRelationAttributeValue(
                    raw,
                    span,
                    index,
                    line,
                    messages,
                    signals,
                    nodes,
                    unresolved);

            case "BA_DEF_DEF_REL_":
                return ParseRelationAttributeDefault(raw, span, index, line);

            case "BU_SG_REL_":
            case "BU_EV_REL_":
            case "BU_BO_REL_":
                return ParseRelationStatement(
                    keyword,
                    raw,
                    span,
                    index,
                    line,
                    messages,
                    signals,
                    nodes,
                    unresolved);

            case "CAT_DEF_":
                return ParseCategoryDefinition(raw, span, index, line);

            case "CAT_":
            case "NS_DESC_":
                return new DbcLegacyStatement(keyword, raw, line);

            case "FILTER":
                return ParseFilter(raw, span, index, line);

            case "VECTOR__INDEPENDENT_SIG_MSG":
            case "VECTOR__XXX":
                return new DbcVendorStatement(keyword, raw, line);

            default:
                return new DbcOpaqueCommonStatement(keyword, raw, line);
        }
    }

    private static DbcCommonStatement ParseEnvironmentVariable(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        var name = ReadToken(span, ref index).ToString();
        if (name.Length == 0)
        {
            return new DbcOpaqueCommonStatement("EV_", raw, line);
        }

        SkipWhitespace(span, ref index);
        if (index < span.Length && span[index] == ':')
        {
            index++;
        }

        SkipWhitespace(span, ref index);
        uint? valueType = TryReadUInt(span, ref index, out var type) ? type : null;

        double? minimum = null;
        double? maximum = null;
        SkipWhitespace(span, ref index);
        if (index < span.Length && span[index] == '[')
        {
            index++;
            SkipWhitespace(span, ref index);
            if (TryReadDouble(span, ref index, out var parsedMinimum))
            {
                minimum = parsedMinimum;
            }

            SkipWhitespace(span, ref index);
            if (index < span.Length && span[index] == '|')
            {
                index++;
            }

            SkipWhitespace(span, ref index);
            if (TryReadDouble(span, ref index, out var parsedMaximum))
            {
                maximum = parsedMaximum;
            }

            SkipWhitespace(span, ref index);
            if (index < span.Length && span[index] == ']')
            {
                index++;
            }
        }

        var unit = string.Empty;
        SkipWhitespace(span, ref index);
        if (index < span.Length && span[index] == '"')
        {
            ReadQuoted(span, ref index, out unit);
        }

        SkipWhitespace(span, ref index);
        string initialValue;
        if (index < span.Length && span[index] == '"')
        {
            ReadQuoted(span, ref index, out initialValue);
        }
        else
        {
            initialValue = ReadToken(span, ref index).ToString();
        }

        SkipWhitespace(span, ref index);
        uint? id = TryReadUInt(span, ref index, out var parsedId) ? parsedId : null;

        var accessType = ReadToken(span, ref index).ToString();
        var accessNodes = ReadNameList(span, ref index);

        return new DbcEnvironmentVariableStatement(
            name,
            valueType,
            minimum,
            maximum,
            unit,
            initialValue,
            id,
            accessType,
            accessNodes,
            raw,
            line);
    }

    private static DbcCommonStatement ParseSignalTypeValue(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        var name = ReadToken(span, ref index).ToString();
        if (name.Length == 0)
        {
            return new DbcOpaqueCommonStatement("SIGTYPE_VALTYPE_", raw, line);
        }

        SkipWhitespace(span, ref index);
        if (index >= span.Length || span[index] != ':')
        {
            return new DbcOpaqueCommonStatement("SIGTYPE_VALTYPE_", raw, line);
        }

        index++;
        SkipWhitespace(span, ref index);
        var typeStart = index;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            index++;
        }

        if (index == typeStart
            || !int.TryParse(
                span[typeStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var valueType))
        {
            return new DbcOpaqueCommonStatement("SIGTYPE_VALTYPE_", raw, line);
        }

        return new DbcSignalTypeValueStatement(name, valueType, raw, line);
    }

    private static DbcCommonStatement ParseSignalTypeAttributeDefinition(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        if (!ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("BA_DEF_SGTYPE_", raw, line);
        }

        var valueType = ReadToken(span, ref index).ToString();
        if (valueType.Length == 0)
        {
            return new DbcOpaqueCommonStatement("BA_DEF_SGTYPE_", raw, line);
        }

        var enumValues = new List<string>();
        ReadAttributeConstraints(
            span,
            ref index,
            valueType,
            out var minimum,
            out var maximum,
            enumValues);
        return new DbcSignalTypeAttributeDefinition(
            name,
            valueType,
            minimum,
            maximum,
            enumValues,
            raw,
            line);
    }

    private static DbcCommonStatement ParseSignalTypeAttributeValue(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        if (!ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("BA_SGTYPE_", raw, line);
        }

        var signalTypeName = ReadToken(span, ref index).ToString();
        if (signalTypeName.Length == 0)
        {
            return new DbcOpaqueCommonStatement("BA_SGTYPE_", raw, line);
        }

        var value = ReadRemainingValue(span, index);
        return new DbcSignalTypeAttributeValue(name, signalTypeName, value, raw, line);
    }

    private static DbcCommonStatement ParseRelationAttributeDefinition(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        var relationType = ReadToken(span, ref index).ToString();
        if (relationType.Length == 0 || !ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("BA_DEF_REL_", raw, line);
        }

        var valueType = ReadToken(span, ref index).ToString();
        if (valueType.Length == 0)
        {
            return new DbcOpaqueCommonStatement("BA_DEF_REL_", raw, line);
        }

        var enumValues = new List<string>();
        ReadAttributeConstraints(
            span,
            ref index,
            valueType,
            out var minimum,
            out var maximum,
            enumValues);
        return new DbcRelationAttributeDefinition(
            relationType,
            name,
            valueType,
            minimum,
            maximum,
            enumValues,
            raw,
            line);
    }

    private static DbcCommonStatement ParseRelationAttributeValue(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcSemanticReference> unresolved)
    {
        if (!ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("BA_REL_", raw, line);
        }

        var relationType = ReadToken(span, ref index).ToString();
        if (relationType.Length == 0)
        {
            return new DbcOpaqueCommonStatement("BA_REL_", raw, line);
        }

        string? nodeName = null;
        uint? messageId = null;
        string? signalName = null;
        string? environmentName = null;

        switch (relationType)
        {
            case "BU_BO_REL_":
                nodeName = Optional(ReadToken(span, ref index).ToString());
                SkipWhitespace(span, ref index);
                if (TryReadUInt(span, ref index, out var messageIdentifier))
                {
                    messageId = messageIdentifier;
                }

                break;
            case "BU_SG_REL_":
                nodeName = Optional(ReadToken(span, ref index).ToString());
                TryConsumeKeyword(span, ref index, "SG_");
                SkipWhitespace(span, ref index);
                if (TryReadUInt(span, ref index, out var signalMessageIdentifier))
                {
                    messageId = signalMessageIdentifier;
                }

                signalName = Optional(ReadToken(span, ref index).ToString());
                break;
            case "BU_EV_REL_":
                nodeName = Optional(ReadToken(span, ref index).ToString());
                TryConsumeKeyword(span, ref index, "EV_");
                environmentName = Optional(ReadToken(span, ref index).ToString());
                break;
        }

        var value = ReadRemainingValue(span, index);
        AddMissingReferences(
            "BA_REL_",
            nodeName,
            messageId,
            signalName,
            raw,
            line,
            messages,
            signals,
            nodes,
            unresolved);
        return new DbcRelationAttributeValue(
            name,
            relationType,
            nodeName,
            messageId,
            signalName,
            environmentName,
            value,
            raw,
            line);
    }

    private static DbcCommonStatement ParseRelationAttributeDefault(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        if (!ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("BA_DEF_DEF_REL_", raw, line);
        }

        var value = ReadRemainingValue(span, index);
        return new DbcRelationAttributeDefault(name, value, raw, line);
    }

    private static DbcCommonStatement ParseRelationStatement(
        string keyword,
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcSemanticReference> unresolved)
    {
        var nodeName = Optional(ReadToken(span, ref index).ToString());
        if (nodeName is null)
        {
            return new DbcOpaqueCommonStatement(keyword, raw, line);
        }

        uint? messageId = null;
        string? signalName = null;
        string? environmentName = null;

        switch (keyword)
        {
            case "BU_BO_REL_":
                SkipWhitespace(span, ref index);
                if (!TryReadUInt(span, ref index, out var messageIdentifier))
                {
                    return new DbcOpaqueCommonStatement(keyword, raw, line);
                }

                messageId = messageIdentifier;
                break;
            case "BU_SG_REL_":
                TryConsumeKeyword(span, ref index, "SG_");
                SkipWhitespace(span, ref index);
                if (!TryReadUInt(span, ref index, out var signalMessageIdentifier))
                {
                    return new DbcOpaqueCommonStatement(keyword, raw, line);
                }

                messageId = signalMessageIdentifier;
                signalName = Optional(ReadToken(span, ref index).ToString());
                if (signalName is null)
                {
                    return new DbcOpaqueCommonStatement(keyword, raw, line);
                }

                break;
            case "BU_EV_REL_":
                TryConsumeKeyword(span, ref index, "EV_");
                environmentName = Optional(ReadToken(span, ref index).ToString());
                if (environmentName is null)
                {
                    return new DbcOpaqueCommonStatement(keyword, raw, line);
                }

                break;
        }

        AddMissingReferences(
            keyword,
            nodeName,
            messageId,
            signalName,
            raw,
            line,
            messages,
            signals,
            nodes,
            unresolved);
        return new DbcRelationStatement(
            keyword,
            nodeName,
            messageId,
            signalName,
            environmentName,
            raw,
            line);
    }

    private static DbcCommonStatement ParseCategoryDefinition(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        var objectType = ReadToken(span, ref index).ToString();
        if (objectType.Length == 0 || !ReadQuoted(span, ref index, out var name))
        {
            return new DbcOpaqueCommonStatement("CAT_DEF_", raw, line);
        }

        var valueType = ReadToken(span, ref index).ToString();
        if (valueType.Length == 0)
        {
            return new DbcOpaqueCommonStatement("CAT_DEF_", raw, line);
        }

        return new DbcCategoryDefinitionStatement(objectType, name, valueType, raw, line);
    }

    private static DbcCommonStatement ParseFilter(
        string raw,
        ReadOnlySpan<char> span,
        int index,
        int line)
    {
        uint? messageId = TryReadUInt(span, ref index, out var parsedMessageId)
            ? parsedMessageId
            : null;
        return new DbcFilterStatement(
            messageId,
            Optional(ReadToken(span, ref index).ToString()),
            Optional(ReadToken(span, ref index).ToString()),
            raw,
            line);
    }

    private static void AddMissingReferences(
        string kind,
        string? nodeName,
        uint? messageId,
        string? signalName,
        string raw,
        int line,
        IReadOnlyDictionary<uint, DbcMessage> messages,
        IReadOnlySet<(uint MessageId, string Name)> signals,
        IReadOnlySet<string> nodes,
        List<DbcSemanticReference> unresolved)
    {
        var missing = false;
        if (nodeName is { Length: > 0 } && !nodes.Contains(nodeName))
        {
            missing = true;
        }

        if (messageId is uint id && !messages.ContainsKey(id))
        {
            missing = true;
        }

        if (messageId is uint signalMessageId
            && signalName is { Length: > 0 }
            && !signals.Contains((signalMessageId, signalName)))
        {
            missing = true;
        }

        if (missing)
        {
            unresolved.Add(new DbcSemanticReference(kind, raw, line));
        }
    }

    private static bool TryConsumeKeyword(
        ReadOnlySpan<char> span,
        ref int index,
        ReadOnlySpan<char> keyword)
    {
        SkipWhitespace(span, ref index);
        if (index + keyword.Length > span.Length
            || !span.Slice(index, keyword.Length).SequenceEqual(keyword))
        {
            return false;
        }

        var end = index + keyword.Length;
        if (end < span.Length && !char.IsWhiteSpace(span[end]) && span[end] != ';')
        {
            return false;
        }

        index = end;
        return true;
    }

    private static string? Optional(string value)
    {
        return value.Length == 0 ? null : value;
    }

    private static void ReadAttributeConstraints(
        ReadOnlySpan<char> span,
        ref int index,
        string valueType,
        out double? minimum,
        out double? maximum,
        List<string> enumValues)
    {
        minimum = null;
        maximum = null;
        SkipWhitespace(span, ref index);
        if (string.Equals(valueType, "ENUM", StringComparison.OrdinalIgnoreCase))
        {
            while (index < span.Length)
            {
                while (index < span.Length
                       && (char.IsWhiteSpace(span[index]) || span[index] == ','))
                {
                    index++;
                }

                if (index >= span.Length || span[index] == ';')
                {
                    break;
                }

                if (!ReadQuoted(span, ref index, out var enumValue))
                {
                    break;
                }

                enumValues.Add(enumValue);
            }

            return;
        }

        var first = ReadToken(span, ref index);
        if (first.Length == 0)
        {
            return;
        }

        SkipWhitespace(span, ref index);
        var second = ReadToken(span, ref index);
        if (double.TryParse(
                first,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedMinimum))
        {
            minimum = parsedMinimum;
        }

        if (second.Length > 0
            && double.TryParse(
                second,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedMaximum))
        {
            maximum = parsedMaximum;
        }
    }

    private static List<string> ReadNameList(ReadOnlySpan<char> span, ref int index)
    {
        var names = new List<string>();
        while (true)
        {
            SkipWhitespace(span, ref index);
            if (index >= span.Length || span[index] == ';')
            {
                break;
            }

            if (span[index] == ',')
            {
                index++;
                continue;
            }

            var name = ReadToken(span, ref index).ToString();
            if (name.Length == 0)
            {
                break;
            }

            names.Add(name);
        }

        return names;
    }

    private static bool TryParseRange(string token, out DbcMultiplexRange range)
    {
        range = default;
        var dash = token.IndexOf('-');
        if (dash <= 0 || dash == token.Length - 1)
        {
            return false;
        }

        if (!uint.TryParse(
                token.AsSpan(0, dash),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minimum)
            || !uint.TryParse(
                token.AsSpan(dash + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximum))
        {
            return false;
        }

        range = new DbcMultiplexRange(minimum, maximum);
        return true;
    }

    private static bool TryReadDouble(ReadOnlySpan<char> span, ref int index, out double value)
    {
        value = 0;
        var start = index;
        while (index < span.Length
               && (char.IsDigit(span[index])
                   || span[index] is ('-' or '+' or '.' or 'e' or 'E')))
        {
            index++;
        }

        return index > start
            && double.TryParse(
                span[start..index],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static DbcSemanticScope ScopeForToken(ReadOnlySpan<char> token)
    {
        return token switch
        {
            _ when token.SequenceEqual("BO_") => DbcSemanticScope.Message,
            _ when token.SequenceEqual("SG_") => DbcSemanticScope.Signal,
            _ when token.SequenceEqual("BU_") => DbcSemanticScope.Node,
            _ when token.SequenceEqual("EV_") => DbcSemanticScope.EnvironmentVariable,
            _ => DbcSemanticScope.File,
        };
    }

    private static string ReadRemainingValue(ReadOnlySpan<char> span, int index)
    {
        SkipWhitespace(span, ref index);
        var end = span[index..].IndexOf(';');
        var value = end < 0 ? span[index..] : span[index..(index + end)];
        return value.Trim().ToString();
    }

    private static bool TryReadUInt(ReadOnlySpan<char> span, ref int index, out uint value)
    {
        value = 0;
        var start = index;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            index++;
        }

        return index > start
            && uint.TryParse(
                span[start..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static ReadOnlySpan<char> ReadToken(ReadOnlySpan<char> span, ref int index)
    {
        SkipWhitespace(span, ref index);
        var start = index;
        while (index < span.Length
               && !char.IsWhiteSpace(span[index])
               && span[index] is not (';' or ','))
        {
            index++;
        }

        return span[start..index];
    }

    private static bool ReadQuoted(ReadOnlySpan<char> span, ref int index, out string value)
    {
        value = string.Empty;
        SkipWhitespace(span, ref index);
        if (index >= span.Length || span[index] != '"')
        {
            return false;
        }

        index++;
        var builder = new StringBuilder();
        while (index < span.Length)
        {
            if (span[index] == '\\' && index + 1 < span.Length)
            {
                builder.Append(span[++index]);
                index++;
                continue;
            }

            if (span[index] == '"')
            {
                index++;
                value = builder.ToString();
                return true;
            }

            builder.Append(span[index++]);
        }

        value = builder.ToString();
        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }
    }

    private static ReadOnlySpan<char> FirstWord(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length
               && (char.IsLetterOrDigit(line[index]) || line[index] == '_'))
        {
            index++;
        }

        return line[..index];
    }

    private static bool IsHeaderKeyword(ReadOnlySpan<char> word)
    {
        return word.SequenceEqual("VERSION")
            || word.SequenceEqual("NS_")
            || word.SequenceEqual("BS_")
            || word.SequenceEqual("BU_");
    }

    private static string FirstLine(string raw)
    {
        var newline = raw.IndexOf('\n');
        return newline < 0 ? raw.TrimEnd('\r') : raw[..newline].TrimEnd('\r');
    }

    private static int CountNewlines(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
