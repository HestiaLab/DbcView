#nullable enable
using DbcView.Domain;
using DbcView.Infrastructure;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcParserDifferentialTests
{
    private const string Fragment =
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

    private static string BmwSamplePath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "bmw_e9x_e8x.dbc");

    [Fact]
    public void New_parser_matches_legacy_on_fragment()
    {
        AssertMatchesLegacy(Fragment);
    }

    [Fact]
    public void New_parser_matches_legacy_on_bmw_sample()
    {
        AssertMatchesLegacy(File.ReadAllText(BmwSamplePath));
    }

    [Theory]
    [InlineData("this is not a DBC file")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\nBO_ not-a-number Broken: 8 XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\n SG_ Orphan : 0|1@0+ (1,0) [0|1] \"\" XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n SG_ Bad : 0|1@0+ (abc,0) [0|1] \"\" XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n SG_ TooWide : 7|2@0+ (1,0) [0|3] \"\" XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 2 XXX\n SG_ TooWide : 63|16@1- (1,0) [0|65535] \"\" XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n SG_ Bad : 0|1@0+ (1,0) [0|1] \"kph XXX\n")]
    [InlineData("VERSION \"\"\nBU_: XXX\nVERSION \"two\"\n")]
    [InlineData("VERSION no-quotes\nBU_: XXX\n")]
    public void New_parser_matches_legacy_on_error_paths(string content)
    {
        AssertMatchesLegacy(content, compareSerialization: false);
    }

    [Fact]
    public void New_parser_matches_legacy_on_semantic_statements()
    {
        const string content =
            "VERSION \"\"\nBU_: XXX\n\n" +
            "BO_ 100 First: 1 XXX\n" +
            " SG_ A : 0|1@0+ (1,0) [0|1] \"\" XXX\n" +
            "CM_ \"comment between messages\";\n" +
            "BA_ \"GenMsgCycleTime\" BO_ 100 50;\n" +
            "VAL_ 100 A 0 \"off\" 1 \"on\";\n" +
            "\n" +
            "BO_ 200 Second: 1 XXX\n" +
            " SG_ B : 0|1@0+ (1,0) [0|1] \"\" XXX\n";

        AssertMatchesLegacy(content);
    }

    private static void AssertMatchesLegacy(string content, bool compareSerialization = true)
    {
        var actual = DbcParser.Parse(content);
        var expected = DbcParser.ParseLegacy(content);

        Assert.Equal(expected.Document.Version, actual.Document.Version);
        Assert.Equal(expected.Document.Nodes, actual.Document.Nodes);
        Assert.Equal(expected.Document.Messages.Count, actual.Document.Messages.Count);
        for (var index = 0; index < expected.Document.Messages.Count; index++)
        {
            AssertMessage(expected.Document.Messages[index], actual.Document.Messages[index]);
        }

        Assert.Equal(
            expected.Diagnostics.Select(Describe).ToArray(),
            actual.Diagnostics.Select(Describe).ToArray());

        if (compareSerialization)
        {
            Assert.Equal(
                NormalizeLines(DbcParser.Serialize(expected.Document)),
                NormalizeLines(DbcParser.Serialize(actual.Document)));
        }
    }

    private static void AssertMessage(DbcMessage expected, DbcMessage actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.Transmitter, actual.Transmitter);
        Assert.Equal(expected.Signals.Count, actual.Signals.Count);
        for (var index = 0; index < expected.Signals.Count; index++)
        {
            AssertSignal(expected.Signals[index], actual.Signals[index]);
        }
    }

    private static void AssertSignal(DbcSignal expected, DbcSignal actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.StartBit, actual.StartBit);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.IsBigEndian, actual.IsBigEndian);
        Assert.Equal(expected.IsSigned, actual.IsSigned);
        Assert.Equal(expected.Factor, actual.Factor);
        Assert.Equal(expected.Offset, actual.Offset);
        Assert.Equal(expected.Min, actual.Min);
        Assert.Equal(expected.Max, actual.Max);
        Assert.Equal(expected.Unit, actual.Unit);
        Assert.Equal(expected.Receiver, actual.Receiver);
        Assert.Equal(expected.Multiplexer, actual.Multiplexer);
    }

    private static string Describe(DbcDiagnostic diagnostic)
    {
        return $"{diagnostic.Code}|{diagnostic.Severity}|{diagnostic.LineNumber}|{diagnostic.Message}";
    }

    private static string[] NormalizeLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
