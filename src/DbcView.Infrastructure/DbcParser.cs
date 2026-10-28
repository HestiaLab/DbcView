using System.Globalization;
using System.Text.RegularExpressions;
using DbcView.Domain;
using DbcView.Infrastructure.Syntax;

namespace DbcView.Infrastructure;

/// <summary>
/// Hand-written DBC (CAN database) parser and serializer with round-trip
/// preservation. Parsing tolerates the whitespace variations found in the
/// wild (single- or double-space <c>SG_</c> indent, CRLF or LF line endings,
/// optional blank lines) and reports structural problems as diagnostics
/// instead of throwing. Serialization emits the canonical DBC layout
/// (two-space <c>SG_</c> indent, blank line between messages) while keeping
/// raw comment/property sections in their original relative position.
/// </summary>
public static class DbcParser
{
    private static readonly Regex MessageHeaderPattern = new(
        @"^BO_\s+(?<id>\d+)\s+(?<name>\S+)\s*:\s*(?<length>\d+)(?:\s+(?<transmitter>\S+))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SignalPattern = new(
        @"^SG_\s+(?<name>\S+)(?:\s+(?<mux>M|m\d+))?\s*:\s*(?<layout>\S+)\s*(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SignalLayoutPattern = new(
        @"^(?<start>\d+)\|(?<length>\d+)@(?<order>[01])(?<sign>[+-])$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DbcParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return DbcSyntaxDocumentBuilder.Build(DbcSyntaxTree.Parse(content));
    }

