#nullable enable
using DbcView.Application;
using DbcView.Domain;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcFileWorkspaceTests
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    [Fact]
    public void CreateFolder_adds_folder_and_raises_changed()
    {
        using var workspace = TestWorkspace.Create();
        var changed = 0;
        workspace.Changed += () => changed++;

        workspace.CreateFolder("can-a");

        Assert.Single(workspace.Folders);
        Assert.Equal("can-a", workspace.Folders[0]);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void CreateFolder_rejects_duplicate_name()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateFolder("can-a");

        Assert.Throws<DbcFileConflictException>(() => workspace.CreateFolder("can-a"));
        Assert.Single(workspace.Folders);
    }

    [Fact]
    public void Upload_parses_content_and_lists_files_sorted()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateFolder("can-a");

        workspace.Upload(new DbcDocumentId("can-a", "z.dbc"), ValidDbc);
        workspace.Upload(new DbcDocumentId("can-a", "a.dbc"), ValidDbc);

        var files = workspace.FilesIn("can-a");
        Assert.Equal(new[] { "a.dbc", "z.dbc" }, files.Select(f => f.Id.FileName));
        Assert.All(files, file => Assert.True(file.ParseResult.IsValid));
        Assert.Single(files[0].Document.Messages);
    }

    [Fact]
    public void Upload_keeps_invalid_content_as_unvalidated_draft()
    {
        const string invalid = "this is not a DBC file";

        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "draft.dbc", invalid);

        Assert.True(entry.HasParseErrors);
        var file = workspace.Find(entry.Id);
        Assert.NotNull(file);
        Assert.Equal(invalid, file!.Content);
        Assert.Empty(file.Document.Messages);
    }

    [Fact]
    public void Upload_rejects_duplicate_document_in_same_folder()
    {
        using var workspace = TestWorkspace.Create();
        workspace.AddFile("can-a", "a.dbc", ValidDbc);

        Assert.Throws<DbcFileConflictException>(
            () => workspace.Upload(new DbcDocumentId("can-a", "a.dbc"), ValidDbc));
    }

    [Fact]
    public void Upload_rejects_missing_folder()
    {
        using var workspace = TestWorkspace.Create();

        Assert.Throws<DbcFileConflictException>(
            () => workspace.Upload(new DbcDocumentId("missing", "a.dbc"), ValidDbc));
    }

    [Fact]
    public void Save_reparses_and_replaces_content()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);

        var longer =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n\nBO_ 200 SecondMsg: 2 XXX\n";
        var updated = workspace.Save(entry.Id, longer);

        Assert.Equal(2, updated.Document.Messages.Count);
        Assert.Equal(longer, updated.Content);
        Assert.Same(updated, workspace.Find(entry.Id));
    }

    [Fact]
    public void Save_keeps_invalid_draft_and_exposes_diagnostics()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);

        var updated = workspace.Save(entry.Id, "arbitrary");

        Assert.True(updated.HasParseErrors);
        Assert.Equal("arbitrary", workspace.Find(entry.Id)!.Content);
    }

    [Fact]
    public void Save_missing_document_throws()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);
        workspace.Rename(entry.Id, "renamed.dbc");

        Assert.Throws<DbcFileNotFoundException>(() => workspace.Save(entry.Id, ValidDbc));
    }

    [Fact]
    public void Rename_moves_document_and_preserves_content()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "a.dbc", ValidDbc);

        workspace.Rename(entry.Id, "renamed.dbc");

        Assert.Null(workspace.Find(new DbcDocumentId("can-a", "a.dbc")));
        var renamed = workspace.Find(new DbcDocumentId("can-a", "renamed.dbc"));
        Assert.NotNull(renamed);
        Assert.Equal(ValidDbc, renamed!.Content);
    }

    [Fact]
    public void Find_returns_null_for_missing_folder_or_file()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateFolder("can-a");

        Assert.Null(workspace.Find(new DbcDocumentId("can-a", "missing.dbc")));
        Assert.Null(workspace.Find(new DbcDocumentId("missing-folder", "a.dbc")));
        Assert.Empty(workspace.FilesIn("missing-folder"));
    }

    [Fact]
    public void Same_file_name_in_different_folders_keeps_independent_state()
    {
        const string contentA = "VERSION \"A\"\nBU_: XXX\n";
        const string contentB = "VERSION \"B\"\nBU_: XXX\n";

        using var workspace = TestWorkspace.Create();
        var entryA = workspace.AddFile("A", "x.dbc", contentA);
        var entryB = workspace.AddFile("B", "x.dbc", contentB);

        Assert.NotEqual(entryA.Id, entryB.Id);
        Assert.Equal("A/x.dbc", entryA.Id.Key);
        Assert.Equal("B/x.dbc", entryB.Id.Key);

        var savedA = workspace.Save(entryA.Id, "VERSION \"A2\"\nBU_: XXX\n");

        Assert.Equal(contentB, workspace.Find(entryB.Id)!.Content);
        Assert.Equal("A2", savedA.Document.Version);
    }

    [Fact]
    public void Duplicate_file_name_is_only_rejected_inside_the_same_folder()
    {
        using var workspace = TestWorkspace.Create();

        workspace.AddFile("A", "x.dbc", ValidDbc);
        var other = workspace.AddFile("B", "x.dbc", ValidDbc);

        Assert.Equal("B", other.Id.Folder);
    }
}

public sealed class DbcDocumentIdTests
{
    [Fact]
    public void Key_combines_folder_and_file_name()
    {
        var id = new DbcDocumentId("A", "x.dbc");

        Assert.Equal("A/x.dbc", id.Key);
        Assert.Equal("A/x.dbc", id.ToString());
    }

    [Fact]
    public void Equality_includes_folder()
    {
        var a = new DbcDocumentId("A", "x.dbc");
        var b = new DbcDocumentId("B", "x.dbc");

        Assert.NotEqual(a, b);
        Assert.Equal(a, new DbcDocumentId("A", "x.dbc"));
    }

    [Fact]
    public void TryParseKey_round_trips()
    {
        var original = new DbcDocumentId("folder", "file.dbc");

        Assert.True(DbcDocumentId.TryParseKey(original.Key, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("/file.dbc")]
    [InlineData("folder/")]
    [InlineData("a/b/file.dbc")]
    public void TryParseKey_rejects_invalid_keys(string? key)
    {
        Assert.False(DbcDocumentId.TryParseKey(key, out _));
    }

    [Fact]
    public void Constructor_rejects_path_separators()
    {
        Assert.Throws<ArgumentException>(() => new DbcDocumentId("a/b", "x.dbc"));
        Assert.Throws<ArgumentException>(() => new DbcDocumentId("A", "b/x.dbc"));
        Assert.Throws<ArgumentException>(() => new DbcDocumentId(" ", "x.dbc"));
        Assert.Throws<ArgumentException>(() => new DbcDocumentId("A", " "));
    }
}
