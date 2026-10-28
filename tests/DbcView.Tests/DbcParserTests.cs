#nullable enable
using DbcView.Domain;
using DbcView.Infrastructure;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcParserTests
{
    private const string SampleFragment =
        "VERSION \"\"\n" +
        "\n" +
        "NS_ :\n" +
        "    NS_DESC_\n" +
        "    CM_\n" +
        "    BA_DEF_\n" +
        "    BA_\n" +
        "    VAL_\n" +
        "\n" +
        "BS_:\n" +
        "\n" +
        "BU_: DME SZL XXX\n" +
        "\n" +
        "BO_ 170 AccPedal: 8 DME\n" +
        " SG_ KickDownPressed : 53|1@0+ (1,0) [0|3] \"\" XXX\n" +
        " SG_ EngineSpeed : 32|16@1- (1,0) [0|65535] \"\" XXX\n" +
        " SG_ AccelPct : 16|16@1+ (0.01,0) [0|100] \"%\" Vector__XXX\n" +
        "\n" +
        "BO_ 404 CruiseControl: 4 SZL\n" +
        " SG_ CruiseActive : 0|1@0+ (1,0) [0|1] \"\" XXX\n";

    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "bmw_e9x_e8x.dbc");

    [Fact]
    public void Parse_reads_messages_and_signals_from_fragment()
    {
        var result = DbcParser.Parse(SampleFragment);
        var doc = result.Document;

        Assert.True(result.IsValid);
        Assert.Equal(2, doc.Messages.Count);

        var pedal = doc.Messages[0];
        Assert.Equal(170u, pedal.Id);
        Assert.Equal("AccPedal", pedal.Name);
        Assert.Equal(8, pedal.Length);
        Assert.Equal("DME", pedal.Transmitter);
        Assert.Equal(3, pedal.Signals.Count);

        var engineSpeed = pedal.Signals[1];
        Assert.Equal("EngineSpeed", engineSpeed.Name);
        Assert.Equal(32, engineSpeed.StartBit);
        Assert.Equal(16, engineSpeed.Length);
        Assert.True(engineSpeed.IsBigEndian);
        Assert.Equal(1.0, engineSpeed.Factor);
        Assert.Equal(0.0, engineSpeed.Offset);
        Assert.Equal(0.0, engineSpeed.Min);
        Assert.Equal(65535.0, engineSpeed.Max);
        Assert.Equal("", engineSpeed.Unit);
        Assert.Equal("XXX", engineSpeed.Receiver);

        var accelPct = pedal.Signals[2];
        Assert.True(accelPct.IsBigEndian);
        Assert.False(accelPct.IsSigned);
        Assert.Equal(0.01, accelPct.Factor);
        Assert.Equal("%", accelPct.Unit);
        Assert.Equal("Vector__XXX", accelPct.Receiver);

        var cruise = doc.Messages[1];
        Assert.Equal(404u, cruise.Id);
        Assert.Equal("CruiseControl", cruise.Name);
        Assert.Equal(4, cruise.Length);
        Assert.Equal("SZL", cruise.Transmitter);
        Assert.Single(cruise.Signals);
    }

    [Fact]
    public void Parse_then_Serialize_round_trips_acceptance_sample()
    {
        var original = File.ReadAllText(SamplePath);

        var result = DbcParser.Parse(original);
        var serialized = DbcParser.Serialize(result.Document);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.False(result.HasErrors);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(DbcDiagnosticSeverity.Warning, diagnostic.Severity));
        Assert.Equal(325, result.Document.Messages.Count);
        Assert.Equal(38, CountLines(original, "CM_"));
        Assert.Equal(3, CountLines(original, "VAL_"));
        Assert.Contains("Vector__XXX", result.Document.Nodes);
        Assert.Equal(NormalizeLines(original), NormalizeLines(serialized));
    }

    [Fact]
    public void Serialize_emits_canonical_message_and_signal_layout()
    {
        var doc = DbcParser.Parse(SampleFragment).Document;
        var serialized = DbcParser.Serialize(doc);

        var lines = serialized.Split('\n');
        var bo170 = Array.FindIndex(lines, l => l.StartsWith("BO_ 170", StringComparison.Ordinal));
        var bo404 = Array.FindIndex(lines, l => l.StartsWith("BO_ 404", StringComparison.Ordinal));

        Assert.True(bo170 >= 0);
        Assert.True(bo404 >= 0);

        // Canonical layout: BO_ line immediately followed by two-space-indented
        // SG_ lines, and a blank line between messages.
        Assert.StartsWith("  SG_ ", lines[bo170 + 1], StringComparison.Ordinal);
        Assert.Contains("  SG_ ", serialized, StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[bo404 - 1]);
    }

    [Fact]
    public void Parse_accepts_valid_empty_documents()
    {
        var empty = DbcParser.Parse("VERSION \"\"\nBU_: XXX\n");
        Assert.True(empty.IsValid);
        Assert.Empty(empty.Diagnostics);
        Assert.Empty(empty.Document.Messages);
        Assert.Single(empty.Document.Nodes);

        var noSignals = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n");
        Assert.True(noSignals.IsValid);
        Assert.Single(noSignals.Document.Messages);
        Assert.Empty(noSignals.Document.Messages[0].Signals);
    }

    [Fact]
    public void Parse_rejects_arbitrary_text_with_stable_diagnostics()
    {
        var result = DbcParser.Parse("this is not a DBC file");

        Assert.True(result.HasErrors);
        Assert.False(result.IsValid);
        Assert.Empty(result.Document.Messages);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.MissingVersion);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.MissingNodes);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(DbcDiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void Parse_reports_malformed_message_header()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ not-a-number Broken: 8 XXX\n");

        Assert.True(result.HasErrors);
        Assert.Empty(result.Document.Messages);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.MalformedMessageHeader
                && diagnostic.LineNumber == 4);
    }

    [Fact]
    public void Parse_reports_invalid_signal_numbers()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ Bad : 0|1@0+ (abc,0) [0|1] \"\" XXX\n");

        Assert.True(result.HasErrors);
        Assert.Single(result.Document.Messages);
        Assert.Empty(result.Document.Messages[0].Signals);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.InvalidNumber
                && diagnostic.LineNumber == 5);
    }

    [Fact]
    public void Parse_reports_out_of_range_little_endian_bits_as_warning()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ TooWide : 7|2@0+ (1,0) [0|3] \"\" XXX\n");

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.Single(result.Document.Messages[0].Signals);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.InvalidBitRange
                && diagnostic.Severity == DbcDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Parse_reports_egregious_big_endian_ranges_as_errors()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 2 XXX\n" +
            " SG_ TooWide : 63|16@1- (1,0) [0|65535] \"\" XXX\n");

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.InvalidBitRange
                && diagnostic.Severity == DbcDiagnosticSeverity.Error);
    }

    [Fact]
    public void Parse_accepts_in_range_big_endian_bits()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 2 XXX\n" +
            " SG_ Fits : 7|16@1- (1,0) [0|65535] \"\" XXX\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_accepts_motorola_checksum_layout_used_by_real_samples()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 8 XXX\n" +
            " SG_ Checksum : 56|8@1+ (1,0) [0|255] \"\" XXX\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.Single(result.Document.Messages[0].Signals);
    }

    [Fact]
    public void Parse_reports_signal_without_enclosing_message()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\n SG_ Orphan : 0|1@0+ (1,0) [0|1] \"\" XXX\n");

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.SignalWithoutMessage
                && diagnostic.LineNumber == 4);
    }

    [Fact]
    public void Parse_reports_unterminated_unit_string()
    {
        var result = DbcParser.Parse(
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ Bad : 0|1@0+ (1,0) [0|1] \"kph XXX\n");

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DbcDiagnosticCodes.MalformedSignal);
    }

    [Fact]
    public void Parse_preserves_comment_order_between_messages()
    {
        var original =
            "VERSION \"\"\nBU_: XXX\n\n" +
            "BO_ 100 First: 1 XXX\n" +
            " SG_ A : 0|1@0+ (1,0) [0|1] \"\" XXX\n" +
            "CM_ \"comment between messages\";\n" +
            "BA_ \"GenMsgCycleTime\" BO_ 100 50;\n" +
            "VAL_ 100 A 0 \"off\" 1 \"on\";\n" +
            "\n" +
            "BO_ 200 Second: 1 XXX\n" +
            " SG_ B : 0|1@0+ (1,0) [0|1] \"\" XXX\n";

        var result = DbcParser.Parse(original);
        Assert.True(result.IsValid);

        var serialized = DbcParser.Serialize(result.Document);
        var lines = NormalizeLines(serialized);

        var first = Array.IndexOf(lines, "BO_ 100 First: 1 XXX");
        var comment = Array.IndexOf(lines, "CM_ \"comment between messages\";");
        var attribute = Array.IndexOf(lines, "BA_ \"GenMsgCycleTime\" BO_ 100 50;");
        var valueDescription = Array.IndexOf(lines, "VAL_ 100 A 0 \"off\" 1 \"on\";");
        var second = Array.IndexOf(lines, "BO_ 200 Second: 1 XXX");

        Assert.True(first < comment);
        Assert.True(comment < attribute);
        Assert.True(attribute < valueDescription);
        Assert.True(valueDescription < second);
    }

    [Fact]
    public void Parse_preserves_multiplexor_marker_without_polluting_name()
    {
        const string content =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 2 XXX\n" +
            " SG_ Mode M : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
            " SG_ Value m1 : 8|8@0+ (1,0) [0|255] \"\" XXX\n";

        var result = DbcParser.Parse(content);
        Assert.True(result.IsValid);
        Assert.Equal("Mode", result.Document.Messages[0].Signals[0].Name);
        Assert.Equal("M", result.Document.Messages[0].Signals[0].Multiplexer);
        Assert.Equal("Value", result.Document.Messages[0].Signals[1].Name);
        Assert.Equal("m1", result.Document.Messages[0].Signals[1].Multiplexer);
        Assert.Contains("SG_ Mode M :", DbcParser.Serialize(result.Document), StringComparison.Ordinal);
    }

    private static int CountLines(string content, string prefix)
    {
        return content.Split('\n').Count(l => l.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whitespace-insensitive comparison for round-trip verification: line
    /// endings, leading indentation, and blank lines are normalized away while
    /// preserving order and all non-whitespace content.
    /// </summary>
    private static string[] NormalizeLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }
}
