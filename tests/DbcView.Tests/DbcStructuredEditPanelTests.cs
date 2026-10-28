#nullable enable
using Bunit;
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcStructuredEditPanelTests : BunitContext
{
    public DbcStructuredEditPanelTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public async Task Comment_edit_previews_changes_and_applies_through_the_callback()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "edit.dbc", SemanticsSample);
        var editor = OpenEditor(workspace, entry);
        var property = new DbcPropertyModel();
        property.ShowFile(entry);
        var engine = entry.Document.Messages.Single(message => message.Name == "Engine");
        property.ShowMessage(engine);
        DbcStructuredEdit? applied = null;

        var cut = RenderPanel(property, edit =>
        {
            applied = edit;
            return Success(edit);
        });

        cut.FindAll("[data-testid='dbc-edit-comment']")[0].Click();
        Assert.NotNull(cut.Find("[data-testid='dbc-edit-dialog']"));
        Assert.Contains(
            "CM_ BO_ 100",
            cut.Find("[data-testid='dbc-edit-old']").TextContent,
            StringComparison.Ordinal);

        cut.Find("[data-testid='dbc-edit-comment-text']").Input("updated comment");
        Assert.Contains(
            "CM_ BO_ 100 \"updated comment\";",
            cut.Find("[data-testid='dbc-edit-preview']").TextContent,
            StringComparison.Ordinal);

        await cut.Find("[data-testid='dbc-edit-apply']")
            .ClickAsync(new MouseEventArgs());

        Assert.NotNull(applied);
        Assert.Equal(DbcStructuredEditKind.Comment, applied!.Kind);
        Assert.Equal("updated comment", applied.CommentText);
        Assert.Empty(cut.FindAll("[data-testid='dbc-edit-dialog']"));
        Assert.NotNull(editor);
    }

    [Fact]
    public void Dirty_text_buffer_disables_semantic_edit_actions()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "dirty.dbc", SemanticsSample);
        var editor = OpenEditor(workspace, entry);
        var property = new DbcPropertyModel();
        property.ShowFile(entry);
        property.ShowMessage(entry.Document.Messages.Single(message => message.Name == "Engine"));
        var cut = RenderPanel(property, Success);

        Assert.Empty(cut.FindAll("[data-testid='dbc-edit-disabled']"));
        Assert.False(cut.FindAll("[data-testid='dbc-edit-comment']")[0].HasAttribute("disabled"));

        editor.EditedContent = "unsaved";
        Services.GetRequiredService<DbcEditorSessionRegistry>().NotifyChanged();

        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-edit-disabled']")));
        Assert.True(cut.FindAll("[data-testid='dbc-edit-comment']")[0].HasAttribute("disabled"));
    }

    [Fact]
    public void Duplicate_attributes_only_expose_the_effective_edit_action()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "dupes.dbc", SemanticsSample);
        OpenEditor(workspace, entry);
        var property = new DbcPropertyModel();
        property.ShowFile(entry);
        property.ShowMessage(entry.Document.Messages.Single(message => message.Name == "Engine"));
        var cut = RenderPanel(property, Success);

        var duplicateRows = cut.FindAll("[data-testid='dbc-attribute'][data-duplicate='true']");
        Assert.Equal(2, duplicateRows.Count);
        Assert.Single(cut.FindAll("[data-testid='dbc-edit-attribute']"));

        cut.Find("[data-testid='dbc-edit-attribute']").Click();
        Assert.NotNull(cut.Find("[data-testid='dbc-edit-duplicate']"));
        Assert.Contains(
            "BA_ \"GenMsgCycleTime\" BO_ 100 60;",
            cut.Find("[data-testid='dbc-edit-old']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Referenced_value_table_edit_previews_an_inline_copy()
    {
        const string content =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ S : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
            "VAL_TABLE_ Shared 0 \"Off\" 1 \"On\";\n" +
            "VAL_ 100 S VAL_TABLE_ Shared;\n";
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "table.dbc", content);
        OpenEditor(workspace, entry);
        var property = new DbcPropertyModel();
        property.ShowFile(entry);
        property.ShowSignal(entry.Document.Messages[0].Signals[0]);
        var cut = RenderPanel(property, Success);

        cut.Find("[data-testid='dbc-edit-value-table']").Click();

        Assert.NotNull(cut.Find("[data-testid='dbc-edit-inline']"));
        Assert.Contains(
            "VAL_ 100 S 0 \"Off\" 1 \"On\";",
            cut.Find("[data-testid='dbc-edit-preview']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_value_table_offers_an_add_action()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "add.dbc", SemanticsSample);
        OpenEditor(workspace, entry);
        var property = new DbcPropertyModel();
        property.ShowFile(entry);
        var temp = entry.Document.Messages.Single(message => message.Name == "Engine")
            .Signals.Single(signal => signal.Name == "Temp");
        property.ShowSignal(temp);
        var cut = RenderPanel(property, Success);

        var button = cut.Find("[data-testid='dbc-edit-value-table']");
        Assert.Equal("Add value table", button.TextContent.Trim());
        button.Click();
        Assert.NotNull(cut.Find("[data-testid='dbc-edit-dialog']"));
        Assert.Contains(
            "Complete the form",
            cut.Find("[data-testid='dbc-edit-dialog']").TextContent,
            StringComparison.Ordinal);
    }

    private IRenderedComponent<DbcPropertyPanel> RenderPanel(
        DbcPropertyModel model,
        Func<DbcStructuredEdit, DbcStructuredEditOutcome> applyEdit)
    {
        return Render<DbcPropertyPanel>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.ApplyEdit, applyEdit));
    }

    private DbcEditorModel OpenEditor(DbcFileWorkspace workspace, DbcFileEntry entry)
    {
        var editor = new DbcEditorModel(workspace);
        editor.Open(entry);
        Services.GetRequiredService<DbcEditorSessionRegistry>().Register(editor);
        return editor;
    }

    private static DbcStructuredEditOutcome Success(DbcStructuredEdit edit)
    {
        return new DbcStructuredEditOutcome(
            true,
            null,
            new DbcStatementDiff(edit.ExistingLine ?? 1, "modified", "old", "new"),
            null,
            Array.Empty<DbcDiagnostic>());
    }

    private static readonly string SemanticsSample = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Samples", "semantics-full.dbc"));
}
