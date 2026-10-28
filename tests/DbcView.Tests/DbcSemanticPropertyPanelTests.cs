#nullable enable
using Bunit;
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcSemanticPropertyPanelTests : BunitContext
{
    public DbcSemanticPropertyPanelTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void File_panel_renders_semantics_and_encoding()
    {
        var entry = LoadSemanticsSample(new DbcFileEncoding("UTF-8", false, 0));
        var model = new DbcPropertyModel();
        model.ShowFile(entry);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        Assert.Contains("UTF-8", cut.Find("[data-testid='dbc-file-encoding']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='dbc-property-comments']"));
        Assert.NotNull(cut.Find("[data-testid='dbc-property-attributes']"));
        Assert.NotNull(cut.Find("[data-testid='dbc-property-not-displayed']"));
        Assert.NotNull(cut.Find("[data-testid='dbc-property-not-interpreted']"));
    }

    [Fact]
    public void Message_panel_marks_duplicates_and_extended_multiplexing()
    {
        var entry = LoadSemanticsSample();
        var engine = entry.Document.Messages.Single(message => message.Name == "Engine");
        var model = new DbcPropertyModel();
        model.ShowFile(entry);
        model.ShowMessage(engine);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        var attributes = cut.FindAll("[data-testid='dbc-attribute']");
        Assert.Equal(2, attributes.Count(attribute => attribute.TextContent.Contains("duplicate")));
        Assert.NotNull(cut.Find("[data-testid='dbc-extended-multiplexing']"));
        Assert.NotNull(cut.Find("[data-testid='dbc-property-comments']"));
    }

    [Fact]
    public void Signal_panel_renders_value_table_and_source_line_buttons()
    {
        var entry = LoadSemanticsSample();
        var engine = entry.Document.Messages.Single(message => message.Name == "Engine");
        var speed = engine.Signals.Single(signal => signal.Name == "Speed");
        var model = new DbcPropertyModel();
        model.ShowFile(entry);
        model.ShowSignal(speed);
        var locatedLine = 0;

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(
                component => component.OnLocateSource,
                EventCallback.Factory.Create<int>(this, line => locatedLine = line)));

        Assert.NotNull(cut.Find("[data-testid='dbc-property-value-table']"));
        Assert.Contains("MissingTable", cut.Find("[data-testid='dbc-property-value-table']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='dbc-extended-multiplexing']"));

        var lineButton = cut.FindAll("[data-testid='dbc-locate-source']")[0];
        lineButton.Click();
        Assert.True(locatedLine > 0);
    }

    [Fact]
    public void Undefined_attribute_produces_a_visible_warning()
    {
        var entry = LoadSemanticsSample();
        var status = entry.Document.Messages.Single(message => message.Name == "Status");
        var model = new DbcPropertyModel();
        model.ShowFile(entry);
        model.ShowMessage(status);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        Assert.NotNull(cut.Find("[data-testid='dbc-undefined-attribute']"));
        Assert.Contains(
            cut.FindAll("[data-testid='dbc-attribute']"),
            attribute => attribute.GetAttribute("data-undefined") == "true");
    }

    [Fact]
    public void Highlighted_comment_is_marked()
    {
        var entry = LoadSemanticsSample();
        var engine = entry.Document.Messages.Single(message => message.Name == "Engine");
        var comment = entry.ParseResult.Semantics!.MessageComments.Single();
        var model = new DbcPropertyModel();
        model.ShowFile(entry);
        model.ShowMessage(engine);
        model.HighlightComment(comment);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        Assert.Contains(
            cut.FindAll("[data-testid='dbc-comment']"),
            element => element.GetAttribute("data-highlighted") == "true");
    }

    [Fact]
    public async Task Value_table_dialog_lists_all_entries_and_closes()
    {
        using var workspace = TestWorkspace.Create();
        var values = string.Join(
            " ",
            Enumerable.Range(0, 12).Select(
                value => $"{value} \"Value {value}\""));
        var content =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ S : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
            $"VAL_ 100 S {values};\n";
        var entry = workspace.AddFile("can-a", "values.dbc", content);
        var model = new DbcPropertyModel();
        model.ShowFile(entry);
        var signal = entry.Document.Messages[0].Signals[0];
        model.ShowSignal(signal);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        cut.Find("[data-testid='dbc-view-value-table']").Click();
        Assert.NotNull(cut.Find("[data-testid='dbc-value-table-dialog']"));
        Assert.Equal(
            12,
            cut.FindAll("[data-testid='dbc-value-table-dialog'] .dbc-value-row").Count);

        await cut.Find("[data-testid='dbc-close-value-table']")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Empty(cut.FindAll("[data-testid='dbc-value-table-dialog']"));
    }

    [Fact]
    public void Invalid_document_shows_reason_instead_of_empty_sections()
    {
        const string content = "this is not a DBC file";
        var parse = DbcParser.Parse(content);
        var entry = new DbcFileEntry(
            new DbcDocumentId("Samples", "invalid.dbc"),
            content,
            parse);
        var model = new DbcPropertyModel();
        model.ShowFile(entry);

        var cut = Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model));

        Assert.NotNull(cut.Find("[data-testid='dbc-semantics-unavailable']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-property-comments']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-property-attributes']"));
    }

    private static DbcFileEntry LoadSemanticsSample(DbcFileEncoding? encoding = null)
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
            parse,
            encoding);
    }
}