    internal static DbcParseResult ParseLegacy(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var diagnostics = new List<DbcDiagnostic>();
        var lines = SplitLines(content);
        var nodes = Array.Empty<string>();
        var headerSections = new List<string>();
        var sections = new List<DbcSection>();
        var pendingRaw = new List<string>();
        DbcMessageBuilder? current = null;
        var inHeader = true;
        var sawVersion = false;
        var sawNodes = false;
        var version = string.Empty;

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
            if (pendingRaw.Count == 0)
            {
                return;
            }

            sections.Add(new DbcRawSection(string.Join("\n", pendingRaw)));
            pendingRaw.Clear();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (inHeader)
            {
                var trimmed = line.TrimStart();
                if (IsVersionLine(trimmed))
                {
                    if (sawVersion)
                    {
                        diagnostics.Add(new DbcDiagnostic(
                            DbcDiagnosticSeverity.Warning,
                            DbcDiagnosticCodes.DuplicateVersion,
                            "Duplicate VERSION section; the last value is used.",
                            lineNumber));
                    }

                    if (!HasQuotedVersion(trimmed))
                    {
                        diagnostics.Add(new DbcDiagnostic(
                            DbcDiagnosticSeverity.Error,
                            DbcDiagnosticCodes.MalformedVersion,
                            "VERSION section must contain a quoted value, for example VERSION \"\".",
                            lineNumber));
                    }

                    version = ExtractVersion(trimmed);
                    sawVersion = true;
                    continue;
                }

                if (IsNodesLine(trimmed))
                {
                    nodes = ParseNodes(trimmed);
                    sawNodes = true;
                    inHeader = false;
                    continue;
                }

                if (IsMessageLine(trimmed) || IsSignalLine(trimmed))
                {
                    // A message cannot precede the BU_ node section; report the
                    // missing header and keep parsing so the user sees all errors.
                    diagnostics.Add(new DbcDiagnostic(
                        DbcDiagnosticSeverity.Error,
                        DbcDiagnosticCodes.MissingNodes,
                        "Missing BU_: node section before the first message.",
                        lineNumber));
                    inHeader = false;
                }
                else
                {
                    headerSections.Add(line);
                    continue;
                }
            }

            var body = line.TrimStart();
            if (IsMessageLine(body))
            {
                FlushMessage();
                FlushRaw();
                if (TryParseMessageHeader(body, lineNumber, out var header, out var headerError))
                {
                    current = new DbcMessageBuilder(header);
                }
                else
                {
                    diagnostics.Add(headerError!);
                }
            }
            else if (IsSignalLine(body))
            {
                if (current is null)
                {
                    diagnostics.Add(new DbcDiagnostic(
                        DbcDiagnosticSeverity.Error,
                        DbcDiagnosticCodes.SignalWithoutMessage,
                        "SG_ signal line has no enclosing BO_ message.",
                        lineNumber));
                    continue;
                }

                if (TryParseSignal(
                        body,
                        current.Header.Length,
                        lineNumber,
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
                else
                {
                    diagnostics.Add(signalError!);
                }
            }
            else
            {
                pendingRaw.Add(line);
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
            string.Join("\n", headerSections),
            sections);
        return new DbcParseResult(document, diagnostics);
    }

    public static string Serialize(DbcDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var writer = new List<string>
        {
            $"VERSION \"{doc.Version}\"",
        };

        if (doc.HeaderSections.Length > 0)
        {
            writer.Add(doc.HeaderSections);
        }

        writer.Add($"BU_: {string.Join(' ', doc.Nodes)}");

        DbcSection? previous = null;
        foreach (var section in doc.Sections)
        {
            switch (section)
            {
                case DbcRawSection raw:
                    writer.Add(raw.Text);
                    break;

                case DbcMessageSection messageSection:
                    if (previous is DbcMessageSection)
                    {
                        writer.Add(string.Empty);
                    }

                    writer.Add(SerializeMessage(messageSection.Message));
                    break;
            }

            previous = section;
        }

        return string.Join("\n", writer);
    }

    private static string SerializeMessage(DbcMessage message)
    {
        var lines = new List<string>
        {
            $"BO_ {message.Id} {message.Name}: {message.Length} {message.Transmitter}",
        };
        lines.AddRange(message.Signals.Select(signal => "  " + SerializeSignal(signal)));
        return string.Join("\n", lines);
    }

    private static string[] SplitLines(string content)
    {
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        // Normalize CRLF/CR to LF, drop a single trailing empty entry so a
        // file ending with a newline does not produce a phantom blank line.
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    internal static bool IsVersionLine(string line)
    {
        return line.StartsWith("VERSION", StringComparison.Ordinal)
            && (line.Length == "VERSION".Length || char.IsWhiteSpace(line["VERSION".Length]));
    }

    // The NS_ section lists parser keywords such as BO_TX_BU_ and SG_MUL_VAL_;
    // require whitespace after the token so those header entries are not
    // mistaken for actual BO_/SG_ definitions.
    private static bool IsMessageLine(string line)
    {
        return line.StartsWith("BO_ ", StringComparison.Ordinal)
            || line.StartsWith("BO_\t", StringComparison.Ordinal);
    }

    private static bool IsSignalLine(string line)
    {
        return line.StartsWith("SG_ ", StringComparison.Ordinal)
            || line.StartsWith("SG_\t", StringComparison.Ordinal);
    }

    internal static bool HasQuotedVersion(string line)
    {
        var firstQuote = line.IndexOf('"');
        return firstQuote >= 0 && line.IndexOf('"', firstQuote + 1) >= 0;
    }

    internal static string ExtractVersion(string line)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
        {
            return string.Empty;
        }

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0)
        {
            return line[(firstQuote + 1)..];
        }

        return line[(firstQuote + 1)..secondQuote];
    }

    internal static bool IsNodesLine(string line)
    {
        if (!line.StartsWith("BU_", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = line["BU_".Length..].TrimStart();
        return rest.StartsWith(":", StringComparison.Ordinal);
    }

    internal static string[] ParseNodes(string line)
    {
        var colon = line.IndexOf(':');
        var rest = colon < 0 ? string.Empty : line[(colon + 1)..].Trim();
        return rest.Length == 0
            ? Array.Empty<string>()
            : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    internal static bool TryParseMessageHeader(
        string line,
        int lineNumber,
        out MessageHeader header,
        out DbcDiagnostic? diagnostic)
    {
        header = default;
        diagnostic = null;

        var match = MessageHeaderPattern.Match(line);
        if (!match.Success)
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.MalformedMessageHeader,
                "Malformed BO_ message header; expected 'BO_ <id> <name>: <length> <transmitter>'.",
                lineNumber);
            return false;
        }

        if (!uint.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.InvalidNumber,
                $"Message identifier '{match.Groups["id"].Value}' is not a valid unsigned 32-bit number.",
                lineNumber);
            return false;
        }

        if (!byte.TryParse(match.Groups["length"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.InvalidMessageLength,
                $"Message length '{match.Groups["length"].Value}' must be between 0 and 255 bytes.",
                lineNumber);
            return false;
        }

        header = new MessageHeader(
            id,
            match.Groups["name"].Value,
            length,
            match.Groups["transmitter"].Value);
        return true;
    }

    internal static bool TryParseSignal(
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

        var match = SignalPattern.Match(line);
        if (!match.Success)
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.MalformedSignal,
                "Malformed SG_ signal line; expected 'SG_ <name> : <start>|<length>@<order><sign> ...'.",
                lineNumber);
            return false;
        }

        var layoutMatch = SignalLayoutPattern.Match(match.Groups["layout"].Value);
        if (!layoutMatch.Success)
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.MalformedSignal,
                "Malformed signal layout; expected '<start bit>|<length>@<0|1><+|->'.",
                lineNumber);
            return false;
        }

        if (!int.TryParse(
                layoutMatch.Groups["start"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var startBit)
            || !int.TryParse(
                layoutMatch.Groups["length"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length))
        {
            diagnostic = new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                DbcDiagnosticCodes.InvalidNumber,
                "Signal start bit or length is outside the supported integer range.",
                lineNumber);
            return false;
        }

        var isBigEndian = layoutMatch.Groups["order"].Value == "1";
        var isSigned = layoutMatch.Groups["sign"].Value == "-";
        var rest = match.Groups["rest"].Value.TrimStart();
        var factor = 0.0;
        var offset = 0.0;
        var min = 0.0;
        var max = 0.0;
        var unit = string.Empty;

        if (rest.StartsWith("(", StringComparison.Ordinal))
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
                    $"Signal factor/offset '({inner})' contains an invalid number.",
                    lineNumber);
                return false;
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith("[", StringComparison.Ordinal))
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
                    $"Signal range '[{inner}]' contains an invalid number.",
                    lineNumber);
                return false;
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith("\"", StringComparison.Ordinal))
        {
            var unitEnd = rest.IndexOf('"', 1);
            if (unitEnd < 0)
            {
                diagnostic = new DbcDiagnostic(
                    DbcDiagnosticSeverity.Error,
                    DbcDiagnosticCodes.MalformedSignal,
                    "Signal unit string is missing its closing '\"'.",
                    lineNumber);
                return false;
            }

            unit = rest[1..unitEnd];
            rest = rest[(unitEnd + 1)..].TrimStart();
        }

        if (!IsBitRangeValid(startBit, length, isBigEndian, messageLength, out var rangeMessage))
        {
            // Real-world DBC files (including the bundled BMW sample) contain
            // Motorola or slightly overflowing signals that only fit under a
            // byte-reversed interpretation. Keep those as warnings, but treat
            // definitions that cannot fit under either convention as errors so
            // obviously invalid content is not presented as a trustworthy DBC.
            var clearlyInvalid = IsClearlyInvalidBitRange(
                startBit,
                length,
                isBigEndian,
                messageLength);
            var rangeDiagnostic = new DbcDiagnostic(
                clearlyInvalid ? DbcDiagnosticSeverity.Error : DbcDiagnosticSeverity.Warning,
                DbcDiagnosticCodes.InvalidBitRange,
                $"Signal '{match.Groups["name"].Value}' {rangeMessage}",
                lineNumber);
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
            match.Groups["name"].Value,
            startBit,
            length,
            isBigEndian,
            isSigned,
            factor,
            offset,
            min,
            max,
            unit,
            rest,
            match.Groups["mux"].Success ? match.Groups["mux"].Value : null);
        return true;
    }

    internal static bool IsBitRangeValid(
        int startBit,
        int length,
        bool isBigEndian,
        byte messageLength,
        out string message)
    {
        message = string.Empty;
        if (length < 1 || length > 64)
        {
            message = $"has length {length}; DBC signals must be between 1 and 64 bits.";
            return false;
        }

        var totalBits = messageLength * 8;
        if (totalBits == 0)
        {
            message = "cannot fit into a zero-length message.";
            return false;
        }

        if (startBit >= totalBits)
        {
            message = $"starts at bit {startBit}, outside the {totalBits}-bit message.";
            return false;
        }

        if (!isBigEndian)
        {
            if (startBit + length > totalBits)
            {
                message =
                    $"spans bits {startBit}..{startBit + length - 1}, outside the {totalBits}-bit message.";
                return false;
            }

            return true;
        }

        // Some DBC producers use a byte-reversed Motorola convention where the
        // start bit is the most significant bit of its byte. Accept either
        // interpretation; only ranges that fit neither are suspicious.
        if (startBit + length <= totalBits)
        {
            return true;
        }

        // Classic sawtooth numbering walks down a byte and then jumps to the
        // most significant bit of the next byte: 7..0, 15..8, 23..16, …
        var position = startBit;
        for (var i = 0; i < length; i++)
        {
            if (position >= totalBits)
            {
                message =
                    $"spans big-endian bit {position}, outside the {totalBits}-bit message.";
                return false;
            }

            position = position % 8 == 0 ? position + 15 : position - 1;
        }

        return true;
    }

    internal static bool IsClearlyInvalidBitRange(
        int startBit,
        int length,
        bool isBigEndian,
        byte messageLength)
    {
        var totalBits = messageLength * 8;
        if (length < 1 || length > 64 || totalBits == 0 || startBit >= totalBits)
        {
            return true;
        }

        if (!isBigEndian)
        {
            return startBit + length > totalBits + 8;
        }

        if (startBit + length <= totalBits + 8)
        {
            return false;
        }

        var position = startBit;
        for (var i = 0; i < length; i++)
        {
            if (position >= totalBits)
            {
                return true;
            }

            position = position % 8 == 0 ? position + 15 : position - 1;
        }

        return false;
    }

    private static string SerializeSignal(DbcSignal signal)
    {
        var byteOrder = signal.IsBigEndian ? "1" : "0";
        var sign = signal.IsSigned ? "-" : "+";
        var layout = $"{signal.StartBit}|{signal.Length}@{byteOrder}{sign}";
        var factorOffset =
            $"({FormatNumber(signal.Factor)},{FormatNumber(signal.Offset)})";
        var range =
            $"[{FormatNumber(signal.Min)}|{FormatNumber(signal.Max)}]";
        var name = signal.Multiplexer is null
            ? signal.Name
            : $"{signal.Name} {signal.Multiplexer}";
        return
            $"SG_ {name} : {layout} {factorOffset} {range} \"{signal.Unit}\" {signal.Receiver}";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    internal readonly record struct MessageHeader(
        uint Id,
        string Name,
        byte Length,
        string Transmitter);

    private sealed class DbcMessageBuilder
    {
        public DbcMessageBuilder(MessageHeader header)
        {
            Header = header;
        }

        public MessageHeader Header { get; }

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
