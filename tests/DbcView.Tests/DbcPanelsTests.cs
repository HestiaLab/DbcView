#nullable enable
using DbcView.Components;
using DbcView.Domain;
using DbcView.ViewModels;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcPanelsTests : BunitContext
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    private const string InvalidDbc = "this is not a DBC file";

    public DbcPanelsTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void TreePanel_renders_folders_and_files_from_workspace()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateFolder("can-a");
        workspace.AddFile("can-a", "engine.dbc", ValidDbc);
        var model = new DbcTreeModel(workspace);

        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        Assert.Contains("can-a", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("engine.dbc", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='dbc-tree']"));
    }

    [Fact]
    public void TreePanel_upload_adds_file_to_workspace()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);

        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        // Upload targets the default "DBC" folder created on first upload.
        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));

        Assert.Single(workspace.Folders);
        Assert.Single(workspace.FilesIn("DBC"));
        Assert.Equal(
            "engine.dbc",
            workspace.Find(new DbcDocumentId("DBC", "engine.dbc"))!.Id.FileName);
    }

    [Fact]
    public void TreePanel_duplicate_upload_keeps_workspace_and_shows_message()
    {
        // Uploading the same file name twice must not escape the event handler
        // as an unhandled exception: the workspace keeps the first file and the
        // panel surfaces a visible message (file-tree duplicate-name rule).
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);

        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));
        input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));

        cut.WaitForAssertion(
            () =>
            {
                Assert.Single(workspace.FilesIn("DBC"));
                Assert.Contains(
                    "already exists",
                    cut.Find("[data-testid='dbc-tree-message']").TextContent,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void TreePanel_rejects_oversized_upload_without_unhandled_exception()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var oversized = new string('x', (4 * 1024 * 1024) + 1);
        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(oversized, "big.dbc"));

        cut.WaitForAssertion(
            () =>
            {
                var message = cut.Find("[data-testid='dbc-tree-message']");
                Assert.Equal("alert", message.GetAttribute("role"));
                Assert.Contains("too large", message.TextContent, StringComparison.Ordinal);
                Assert.Empty(workspace.Folders);
            });

        // The failed upload must leave the panel usable for the next upload.
        input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(ValidDbc, "ok.dbc"));

        cut.WaitForAssertion(
            () =>
            {
                Assert.Single(workspace.FilesIn("DBC"));
                Assert.Contains(
                    "uploaded",
                    cut.Find("[data-testid='dbc-tree-message']").TextContent,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void TreePanel_upload_of_invalid_dbc_reports_unvalidated_draft()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var input = cut.FindComponent<InputFile>();
        input.UploadFiles(InputFileContent.CreateFromText(InvalidDbc, "broken.dbc"));

        cut.WaitForAssertion(
            () =>
            {
                var file = workspace.Find(new DbcDocumentId("DBC", "broken.dbc"));
                Assert.NotNull(file);
                Assert.True(file!.HasParseErrors);
                Assert.Equal(InvalidDbc, file.Content);
                var message = cut.Find("[data-testid='dbc-tree-message']");
                Assert.Equal("alert", message.GetAttribute("role"));
                Assert.Contains("unvalidated draft", message.TextContent, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void TreePanel_reports_too_many_selected_files_without_unhandled_exception()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var files = Enumerable.Range(0, 11)
            .Select(index => InputFileContent.CreateFromText(ValidDbc, $"f{index}.dbc"))
            .ToArray();
        cut.FindComponent<InputFile>().UploadFiles(files);

        cut.WaitForAssertion(
            () =>
            {
                var message = cut.Find("[data-testid='dbc-tree-message']");
                Assert.Equal("alert", message.GetAttribute("role"));
                Assert.Contains("could not be read", message.TextContent, StringComparison.Ordinal);
                Assert.Empty(workspace.Folders);
            });
    }

    [Fact]
    public void TreePanel_upload_goes_to_the_selected_folder()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateFolder("A");
        workspace.CreateFolder("B");
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var folderB = cut.FindAll("[data-testid='dbc-folder']")
            .Single(button => button.TextContent.Contains("B", StringComparison.Ordinal));
        folderB.Click();

        Assert.Equal("B", model.SelectedFolder);
        Assert.Contains(
            "B",
            cut.Find("[data-testid='dbc-upload-target']").TextContent,
            StringComparison.Ordinal);

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "x.dbc"));

        Assert.NotNull(workspace.Find(new DbcDocumentId("B", "x.dbc")));
        Assert.Empty(workspace.FilesIn("A"));
    }

    [Fact]
    public void TreePanel_new_folder_becomes_the_visible_upload_target()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-new-folder']").Click();

        var folder = model.SelectedFolder;
        Assert.NotNull(folder);
        Assert.Contains(folder, cut.Find("[data-testid='dbc-upload-target']").TextContent, StringComparison.Ordinal);

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "x.dbc"));

        Assert.NotNull(workspace.Find(new DbcDocumentId(folder!, "x.dbc")));
    }

    [Fact]
    public void TreePanel_exposes_native_keyboard_operable_buttons()
    {
        using var workspace = TestWorkspace.Create();
        workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        var folder = cut.Find("[data-testid='dbc-folder']");
        Assert.Equal("BUTTON", folder.TagName, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(folder.GetAttribute("aria-pressed")));

        var file = cut.Find("[data-testid='dbc-file']");
        Assert.Equal("BUTTON", file.TagName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("false", file.GetAttribute("aria-pressed"));
        Assert.Null(file.GetAttribute("tabindex"));

        var upload = cut.Find(".dbc-upload input[type='file']");
        Assert.Null(upload.GetAttribute("hidden"));
        Assert.DoesNotContain("display: none", upload.GetAttribute("style") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void TreePanel_file_selection_sets_aria_pressed()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcTreeModel(workspace);
        var cut = Render<DbcTreePanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-file']").Click();

        Assert.Equal(entry.Id, model.SelectedDocument);
        Assert.Equal(
            "true",
            cut.Find("[data-testid='dbc-file']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void EditorPanel_switches_between_text_and_table_views()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        // Table view is the default; the message row renders.
        Assert.Contains("EmptyMsg", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='dbc-mode-text']").Click();
        Assert.Contains("VERSION", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='dbc-mode-table']").Click();
        Assert.Contains("EmptyMsg", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorPanel_save_updates_workspace_content()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-mode-text']").Click();
        var textarea = cut.Find("textarea");
        var edited =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n\nBO_ 200 SecondMsg: 2 XXX\n";
        textarea.Change(edited);
        cut.Find("[data-testid='dbc-save']").Click();

        Assert.True(workspace.Find(entry.Id)!.ParseResult.IsValid);
        Assert.Equal(2, workspace.Find(entry.Id)!.Document.Messages.Count);
        cut.WaitForAssertion(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='dbc-save-success']")));
    }

    [Fact]
    public void EditorPanel_save_invalid_draft_shows_errors_and_keeps_raw_text()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid='dbc-mode-text']").Click();
        cut.Find("textarea").Change(InvalidDbc);
        cut.Find("[data-testid='dbc-save']").Click();

        cut.WaitForAssertion(
            () =>
            {
                var alert = cut.Find("[data-testid='dbc-parse-errors']");
                Assert.Equal("alert", alert.GetAttribute("role"));
                Assert.Contains("unvalidated draft", alert.TextContent, StringComparison.Ordinal);
                Assert.Contains("DBC001", alert.TextContent, StringComparison.Ordinal);
                Assert.Equal(InvalidDbc, workspace.Find(entry.Id)!.Content);
            });

        cut.Find("[data-testid='dbc-mode-table']").Click();
        Assert.Contains(
            "Table view is unavailable",
            cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EditorPanel_shows_parse_errors_for_invalid_file_instead_of_empty_document()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "bad.dbc", InvalidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        Assert.NotEmpty(cut.FindAll("[data-testid='dbc-parse-errors']"));
        Assert.Contains("DBC001", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No messages in this DBC file", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorPanel_still_reports_valid_empty_file_as_no_messages()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "empty.dbc", "VERSION \"\"\nBU_: XXX\n");
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        Assert.Contains("No messages in this DBC file.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='dbc-parse-errors']"));
    }

    [Fact]
    public void EditorPanel_shows_bit_range_warnings_without_hiding_the_table()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile(
            "can-a",
            "warning.dbc",
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ TooWide : 7|2@0+ (1,0) [0|3] \"\" XXX\n");
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var cut = Render<DbcEditorPanel>(p => p.Add(x => x.Model, model));

        Assert.NotEmpty(cut.FindAll("[data-testid='dbc-parse-warnings']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-parse-errors']"));
        Assert.Contains("TooWide", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorPanel_keeps_unsaved_text_after_host_rerender()
    {
        // FR-08: unrelated host re-renders must not silently discard unsaved
        // editor input. Reproduce the full flow through the workspace: upload a
        // file, open it in the editor, type unsaved text, then force a host
        // re-render and assert the textarea keeps the unsaved content.
        var cut = Render<DbcViewWorkspace>();

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));

        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        cut.Find("[data-testid='dbc-file']").Click();

        // Switch the editor to text view so the textarea renders.
        cut.WaitForAssertion(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='dbc-mode-text']")));
        cut.Find("[data-testid='dbc-mode-text']").Click();

        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-editor'] textarea")));
        var textarea = cut.Find("[data-testid='dbc-editor'] textarea");
        textarea.Change("unsaved-edit");

        // Force the host to re-render (the workspace.Changed path does this in
        // the running app). The edit buffer must survive.
        cut.Render();

        Assert.Equal(
            "unsaved-edit",
            cut.Find("[data-testid='dbc-editor'] textarea").GetAttribute("value"));
    }

    [Fact]
    public void PropertyPanel_shows_file_properties()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcPropertyModel();
        model.ShowFile(entry);

        var cut = Render<DbcPropertyPanel>(p => p.Add(x => x.Model, model));

        Assert.Contains("File", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("can-a/a.dbc", cut.Markup, StringComparison.Ordinal);
    }
}
