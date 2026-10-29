#nullable enable
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web.ViewModels;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcCommandPaletteModelTests
{
    private static readonly DbcDocument Document = CreateDocument(
        Message(
            100,
            "EngineSpeed",
            "ECU1",
            ("SpeedWheel", "ECU2"),
            ("BrakePressure", "ECU1")),
        Message(
            200,
            "EngineTemp",
            "ECU2",
            ("SpeedLimit", "ECU1,ECU2"),
            ("Alpha", "ECU2")),
        Message(
            300,
            "WheelSpeed",
            "ECU1",
            ("WheelSpeedRaw", "ECU2")));

    [Fact]
    public void Exact_prefix_and_substring_matches_rank_in_that_order()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);
        model.SetQuery("WheelSpeed");

        var message = Assert.Single(model.MessageResults);
        Assert.Equal("WheelSpeed", message.Message.Name);
        Assert.Equal(DbcPaletteMatchRank.Exact, message.Rank);

        var signal = Assert.Single(model.SignalResults);
        Assert.Equal("WheelSpeedRaw", signal.Signal!.Name);
        Assert.Equal(DbcPaletteMatchRank.Prefix, signal.Rank);

        model.SetQuery("speed");
        Assert.Equal(
            new[] { "EngineSpeed", "WheelSpeed" },
            model.MessageResults.Select(result => result.PrimaryText));
        Assert.All(
            model.MessageResults,
            result => Assert.Equal(DbcPaletteMatchRank.Substring, result.Rank));
        Assert.Equal(
            new[] { "SpeedWheel", "SpeedLimit", "WheelSpeedRaw" },
            model.SignalResults.Select(result => result.PrimaryText));
    }

    [Fact]
    public void Ties_keep_source_order()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);
        model.SetQuery("speed");

        Assert.Equal(
            new[] { "SpeedWheel", "SpeedLimit" },
            model.SignalResults
                .Where(result => result.Rank == DbcPaletteMatchRank.Prefix)
                .Select(result => result.PrimaryText));
    }

    [Fact]
    public void Message_ids_match_decimal_and_hexadecimal_forms()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);

        model.SetQuery("100");
        var decimalMatch = Assert.Single(model.MessageResults);
        Assert.Equal(100u, decimalMatch.Message.Id);
        Assert.Equal(DbcPaletteMatchRank.Exact, decimalMatch.Rank);

        model.SetQuery("0x64");
        Assert.Equal(100u, Assert.Single(model.MessageResults).Message.Id);

        model.SetQuery("0xC8");
        Assert.Equal(200u, Assert.Single(model.MessageResults).Message.Id);

        model.SetQuery("0xc8");
        Assert.Equal(200u, Assert.Single(model.MessageResults).Message.Id);
    }

    [Fact]
    public void Transmitter_and_receiver_fields_are_searchable()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);
        model.SetQuery("ecu2");

        Assert.Contains(
            model.MessageResults,
            result => result.Message.Name == "EngineTemp"
                && result.Rank == DbcPaletteMatchRank.Exact);
        Assert.Contains(
            model.SignalResults,
            result => result.Signal!.Name == "SpeedWheel"
                && result.Rank == DbcPaletteMatchRank.Exact);
        Assert.Contains(
            model.SignalResults,
            result => result.Signal!.Name == "SpeedLimit"
                && result.Rank == DbcPaletteMatchRank.Substring);
    }

    [Fact]
    public void Results_are_grouped_and_capped_at_fifty()
    {
        var messages = Enumerable.Range(0, 60)
            .Select(index => Message(
                (uint)(1000 + index),
                $"Match_{index:00}",
                "XXX",
                ($"Signal_{index:00}", "XXX")))
            .ToArray();
        var model = new DbcCommandPaletteModel();
        model.SetDocument(CreateDocument(messages));

        model.SetQuery("Match_");
        Assert.Equal(50, model.MessageResults.Count);
        Assert.Empty(model.SignalResults);
        Assert.Equal(50, model.ResultCount);
        Assert.Equal(60, model.TotalMatchCount);

        model.SetQuery("Signal_");
        Assert.Empty(model.MessageResults);
        Assert.Equal(50, model.SignalResults.Count);
        Assert.Equal(50, model.ResultCount);
        Assert.Equal(60, model.TotalMatchCount);
    }

    [Fact]
    public void Query_preserves_typed_whitespace_but_matches_trimmed_text()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);

        model.SetQuery("  speed  ");

        Assert.Equal("  speed  ", model.Query);
        Assert.True(model.HasQuery);
        Assert.NotEmpty(model.SignalResults);
    }

    [Fact]
    public void Empty_query_returns_no_results_and_no_hint_rows()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);

        model.SetQuery("   ");

        Assert.False(model.HasQuery);
        Assert.Empty(model.CommandResults);
        Assert.Empty(model.MessageResults);
        Assert.Empty(model.SignalResults);
        Assert.Equal(0, model.TotalMatchCount);
    }

    [Fact]
    public void Comment_text_is_searchable_but_file_comments_are_not()
    {
        const string content =
            "VERSION \"\"\nBU_: XXX\n\n" +
            "BO_ 100 Engine: 2 XXX\n" +
            " SG_ Speed : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
            "CM_ \"private file note\";\n" +
            "CM_ BO_ 100 \"message context note\";\n" +
            "CM_ SG_ 100 Speed \"vehicle speed comment\";\n";
        var parse = DbcParser.Parse(content);
        var model = new DbcCommandPaletteModel();
        model.SetDocument(parse.Document, parse.Semantics);

        model.SetQuery("vehicle");

        var signal = Assert.Single(model.SignalResults);
        Assert.NotNull(signal.Comment);
        Assert.Equal("vehicle speed comment", signal.Comment!.Text);

        model.SetQuery("context");
        var message = Assert.Single(model.MessageResults);
        Assert.NotNull(message.Comment);
        Assert.Equal("message context note", message.Comment!.Text);

        model.SetQuery("private");
        Assert.Empty(model.MessageResults);
        Assert.Empty(model.SignalResults);
    }

    [Fact]
    public void SetDocument_resets_the_query_and_results()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);
        model.SetQuery("speed");
        Assert.NotEmpty(model.SignalResults);

        model.SetDocument(Document);

        Assert.Empty(model.CommandResults);
        Assert.Empty(model.MessageResults);
        Assert.Empty(model.SignalResults);
        Assert.Equal(string.Empty, model.Query);
        Assert.Equal(0, model.TotalMatchCount);
    }

    [Fact]
    public void Check_command_matches_name_and_keyword_queries()
    {
        var model = new DbcCommandPaletteModel();
        model.SetDocument(Document);

        model.SetQuery("check");

        var command = Assert.Single(model.CommandResults);
        Assert.Equal(DbcCommandPaletteModel.CheckDocumentCommandId, command.Id);
        Assert.Equal(1, model.ResultCount);
        Assert.Equal(1, model.TotalMatchCount);

        model.SetQuery("validate");
        Assert.Single(model.CommandResults);

        model.SetQuery("speed");
        Assert.Empty(model.CommandResults);
    }

    private static DbcMessage Message(
        uint id,
        string name,
        string transmitter,
        params (string Name, string Receiver)[] signals)
    {
        var parsedSignals = signals
            .Select(signal => new DbcSignal(
                signal.Name,
                0,
                8,
                isBigEndian: false,
                isSigned: false,
                1,
                0,
                0,
                255,
                string.Empty,
                signal.Receiver))
            .ToArray();
        return new DbcMessage(id, name, 8, transmitter, parsedSignals);
    }

    private static DbcDocument CreateDocument(params DbcMessage[] messages)
    {
        return new DbcDocument(
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            messages.Select(message => (DbcSection)new DbcMessageSection(message)).ToArray());
    }
}
