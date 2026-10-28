#nullable enable
using Bunit;
using DbcView.Application;
using DbcView.Domain;
using DbcView.Web;
using DbcView.Web.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DbcView.Tests;

public sealed class InMemoryWorkspacePersistence : IWorkspacePersistence
{
    public DbcWorkspaceSnapshot? Snapshot { get; private set; }

    public int SaveCount { get; private set; }

    public int ClearCount { get; private set; }

    public string? SaveError { get; set; }

    public Task<DbcPersistenceLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            Snapshot is null
                ? DbcPersistenceLoadResult.None()
                : DbcPersistenceLoadResult.Restored(Snapshot));
    }

    public Task<DbcPersistenceResult> SaveAsync(
        DbcWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        SaveCount++;
        if (SaveError is not null)
        {
            return Task.FromResult(DbcPersistenceResult.Fail(SaveError));
        }

        Snapshot = snapshot;
        return Task.FromResult(DbcPersistenceResult.Ok());
    }

    public Task<DbcPersistenceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        Snapshot = null;
        return Task.FromResult(DbcPersistenceResult.Ok());
    }
}

public sealed class DbcPersistenceTests : BunitContext
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    private readonly InMemoryWorkspacePersistence _persistence = new();

    public DbcPersistenceTests()
    {
        Services.AddDbcViewServices();
        Services.RemoveAll<IWorkspacePersistence>();
        Services.AddSingleton<IWorkspacePersistence>(_persistence);
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public async Task Enabling_persistence_saves_a_snapshot_without_documents()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.Find("[data-testid='dbc-persistence']").Click();
        Assert.NotNull(cut.Find("[data-testid='dbc-persistence-dialog']"));
        cut.Find("[data-testid='dbc-persistence-enable']").Click();

        await cut.InvokeAsync(() => { });
        Assert.True(_persistence.SaveCount >= 1);
        Assert.NotNull(_persistence.Snapshot);
        Assert.Equal(DbcWorkspaceSnapshot.CurrentSchemaVersion, _persistence.Snapshot!.SchemaVersion);
        Assert.Empty(_persistence.Snapshot.Files);
        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-persistence-status']")));
    }

    [Fact]
    public async Task Forget_clears_the_saved_snapshot_and_hides_the_status()
    {
        var cut = Render<DbcViewWorkspace>();
        cut.Find("[data-testid='dbc-persistence']").Click();
        cut.Find("[data-testid='dbc-persistence-enable']").Click();
        await cut.InvokeAsync(() => { });
        Assert.NotNull(_persistence.Snapshot);

        cut.Find("[data-testid='dbc-persistence']").Click();
        await cut.Find("[data-testid='dbc-persistence-forget']")
            .ClickAsync(new MouseEventArgs());

        Assert.Equal(1, _persistence.ClearCount);
        Assert.Null(_persistence.Snapshot);
        cut.WaitForAssertion(
            () => Assert.Empty(cut.FindAll("[data-testid='dbc-persistence-status']")));
        Assert.Contains(
            "removed",
            cut.Find("[data-testid='dbc-persistence-notice']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_includes_folders_files_open_tabs_and_view_modes()
    {
        var cut = Render<DbcViewWorkspace>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "saved.dbc"));
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        TreeTestHelpers.ClickFile(cut);
        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-mode-text']")));
        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-table']")));
        cut.Find("[data-testid='dbc-mode-text']").Click();
        cut.WaitForAssertion(
            () => Assert.Equal(
                "true",
                cut.Find("[data-testid='dbc-mode-text']").GetAttribute("aria-pressed")));

        cut.Find("[data-testid='dbc-persistence']").Click();
        cut.Find("[data-testid='dbc-persistence-enable']").Click();
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() => Assert.NotNull(_persistence.Snapshot));
        var snapshot = _persistence.Snapshot!;
        var file = Assert.Single(snapshot.Files);
        Assert.Equal("DBC", file.Folder);
        Assert.Equal("saved.dbc", file.FileName);
        Assert.Equal("Text", file.ViewMode);
        var open = Assert.Single(snapshot.OpenDocuments);
        Assert.Equal("saved.dbc", open.FileName);
        Assert.NotNull(snapshot.ActiveDocument);
        Assert.Equal("saved.dbc", snapshot.ActiveDocument!.FileName);
    }

    [Fact]
    public async Task Unsaved_text_drafts_are_not_written_to_the_snapshot()
    {
        var cut = Render<DbcViewWorkspace>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "draft.dbc"));
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        TreeTestHelpers.ClickFile(cut);
        cut.WaitForAssertion(
            () => Assert.NotNull(cut.Find("[data-testid='dbc-mode-text']")));
        cut.Find("[data-testid='dbc-mode-text']").Click();
        cut.Find("textarea").Change("unsaved draft");

        cut.Find("[data-testid='dbc-persistence']").Click();
        cut.Find("[data-testid='dbc-persistence-enable']").Click();
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() => Assert.NotNull(_persistence.Snapshot));
        var file = Assert.Single(_persistence.Snapshot!.Files);
        Assert.Equal(ValidDbc, file.Content);
        Assert.DoesNotContain("unsaved draft", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_failure_is_visible_in_the_status_and_dialog()
    {
        _persistence.SaveError = "Quota exceeded.";
        var cut = Render<DbcViewWorkspace>();

        cut.Find("[data-testid='dbc-persistence']").Click();
        cut.Find("[data-testid='dbc-persistence-enable']").Click();
        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(
            () => Assert.Contains(
                "Session storage error",
                cut.Find("[data-testid='dbc-persistence-status']").TextContent,
                StringComparison.Ordinal));
        Assert.Contains(
            "Quota exceeded.",
            cut.Find("[data-testid='dbc-persistence-notice']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Persistence_is_off_and_not_shown_before_enabling()
    {
        var cut = Render<DbcViewWorkspace>();

        Assert.Empty(cut.FindAll("[data-testid='dbc-persistence-status']"));
        var button = cut.Find("[data-testid='dbc-persistence']");
        Assert.Equal("false", button.GetAttribute("aria-pressed"));
        Assert.Equal(0, _persistence.SaveCount);
    }
}
