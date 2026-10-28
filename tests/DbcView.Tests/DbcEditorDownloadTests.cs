#nullable enable
using Bunit;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcEditorDownloadTests : BunitContext
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    public DbcEditorDownloadTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void Download_button_blocks_invalid_text_with_diagnostics()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "download.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.SetMode(DbcEditorModel.ViewMode.Text);
        model.EditedContent = "not a dbc";
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-download']").Click();

        Assert.NotNull(cut.Find("[data-testid='dbc-download-error']"));
        Assert.Contains("error", cut.Find("[data-testid='dbc-download-error']").TextContent);
        Assert.Contains(
            "DBC001",
            cut.Find("[data-testid='dbc-download-error']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Download_button_accepts_valid_text_outside_the_browser()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "valid.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-download']").Click();

        Assert.Empty(cut.FindAll("[data-testid='dbc-download-error']"));
        Assert.Contains(
            "available in the browser",
            cut.Find("[data-testid='dbc-download-notice']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revert_all_discards_text_and_structured_session_edits()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "revert.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.SetMode(DbcEditorModel.ViewMode.Text);
        var cut = Render<DbcEditorPanel>(parameters => parameters.Add(x => x.Model, model));

        Assert.True(cut.Find("[data-testid='dbc-revert-all']").HasAttribute("disabled"));

        cut.Find("textarea").Change("changed");
        await cut.InvokeAsync(() => Assert.True(model.HasSessionEdits));
        Assert.False(cut.Find("[data-testid='dbc-revert-all']").HasAttribute("disabled"));

        await cut.Find("[data-testid='dbc-revert-all']")
            .ClickAsync(new MouseEventArgs());

        Assert.False(model.HasSessionEdits);
        Assert.Equal(ValidDbc, model.EditedContent);
        Assert.Equal(ValidDbc, cut.Find("textarea").GetAttribute("value"));
    }
}
