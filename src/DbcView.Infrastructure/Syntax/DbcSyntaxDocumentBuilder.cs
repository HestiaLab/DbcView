using System.Globalization;
using System.Text;
using DbcView.Domain;

namespace DbcView.Infrastructure.Syntax;

/// <summary>
/// Projects the lossless syntax tree onto the public <see cref="DbcDocument"/>
/// facade. Message and signal validation is delegated to the legacy field
/// validators until they are replaced, so diagnostics keep their stable codes
/// and line numbers during the P1 transition.
/// </summary>
internal static class DbcSyntaxDocumentBuilder
{
    public static DbcParseResult Build(DbcSyntaxTree tree)
    {
        var diagnostics = new List<DbcDiagnostic>();
        var sections = new List<DbcSection>();
        var header = new StringBuilder();
        var pendingRaw = new StringBuilder();
        var statementSpans = new List<DbcStatementSpan>(tree.StatementCount);
        var version = string.Empty;
        var nodes = Array.Empty<string>();
        var sawVersion = false;
        var sawNodes = false;
        var inHeader = true;
        var line = 0;
        DbcMessageBuilder? current = null;

        void FlushMessage()
        {
            if (current is null)
            {
                return;
            }

            sections.Add(new DbcMessageSection(current.Build()));
            current = null;
        }

        void FlushRaw()
        {
            if (pendingRaw.Length == 0)
            {
                return;
            }

            var text = Normalize(pendingRaw.ToString()).TrimEnd('\n');
            pendingRaw.Clear();
            if (text.Length > 0)
            {
                sections.Add(new DbcRawSection(text));
            }
        }

        for (var index = 0; index < tree.StatementCount; index++)
        {
            var statement = tree.GetStatement(index);
            var raw = statement.RawText;
            var startLine = line + 1;
            line += CountNewlines(raw);
            statementSpans.Add(new DbcStatementSpan(
                statement.Start,
                statement.FullWidth,
                startLine));

            var firstLine = FirstLine(raw).TrimStart('\uFEFF');
            var trimmedFirstLine = firstLine.TrimStart();

            if (inHeader)
            {
                if (DbcParser.IsVersionLine(trimmedFirstLine))
                {
                    if (sawVersion)
                    {
                        diagnostics.Add(new DbcDiagnostic(
                            DbcDiagnosticSeverity.Warning,
                            DbcDiagnosticCodes.DuplicateVersion,
                            "Duplicate VERSION section; the last value is used.",
                            startLine));
                    }

                    if (!DbcParser.HasQuotedVersion(trimmedFirstLine))
                    {
                        diagnostics.Add(new DbcDiagnostic(
                            DbcDiagnosticSeverity.Error,
                            DbcDiagnosticCodes.MalformedVersion,
                            "VERSION section must contain a quoted value, for example VERSION \"\".",
                            startLine));
                    }

                    version = DbcParser.ExtractVersion(trimmedFirstLine);
                    sawVersion = true;
                    continue;
                }

                if (DbcParser.IsNodesLine(trimmedFirstLine))
                {
                    nodes = DbcParser.ParseNodes(trimmedFirstLine);
                    sawNodes = true;
                    inHeader = false;
                    continue;
                }

                if (statement.Kind is DbcStatementKind.Message or DbcStatementKind.Signal)
                {
                    diagnostics.Add(new DbcDiagnostic(
                        DbcDiagnosticSeverity.Error,
                        DbcDiagnosticCodes.MissingNodes,
                        "Missing BU_: node section before the first message.",
                        startLine));
                    inHeader = false;
                }
                else
                {
                    header.Append(raw);
                    continue;
                }
            }

            switch (statement.Kind)
            {
                case DbcStatementKind.Message:
                    FlushMessage();
                    FlushRaw();
                    if (TryParseMessageHeader(
                            trimmedFirstLine,
                            startLine,
                            out var messageHeader,
                            out var headerError))
                    {
                        current = new DbcMessageBuilder(messageHeader);
                    }
                    else if (headerError is not null)
                    {
                        diagnostics.Add(headerError);
                    }

                    break;

                case DbcStatementKind.Signal:
                    if (current is null)
                    {
                        diagnostics.Add(new DbcDiagnostic(
                            DbcDiagnosticSeverity.Error,
                            DbcDiagnosticCodes.SignalWithoutMessage,
                            "SG_ signal line has no enclosing BO_ message.",
                            startLine));
                        break;
                    }

                    if (TryParseSignal(
                            trimmedFirstLine,
                            current.Header.Length,
                            startLine,
                            out var signal,
                            out var signalError,
                            out var signalWarning))
                    {
                        current.Signals.Add(signal!);
                        if (signalError is not null)
                        {
                            diagnostics.Add(signalError);
                        }

                        if (signalWarning is not null)
                        {
                            diagnostics.Add(signalWarning);
                        }
                    }
                    else if (signalError is not null)
                    {
                        diagnostics.Add(signalError);
                    }

                    break;

                default:
                    pendingRaw.Append(raw);
                    break;
            }
        }

        FlushMessage();
        FlushRaw();

        if (!sawVersion)
        {
            diagnostics.Add(new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.MissingVersion,
                "Missing VERSION section: the content does not look like a DBC database.",
                0));
        }

