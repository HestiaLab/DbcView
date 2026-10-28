#nullable enable
using Bunit;
using DbcView.Domain;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcEditorTableTests : BunitContext
{
    private const string MultiMessageDbc =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "BO_ 200 Second: 1 XXX\n" +
        " SG_ C : 0|8@0+ (1,0) [0|255] \"\" XXX\n";

    private const string SingleMessageDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    public DbcEditorTableTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void Default_table_is_collapsed_and_shows_signal_counts()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        Assert.Equal(2, cut.FindAll("[data-testid='dbc-message']").Count);
        Assert.Empty(cut.FindAll("[data-testid='dbc-signal']"));
        Assert.Equal("2", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
        Assert.Contains(
            "2 messages",
            cut.Find("[data-testid='dbc-table-summary']").TextContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "3 signals",
            cut.Find("[data-testid='dbc-table-summary']").TextContent,
            StringComparison.Ordinal);

        var messageRows = cut.FindAll(".dbc-message-row");
        Assert.Equal("2", messageRows[0].QuerySelectorAll("[role='cell']")[3].TextContent.Trim());
        Assert.Equal("1", messageRows[1].QuerySelectorAll("[role='cell']")[3].TextContent.Trim());
        Assert.Equal("1", messageRows[0].GetAttribute("aria-rowindex"));
        Assert.Equal("2", messageRows[1].GetAttribute("aria-rowindex"));
        Assert.Equal("0", cut.FindAll("[data-testid='dbc-message']")[0].GetAttribute("tabindex"));
        Assert.Equal("-1", cut.FindAll("[data-testid='dbc-message']")[1].GetAttribute("tabindex"));

        var toggles = cut.FindAll("[data-testid='dbc-toggle-message']");
        Assert.Equal(2, toggles.Count);
        Assert.All(toggles, toggle => Assert.Equal("false", toggle.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void Message_without_signals_has_no_toggle()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", SingleMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        Assert.Empty(cut.FindAll("[data-testid='dbc-toggle-message']"));
        Assert.Equal("0", cut.Find(".dbc-message-row").QuerySelectorAll("[role='cell']")[3].TextContent.Trim());
    }

    [Fact]
    public void Expand_button_reveals_and_hides_signal_rows()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-toggle-message']").Click();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(2, cut.FindAll("[data-testid='dbc-signal']").Count);
                Assert.Equal("4", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
                Assert.Equal(
                    "true",
                    cut.FindAll("[data-testid='dbc-toggle-message']")[0].GetAttribute("aria-expanded"));
                var rows = cut.FindAll(".dbc-table-body [data-row-index]");
                Assert.Equal(4, rows.Count);
                Assert.Equal(
                    new[] { "1", "2", "3", "4" },
                    rows.Select(row => row.GetAttribute("aria-rowindex")).ToArray());
            });

        cut.FindAll("[data-testid='dbc-toggle-message']")[0].Click();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Empty(cut.FindAll("[data-testid='dbc-signal']"));
                Assert.Equal("2", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
            });
    }

    [Fact]
    public void Expand_all_and_collapse_all_update_rows_and_button_state()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-expand-all']").Click();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(3, cut.FindAll("[data-testid='dbc-signal']").Count);
                Assert.Equal("5", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
                Assert.True(cut.Find("[data-testid='dbc-expand-all']").HasAttribute("disabled"));
                Assert.False(cut.Find("[data-testid='dbc-collapse-all']").HasAttribute("disabled"));
            });

        cut.Find("[data-testid='dbc-collapse-all']").Click();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Empty(cut.FindAll("[data-testid='dbc-signal']"));
                Assert.Equal("2", cut.Find(".dbc-table").GetAttribute("aria-rowcount"));
                Assert.False(cut.Find("[data-testid='dbc-expand-all']").HasAttribute("disabled"));
                Assert.True(cut.Find("[data-testid='dbc-collapse-all']").HasAttribute("disabled"));
            });
    }

    [Fact]
    public async Task Keyboard_navigation_moves_the_roving_tabindex_and_expands()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        await cut.FindAll("[data-testid='dbc-message']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal("-1", cut.FindAll("[data-testid='dbc-message']")[0].GetAttribute("tabindex"));
                Assert.Equal("0", cut.FindAll("[data-testid='dbc-message']")[1].GetAttribute("tabindex"));
            });

        await cut.FindAll("[data-testid='dbc-message']")[1]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        await cut.FindAll("[data-testid='dbc-message']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(2, cut.FindAll("[data-testid='dbc-signal']").Count);
                Assert.Equal("-1", cut.FindAll("[data-testid='dbc-signal']")[0].GetAttribute("tabindex"));
            });

        await cut.FindAll("[data-testid='dbc-message']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(
            () => Assert.Equal("0", cut.Find("[data-testid='dbc-signal']").GetAttribute("tabindex")));

        await cut.Find("[data-testid='dbc-signal']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        cut.WaitForAssertion(
            () =>
            {
                var messages = cut.FindAll("[data-testid='dbc-message']");
                Assert.Equal("-1", messages[0].GetAttribute("tabindex"));
                Assert.Equal("0", messages[1].GetAttribute("tabindex"));
            });

        await cut.FindAll("[data-testid='dbc-message']")[1]
            .KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await cut.FindAll("[data-testid='dbc-message']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowLeft" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='dbc-signal']")));
    }

    [Fact]
    public void Selecting_rows_updates_callbacks_and_selected_state()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        DbcMessage? selectedMessage = null;
        DbcSignal? selectedSignal = null;

        var cut = Render<DbcEditorPanel>(
            p => p.Add(x => x.Model, model)
                .Add(x => x.OnMessageSelected, (DbcMessage message) => selectedMessage = message)
                .Add(x => x.OnSignalSelected, (DbcSignal signal) => selectedSignal = signal));

        cut.FindAll("[data-testid='dbc-message']")[0].Click();

        Assert.Equal("First", selectedMessage!.Name);
        Assert.True(cut.FindAll(".dbc-message-row")[0].ClassList.Contains("is-selected"));
        Assert.Equal("true", cut.FindAll(".dbc-message-row")[0].GetAttribute("aria-selected"));

        cut.Find("[data-testid='dbc-toggle-message']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='dbc-signal']")));
        cut.FindAll("[data-testid='dbc-signal']")[0].Click();

        Assert.Equal("A", selectedSignal!.Name);
        Assert.True(cut.FindAll(".dbc-signal-row")[0].ClassList.Contains("is-selected"));
        Assert.False(cut.FindAll(".dbc-message-row")[0].ClassList.Contains("is-selected"));
    }
}
