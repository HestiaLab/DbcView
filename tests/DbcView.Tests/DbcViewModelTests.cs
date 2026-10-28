#nullable enable
using DbcView.Application;
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

    private const string MultiMessageDbc =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "BO_ 200 Second: 1 XXX\n" +
        " SG_ C : 0|8@0+ (1,0) [0|255] \"\" XXX\n";

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
    public void Open_defaults_to_collapsed_rows()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);

        model.Open(entry);

        Assert.Equal(2, model.MessageCount);
        Assert.Equal(3, model.SignalCount);
        Assert.Equal(2, model.RowCount);
        Assert.All(model.EnumerateRows(), row => Assert.True(row.IsMessage));
        Assert.All(model.EnumerateRows(), row => Assert.False(row.IsExpanded));
        Assert.False(model.HasExpandedMessages);
        Assert.False(model.AllMessagesExpanded);
    }

    [Fact]
    public void ToggleExpanded_adds_and_removes_signal_rows()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var first = model.Document!.Messages[0];

        model.ToggleExpanded(first);

        Assert.True(model.IsExpanded(first));
        Assert.Equal(4, model.RowCount);
        Assert.Equal(DbcTableRowKind.Message, model.GetRow(0).Kind);
        Assert.Equal(DbcTableRowKind.Signal, model.GetRow(1).Kind);
        Assert.Equal(DbcTableRowKind.Signal, model.GetRow(2).Kind);
        Assert.Equal(DbcTableRowKind.Message, model.GetRow(3).Kind);
        Assert.Equal("A", model.GetRow(1).Signal!.Name);
        Assert.Equal("B", model.GetRow(2).Signal!.Name);
        Assert.Equal(new[] { 0, 1, 2, 3 }, model.EnumerateRows().Select(row => row.Index));

        model.ToggleExpanded(first);

        Assert.False(model.IsExpanded(first));
        Assert.Equal(2, model.RowCount);
    }

    [Fact]
    public void ExpandAll_and_CollapseAll_cover_every_message()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        model.ExpandAll();

        Assert.True(model.AllMessagesExpanded);
        Assert.Equal(5, model.RowCount);
        Assert.Equal(3, model.EnumerateRows().Count(row => row.Kind == DbcTableRowKind.Signal));

        model.CollapseAll();

        Assert.False(model.HasExpandedMessages);
        Assert.Equal(2, model.RowCount);
    }

    [Fact]
    public void Open_resets_expansion_state()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.ExpandAll();

        model.Open(entry);

        Assert.Equal(2, model.RowCount);
        Assert.False(model.HasExpandedMessages);
    }

    [Fact]
    public void Save_preserves_expansion_for_messages_that_still_exist()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", MultiMessageDbc);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.ToggleExpanded(model.Document!.Messages[0]);

        model.EditedContent = MultiMessageDbc + "\nBO_ 300 Third: 1 XXX\n";
        var outcome = model.Save();

        Assert.True(outcome.Saved);
        Assert.True(model.IsExpanded(model.Document!.Messages[0]));
        Assert.False(model.IsExpanded(model.Document!.Messages[1]));
        Assert.Equal(5, model.RowCount);
    }

    [Fact]
    public void Invalid_document_has_no_table_rows()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "bad.dbc", "not a dbc");
        var model = new DbcEditorModel(workspace);

        model.Open(entry);

        Assert.True(model.ParseResult!.HasErrors);
        Assert.Equal(0, model.RowCount);
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

public sealed class DbcStructuredSessionTests
{
    private const string Content =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    [Fact]
    public void Session_tracks_dirty_text_and_reverts_to_the_open_baseline()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "session.dbc", Content);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        Assert.False(model.IsTextDirty);
        Assert.False(model.HasSessionEdits);

        model.EditedContent = "unsaved";
        Assert.True(model.IsTextDirty);
        Assert.True(model.HasSessionEdits);

        Assert.True(model.RevertAllEdits());

        Assert.False(model.IsTextDirty);
        Assert.False(model.HasSessionEdits);
        Assert.Equal(Content, model.EditedContent);
        Assert.Equal(Content, workspace.Find(entry.Id)?.Content);
    }

    [Fact]
    public void Reload_keeps_the_session_baseline_and_tracks_applied_edits()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "reload.dbc", Content);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        var outcome = workspace.ApplyStructuredEdit(
            entry.Id,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.File,
                CommentText: "applied"));
        Assert.True(outcome.Success, outcome.Error);

        model.Reload(workspace.Find(entry.Id)!);

        Assert.False(model.IsTextDirty);
        Assert.True(model.HasSessionEdits);
        Assert.Contains("CM_ \"applied\";", model.EditedContent, StringComparison.Ordinal);
        Assert.True(model.RevertAllEdits());
        Assert.Equal(Content, model.EditedContent);
    }

    [Fact]
    public void Download_metadata_uses_reviewed_name_and_encoding_notice()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile(
            "can-a",
            "legacy.dbc",
            Content,
            new DbcFileEncoding("Windows-1252", true, 2));
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        Assert.Equal("legacy.dbc.reviewed.dbc", model.DownloadFileName);
        Assert.Contains(
            "Windows-1252 (fallback)",
            model.DownloadEncodingNotice,
            StringComparison.Ordinal);
        Assert.Contains("UTF-8", model.DownloadEncodingNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void Download_validation_reparses_the_current_text_buffer()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "download.dbc", Content);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);
        model.EditedContent = "not a dbc";

        var parse = model.ValidateDownload();

        Assert.True(parse.HasErrors);
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