        if (!sawNodes)
        {
            diagnostics.Add(new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.MissingNodes,
                "Missing BU_: node section: the content does not look like a DBC database.",
                0));
        }

        var document = new DbcDocument(
            version,
            nodes,
            Normalize(header.ToString()).TrimEnd('\n'),
            sections);
        return new DbcParseResult(
            document,
            diagnostics,
            () => DbcSemanticIndexBuilder.Build(tree, document),
            statementSpans);
    }

    private static bool TryParseMessageHeader(
        string line,
        int lineNumber,
        out DbcParser.MessageHeader header,
        out DbcDiagnostic? diagnostic)
    {
        header = default;
        diagnostic = null;
        var span = line.AsSpan().TrimStart();
        if (!span.StartsWith("BO_"))
        {
            diagnostic = MalformedMessage(lineNumber);
            return false;
        }

        var index = 3;
        SkipWhitespace(span, ref index);
        var idStart = index;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            index++;
        }

        if (index == idStart)
        {
            diagnostic = MalformedMessage(lineNumber);
            return false;
        }

        var idText = span[idStart..index].ToString();
        if (!uint.TryParse(
                span[idStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id))
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.InvalidNumber,
                $"Message identifier '{idText}' is not a valid unsigned 32-bit number.",
                lineNumber);
            return false;
        }

        SkipWhitespace(span, ref index);
        var nameStart = index;
        while (index < span.Length
               && !char.IsWhiteSpace(span[index])
               && span[index] != ':')
        {
            index++;
        }

        if (index == nameStart)
        {
            diagnostic = MalformedMessage(lineNumber);
            return false;
        }

        var name = span[nameStart..index].ToString();
        SkipWhitespace(span, ref index);
        if (index >= span.Length || span[index] != ':')
        {
            diagnostic = MalformedMessage(lineNumber);
            return false;
        }

        index++;
        SkipWhitespace(span, ref index);
        var lengthStart = index;
        while (index < span.Length && char.IsDigit(span[index]))
        {
            index++;
        }

        if (index == lengthStart)
        {
            diagnostic = MalformedMessage(lineNumber);
            return false;
        }

        var lengthText = span[lengthStart..index].ToString();
        if (!byte.TryParse(
                span[lengthStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length))
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.InvalidMessageLength,
                $"Message length '{lengthText}' must be between 0 and 255 bytes.",
                lineNumber);
            return false;
        }

        SkipWhitespace(span, ref index);
        var transmitter = string.Empty;
        if (index < span.Length)
        {
            var transmitterStart = index;
            while (index < span.Length && !char.IsWhiteSpace(span[index]))
            {
                index++;
            }

            transmitter = span[transmitterStart..index].ToString();
            SkipWhitespace(span, ref index);
            if (index < span.Length)
            {
                diagnostic = MalformedMessage(lineNumber);
                return false;
            }
        }

        header = new DbcParser.MessageHeader(id, name, length, transmitter);
        return true;
    }

    private static bool TryParseSignal(
        string line,
        byte messageLength,
        int lineNumber,
        out DbcSignal? signal,
        out DbcDiagnostic? diagnostic,
        out DbcDiagnostic? warning)
    {
        signal = null;
        diagnostic = null;
        warning = null;
        var span = line.AsSpan().TrimStart();
        if (!span.StartsWith("SG_"))
        {
            diagnostic = MalformedSignal(lineNumber);
            return false;
        }

        var index = 3;
        SkipWhitespace(span, ref index);
        var nameStart = index;
        while (index < span.Length
               && !char.IsWhiteSpace(span[index])
               && span[index] != ':')
        {
            index++;
        }

        if (index == nameStart)
        {
            diagnostic = MalformedSignal(lineNumber);
            return false;
        }

        var name = span[nameStart..index].ToString();
        SkipWhitespace(span, ref index);
        var multiplexer = (string?)null;
        if (index < span.Length && span[index] != ':')
        {
            var muxStart = index;
            while (index < span.Length
                   && !char.IsWhiteSpace(span[index])
                   && span[index] != ':')
            {
                index++;
            }

            var mux = span[muxStart..index];
            if (!IsMultiplexerToken(mux))
            {
                diagnostic = MalformedSignal(lineNumber);
                return false;
            }

            multiplexer = mux.ToString();
            SkipWhitespace(span, ref index);
        }

        if (index >= span.Length || span[index] != ':')
        {
            diagnostic = MalformedSignal(lineNumber);
            return false;
        }

        index++;
        SkipWhitespace(span, ref index);
        var layoutStart = index;
        while (index < span.Length && !char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index == layoutStart)
        {
            diagnostic = MalformedSignal(lineNumber);
            return false;
        }

        if (!TryParseSignalLayout(
                span[layoutStart..index],
                out var startBit,
                out var length,
                out var isBigEndian,
                out var isSigned,
                out var layoutMalformed,
                out var numberMalformed))
        {
            diagnostic = numberMalformed
                ? new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.InvalidNumber,
                    "Signal start bit or length is outside the supported integer range.",
                    lineNumber)
                : layoutMalformed
                    ? new DbcDiagnostic(
                        DbcDiagnosticSeverity.Error,
                        DbcDiagnosticCodes.MalformedSignal,
                        "Malformed signal layout; expected '<start bit>|<length>@<0|1><+|->'.",
                        lineNumber)
                    : MalformedSignal(lineNumber);
            return false;
        }

        var rest = span[index..].TrimStart();
        var factor = 0.0;
        var offset = 0.0;
        var min = 0.0;
        var max = 0.0;
        var unit = string.Empty;

        if (rest.StartsWith("("))
        {
            var close = rest.IndexOf(')');
            if (close < 0)
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.MalformedSignal,
                    "Signal factor/offset block is missing its closing ')'.",
                    lineNumber);
                return false;
            }

            var inner = rest[1..close];
            var comma = inner.IndexOf(',');
            if (comma < 0
                || !double.TryParse(
                    inner[..comma],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out factor)
                || !double.TryParse(
                    inner[(comma + 1)..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out offset))
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.InvalidNumber,
                    $"Signal factor/offset '({inner.ToString()})' contains an invalid number.",
                    lineNumber);
                return false;
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith("["))
        {
            var close = rest.IndexOf(']');
            if (close < 0)
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.MalformedSignal,
                    "Signal range block is missing its closing ']'.",
                    lineNumber);
                return false;
            }

            var inner = rest[1..close];
            var separator = inner.IndexOf('|');
            if (separator < 0
                || !double.TryParse(
                    inner[..separator],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out min)
                || !double.TryParse(
                    inner[(separator + 1)..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out max))
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.InvalidNumber,
                    $"Signal range '[{inner.ToString()}]' contains an invalid number.",
                    lineNumber);
                return false;
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith("\""))
        {
            var unitEnd = rest[1..].IndexOf('"');
            if (unitEnd < 0)
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.MalformedSignal,
                    "Signal unit string is missing its closing '\"'.",
                    lineNumber);
                return false;
            }

            unitEnd += 1;
            unit = rest[1..unitEnd].ToString();
            rest = rest[(unitEnd + 1)..].TrimStart();
        }

        var receiver = rest.ToString();
        var rangeDiagnostic = CheckBitRange(
            name,
            startBit,
            length,
            isBigEndian,
            messageLength,
            lineNumber,
            out var clearlyInvalid);
        if (rangeDiagnostic is not null)
        {
            if (clearlyInvalid)
            {
                diagnostic = rangeDiagnostic;
            }
            else
            {
                warning = rangeDiagnostic;
            }
        }

        signal = new DbcSignal(
            name,
            startBit,
            length,
            isBigEndian,
            isSigned,
            factor,
            offset,
            min,
            max,
            unit,
            receiver,
            multiplexer);
        return true;
    }

    private static bool TryParseSignalLayout(
        ReadOnlySpan<char> layout,
        out int startBit,
        out int length,
        out bool isBigEndian,
        out bool isSigned,
        out bool malformed,
        out bool numberMalformed)
    {
        startBit = 0;
        length = 0;
        isBigEndian = false;
        isSigned = false;
        malformed = false;
        numberMalformed = false;

        var index = 0;
        var startStart = index;
        while (index < layout.Length && char.IsDigit(layout[index]))
        {
            index++;
        }

        if (index == startStart || index >= layout.Length || layout[index] != '|')
        {
            malformed = true;
            return false;
        }

        var startText = layout[startStart..index];
        index++;
        var lengthStart = index;
        while (index < layout.Length && char.IsDigit(layout[index]))
        {
            index++;
        }

        if (index == lengthStart || index >= layout.Length || layout[index] != '@')
        {
            malformed = true;
            return false;
        }

        var lengthText = layout[lengthStart..index];
        index++;
        if (index >= layout.Length || layout[index] is not ('0' or '1'))
        {
            malformed = true;
            return false;
        }

        isBigEndian = layout[index] == '1';
        index++;
        if (index >= layout.Length || layout[index] is not ('+' or '-'))
        {
            malformed = true;
            return false;
        }

        isSigned = layout[index] == '-';
        index++;
        if (index != layout.Length)
        {
            malformed = true;
            return false;
        }

        if (!int.TryParse(
                startText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out startBit)
            || !int.TryParse(
                lengthText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out length))
        {
            numberMalformed = true;
            return false;
        }

        return true;
    }

    private static DbcDiagnostic? CheckBitRange(
        string name,
        int startBit,
        int length,
        bool isBigEndian,
        byte messageLength,
        int lineNumber,
        out bool clearlyInvalid)
    {
        clearlyInvalid = false;
        if (DbcParser.IsBitRangeValid(
                startBit,
                length,
                isBigEndian,
                messageLength,
                out var rangeMessage))
        {
            return null;
        }

        clearlyInvalid = DbcParser.IsClearlyInvalidBitRange(
            startBit,
            length,
            isBigEndian,
            messageLength);
        return new DbcDiagnostic(
            clearlyInvalid ? DbcDiagnosticSeverity.Error : DbcDiagnosticSeverity.Warning,
            DbcDiagnosticCodes.InvalidBitRange,
            $"Signal '{name}' {rangeMessage}",
            lineNumber);
    }

    private static bool IsMultiplexerToken(ReadOnlySpan<char> token)
    {
        if (token.Length == 1 && token[0] == 'M')
        {
            return true;
        }

        if (token.Length < 2 || token[0] != 'm')
        {
            return false;
        }

        for (var index = 1; index < token.Length; index++)
        {
            if (!char.IsDigit(token[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }
    }

    private static DbcDiagnostic MalformedMessage(int lineNumber)
    {
        return new DbcDiagnostic(
            DbcDiagnosticSeverity.Error,
            DbcDiagnosticCodes.MalformedMessageHeader,
            "Malformed BO_ message header; expected 'BO_ <id> <name>: <length> <transmitter>'.",
            lineNumber);
    }

    private static DbcDiagnostic MalformedSignal(int lineNumber)
    {
        return new DbcDiagnostic(
            DbcDiagnosticSeverity.Error,
            DbcDiagnosticCodes.MalformedSignal,
            "Malformed SG_ signal line; expected 'SG_ <name> : <start>|<length>@<order><sign> ...'.",
            lineNumber);
    }

    private static string FirstLine(string raw)
    {
        var newline = raw.IndexOf('\n');
        if (newline < 0)
        {
            return raw.TrimEnd('\r');
        }

        return raw[..newline].TrimEnd('\r');
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

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private sealed class DbcMessageBuilder
    {
        public DbcMessageBuilder(DbcParser.MessageHeader header)
        {
            Header = header;
        }

        public DbcParser.MessageHeader Header { get; }

        public List<DbcSignal> Signals { get; } = new();

        public DbcMessage Build()
        {
            return new DbcMessage(
                Header.Id,
                Header.Name,
                Header.Length,
                Header.Transmitter,
                Signals.ToArray());
        }
    }
}
