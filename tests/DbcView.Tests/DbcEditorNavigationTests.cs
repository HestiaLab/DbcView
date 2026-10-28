#nullable enable
using Bunit;
using DbcView.Domain;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcEditorNavigationTests : BunitContext
{
    private const string MultiMessageDbc =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "BO_ 200 Second: 1 XXX\n" +
        " SG_ C : 0|8@0+ (1,0) [0|255] \"\" XXX\n";

    public DbcEditorNavigationTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public async Task Request_expands_the_message_and_focuses_the_signal_row()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();

        Assert.Empty(cut.FindAll("[data-testid='dbc-signal']"));

        await cut.InvokeAsync(() => navigation.Request(
            new DbcEditorNavigationRequest(entry.Id, 200, "C")));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='dbc-signal']")));
        var signal = cut.Find("[data-testid='dbc-signal']");
        Assert.Contains("C", signal.TextContent, StringComparison.Ordinal);
        Assert.Equal("0", signal.GetAttribute("tabindex"));
        Assert.Equal("3", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
        Assert.Null(navigation.Pending);
    }

    [Fact]
    public async Task Request_switches_text_mode_to_table_and_reveals_message_row()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.SetMode(DbcEditorModel.ViewMode.Text);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();

        Assert.NotEmpty(cut.FindAll("textarea"));

        await cut.InvokeAsync(() => navigation.Request(
            new DbcEditorNavigationRequest(entry.Id, 100, null)));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("textarea")));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".dbc-table")));
        var message = cut.FindAll("[data-testid='dbc-message']")[0];
        Assert.Equal("0", message.GetAttribute("tabindex"));
        Assert.Equal(
            "4",
            cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
    }

    [Fact]
    public async Task Request_for_another_document_is_ignored()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();

        await cut.InvokeAsync(() => navigation.Request(
            new DbcEditorNavigationRequest(
                new DbcDocumentId("can-a", "other.dbc"),
                100,
                "A")));

        Assert.Equal(2, model.RowCount);
        Assert.Empty(cut.FindAll("[data-testid='dbc-signal']"));
        Assert.NotNull(navigation.Pending);
    }

    [Fact]
    public async Task Source_request_switches_to_text_mode_for_the_matching_statement()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();
        var span = model.ParseResult!.StatementSpans.Single(
            candidate => candidate.LineNumber == 9);

        await cut.InvokeAsync(() => navigation.Request(
            DbcEditorNavigationRequest.ForSource(entry.Id, 9)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("textarea")));
        Assert.Equal(DbcEditorModel.ViewMode.Text, model.Mode);
        Assert.Contains(
            "SG_ C",
            model.EditedContent.Substring(span.Start, span.Length),
            StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='dbc-source-warning']"));
        Assert.Null(navigation.Pending);
    }

    [Fact]
    public async Task Source_request_with_unsaved_text_warns_instead_of_navigating()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.EditedContent = "unsaved edit";
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();

        await cut.InvokeAsync(() => navigation.Request(
            DbcEditorNavigationRequest.ForSource(entry.Id, 9)));

        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-source-warning']")));
        Assert.Equal(DbcEditorModel.ViewMode.Table, model.Mode);
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void Pending_request_is_consumed_when_the_matching_panel_initializes()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var navigation = Services.GetRequiredService<DbcEditorNavigation>();
        navigation.Request(new DbcEditorNavigationRequest(entry.Id, 200, "C"));

        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='dbc-signal']")));
        Assert.Null(navigation.Pending);
    }
}
