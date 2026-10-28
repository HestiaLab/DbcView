#nullable enable
using DbcView.Domain;
using DbcView.Web.ViewModels;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcTreeModelTests
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    [Fact]
    public void NewFolder_and_Upload_raise_StateChanged()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var changed = 0;
        model.StateChanged += () => changed++;

        model.NewFolder("can-a");
        model.Upload(new DbcDocumentId("can-a", "a.dbc"), ValidDbc);

        Assert.Equal(2, changed);
        Assert.Single(model.Folders);
        Assert.Single(model.FilesFor("can-a"));
    }

    [Fact]
    public void Select_updates_full_document_identity_and_raises_StateChanged()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcTreeModel(workspace);
        var changed = 0;
        model.StateChanged += () => changed++;

        model.Select(entry.Id);

        Assert.Equal(entry.Id, model.SelectedDocument);
        Assert.Equal("can-a", model.SelectedFolder);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void NewFolder_becomes_the_upload_target()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);

        model.NewFolder("target");

        Assert.Equal("target", model.SelectedFolder);
    }

    [Fact]
    public void DocumentSelectionChanged_only_fires_for_document_selection()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcTreeModel(workspace);
        var documentSelections = 0;
        model.DocumentSelectionChanged += () => documentSelections++;

        model.NewFolder("other");
        model.SelectFolder("other");
        model.Upload(new DbcDocumentId("can-a", "b.dbc"), ValidDbc);

        Assert.Equal(0, documentSelections);

        model.Select(entry.Id);

        Assert.Equal(1, documentSelections);
    }

    [Fact]
    public void Workspace_changes_are_forwarded_to_StateChanged()
    {
        using var workspace = TestWorkspace.Create();
        var model = new DbcTreeModel(workspace);
        var changed = 0;
        model.StateChanged += () => changed++;

        workspace.CreateFolder("can-a");

        Assert.Equal(1, changed);
    }
}

public sealed class DbcEditorModelTests
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    [Fact]
    public void Open_sets_file_state_and_raises_StateChanged()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        var changed = 0;
        model.StateChanged += () => changed++;

        model.Open(entry);

        Assert.Equal(entry.Id, model.DocumentId);
        Assert.Equal(ValidDbc, model.RawContent);
        Assert.NotNull(model.Document);
        Assert.Equal(DbcEditorModel.ViewMode.Table, model.Mode);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Open_initializes_EditedContent_to_file_content()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);

        model.Open(entry);

        Assert.Equal(ValidDbc, model.EditedContent);
    }

    [Fact]
    public void SetMode_switches_and_notifies()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var changed = 0;
        model.StateChanged += () => changed++;

        model.SetMode(DbcEditorModel.ViewMode.Text);

        Assert.Equal(DbcEditorModel.ViewMode.Text, model.Mode);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Save_reparses_content_and_writes_back_to_workspace()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var edited =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n\nBO_ 200 SecondMsg: 2 XXX\n";
        model.EditedContent = edited;
        var outcome = model.Save();

        Assert.True(outcome.Saved);
        Assert.True(outcome.ParseResult!.IsValid);
        var updated = workspace.Find(entry.Id)!;
        Assert.Equal(2, updated.Document.Messages.Count);
        Assert.Equal(edited, updated.Content);
        Assert.Same(model.ParseResult, updated.ParseResult);
        Assert.Null(model.SaveError);
    }

    [Fact]
    public void Save_keeps_invalid_draft_and_exposes_diagnostics()
    {
        const string invalid = "not a dbc at all";

        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        model.EditedContent = invalid;
        var outcome = model.Save();

        Assert.True(outcome.Saved);
        Assert.True(outcome.ParseResult!.HasErrors);
        Assert.NotNull(model.LastSaveResult);
        Assert.True(model.LastSaveResult!.HasErrors);
        Assert.Equal(invalid, workspace.Find(entry.Id)!.Content);
        Assert.Equal(invalid, model.RawContent);
        Assert.True(model.ParseResult!.HasErrors);
    }

    [Fact]
    public void Save_returns_visible_error_when_file_is_gone()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        workspace.Rename(entry.Id, "renamed.dbc");

        var outcome = model.Save();

        Assert.False(outcome.Saved);
        Assert.NotNull(outcome.Error);
        Assert.Equal(outcome.Error, model.SaveError);
    }

    [Fact]
    public void Two_editors_with_same_file_name_in_different_folders_save_independently()
    {
        using var workspace = TestWorkspace.Create();
        var entryA = workspace.AddFile("A", "x.dbc", "VERSION \"A\"\nBU_: XXX\n");
        var entryB = workspace.AddFile("B", "x.dbc", "VERSION \"B\"\nBU_: XXX\n");

        var editorA = new DbcEditorModel(workspace);
        var editorB = new DbcEditorModel(workspace);
        editorA.Open(entryA);
        editorB.Open(entryB);

        editorA.EditedContent = "VERSION \"A2\"\nBU_: XXX\nBO_ 1 M: 1 XXX\n";
        var outcome = editorA.Save();

        Assert.True(outcome.Saved);
        Assert.Equal("B", workspace.Find(entryB.Id)!.Document.Version);
        Assert.Equal("A2", workspace.Find(entryA.Id)!.Document.Version);
    }
}

public sealed class DbcPropertyModelTests
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    [Fact]
    public void ShowMessage_populates_property_state()
    {
        var message = new DbcMessage(
            100u,
            "EmptyMsg",
            1,
            "XXX",
            Array.Empty<DbcSignal>());
        var model = new DbcPropertyModel();

        model.ShowMessage(message);

        Assert.Equal("Message", model.TargetType);
        Assert.Equal("100", model.Id);
        Assert.Equal("EmptyMsg", model.Name);
        Assert.Equal("1", model.Length);
        Assert.Equal("XXX", model.Transmitter);
    }

    [Fact]
    public void ShowSignal_populates_property_state()
    {
        var signal = new DbcSignal(
            "Speed",
            32,
            16,
            isBigEndian: true,
            isSigned: false,
            1.0,
            0.0,
            0.0,
            65535.0,
            "kph",
            "XXX");
        var model = new DbcPropertyModel();

        model.ShowSignal(signal);

        Assert.Equal("Signal", model.TargetType);
        Assert.Equal("Speed", model.Name);
        Assert.Equal("32", model.StartBit);
        Assert.Equal("16", model.Length);
        Assert.Equal("1", model.Factor);
        Assert.Equal("0", model.Offset);
        Assert.Equal("0", model.Min);
        Assert.Equal("65535", model.Max);
    }

    [Fact]
    public void ShowFile_uses_full_document_identity()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("A", "x.dbc", ValidDbc);
        var model = new DbcPropertyModel();

        model.ShowFile(entry);

        Assert.Equal("File", model.TargetType);
        Assert.Equal("A/x.dbc", model.TargetName);
        Assert.Equal("A/x.dbc", model.Name);
    }
}
