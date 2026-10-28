#nullable enable
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web.ViewModels;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcSemanticPropertyBuilderTests
{
    [Fact]
    public void File_sections_expose_comments_attributes_and_undisplayed_statements()
    {
        var file = LoadSample();

        var sections = DbcSemanticPropertyBuilder.Build(file, null, null);

        Assert.True(sections.Available);
        Assert.Null(sections.UnavailableReason);
        var comment = Assert.Single(sections.Comments);
        Assert.Equal("File level comment", comment.Text);
        Assert.Contains(
            sections.Attributes,
            attribute => attribute.Name == "BusType"
                && attribute.Source == DbcAttributeSource.Explicit
                && attribute.Value == "\"CAN\"");
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "BA_DEF_ BU_");
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "CM_ BU_");
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "CM_ EV_");
        var opaque = Assert.Single(sections.NotInterpreted);
        Assert.Equal("VENDOR_UNKNOWN_", opaque.Keyword);
    }

    [Fact]
    public void Message_attributes_mark_duplicates_and_last_value_effective()
    {
        var file = LoadSample();
        var message = file.Document.Messages.Single(candidate => candidate.Name == "Engine");

        var sections = DbcSemanticPropertyBuilder.Build(file, message, null);

        Assert.True(sections.Available);
        var cycleValues = sections.Attributes
            .Where(attribute => attribute.Name == "GenMsgCycleTime")
            .ToArray();
        Assert.Equal(2, cycleValues.Length);
        Assert.All(cycleValues, value => Assert.True(value.IsDuplicate));
        Assert.False(cycleValues[0].IsEffective);
        Assert.Equal("50", cycleValues[0].Value);
        Assert.True(cycleValues[1].IsEffective);
        Assert.Equal("60", cycleValues[1].Value);
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "BO_TX_BU_");
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "SIG_VALTYPE_");
        Assert.True(sections.HasExtendedMultiplexing);
    }

    [Fact]
    public void Signal_sections_expose_value_table_references_and_multiplexing()
    {
        var file = LoadSample();
        var engine = file.Document.Messages.Single(candidate => candidate.Name == "Engine");
        var speed = engine.Signals.Single(candidate => candidate.Name == "Speed");

        var sections = DbcSemanticPropertyBuilder.Build(file, engine, speed);

        Assert.True(sections.Available);
        Assert.Contains(sections.Comments, comment => comment.Text == "Vehicle speed");
        Assert.Contains(
            sections.Attributes,
            attribute => attribute.Name == "GenSigStartValue"
                && attribute.Source == DbcAttributeSource.Explicit
                && attribute.Value == "1.5");
        var table = Assert.IsType<DbcPropertyValueTable>(sections.ValueTable);
        Assert.Equal("MissingTable", table.Name);
        Assert.True(table.IsMissingTable);
        Assert.True(sections.HasExtendedMultiplexing);
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "SG_MUL_VAL_");
        Assert.Contains(
            sections.NotDisplayed,
            group => group.Keyword == "SIG_GROUP_");
    }

    [Fact]
    public void Undefined_attributes_are_shown_with_source_line()
    {
        var file = LoadSample();
        var status = file.Document.Messages.Single(candidate => candidate.Name == "Status");

        var sections = DbcSemanticPropertyBuilder.Build(file, status, null);

        var attribute = Assert.Single(
            sections.Attributes,
            candidate => candidate.Name == "UndefinedAttr");
        Assert.True(attribute.IsUndefined);
        Assert.Equal(DbcAttributeSource.Undefined, attribute.Source);
        Assert.Equal("7", attribute.Value);
        Assert.True(attribute.LineNumber > 0);
    }

    [Fact]
    public void Highlighted_comments_are_marked()
    {
        var file = LoadSample();
        var engine = file.Document.Messages.Single(candidate => candidate.Name == "Engine");

        var sections = DbcSemanticPropertyBuilder.Build(
            file,
            engine,
            null,
            highlightedCommentLine: 13);

        Assert.Contains(sections.Comments, comment => comment.IsHighlighted);
    }

    [Fact]
    public void Warning_documents_keep_semantics_but_error_documents_do_not()
    {
        var warningText =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 1 M: 1 XXX\n" +
            " SG_ TooWide : 7|2@0+ (1,0) [0|3] \"\" XXX\n";
        var warningParse = DbcParser.Parse(warningText);
        var warningFile = new DbcFileEntry(
            new DbcDocumentId("Samples", "warning.dbc"),
            warningText,
            warningParse);
        var warningSections = DbcSemanticPropertyBuilder.Build(
            warningFile,
            null,
            null);
        Assert.True(warningSections.Available);
        Assert.Contains(
            warningSections.NotInterpreted,
            group => group.Items.Any(statement => statement.Code == "DBC006"));

        var errorText = "this is not a DBC file";
        var errorParse = DbcParser.Parse(errorText);
        var errorFile = new DbcFileEntry(
            new DbcDocumentId("Samples", "error.dbc"),
            errorText,
            errorParse);
        var errorSections = DbcSemanticPropertyBuilder.Build(errorFile, null, null);
        Assert.False(errorSections.Available);
        Assert.Contains("error", errorSections.UnavailableReason!, StringComparison.Ordinal);
        Assert.Empty(errorSections.Comments);
        Assert.Empty(errorSections.Attributes);
    }

    private static DbcFileEntry LoadSample()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "semantics-full.dbc");
        var content = File.ReadAllText(path);
        var parse = DbcParser.Parse(content);
        Assert.True(parse.IsValid, string.Join(Environment.NewLine, parse.Diagnostics));
        return new DbcFileEntry(
            new DbcDocumentId("Samples", "semantics-full.dbc"),
            content,
            parse);
    }
}
