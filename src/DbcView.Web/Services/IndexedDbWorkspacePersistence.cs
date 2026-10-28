using System.Text.Json;
using DbcView.Application;
using Microsoft.JSInterop;

namespace DbcView.Web.Services;

/// <summary>IndexedDB-backed local session persistence. Never contacts a server.</summary>
public sealed class IndexedDbWorkspacePersistence : IWorkspacePersistence
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public IndexedDbWorkspacePersistence(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    public async Task<DbcPersistenceLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await GetModuleAsync(cancellationToken);
            var json = await module.InvokeAsync<string?>(
                "load",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return DbcPersistenceLoadResult.None();
            }

            var snapshot = JsonSerializer.Deserialize<DbcWorkspaceSnapshot>(
                json,
                JsonOptions);
            if (snapshot is null
                || snapshot.SchemaVersion != DbcWorkspaceSnapshot.CurrentSchemaVersion
                || snapshot.Folders is null
                || snapshot.Files is null
                || snapshot.OpenDocuments is null
                || snapshot.Files.Any(file => string.IsNullOrWhiteSpace(file.Folder)
                    || string.IsNullOrWhiteSpace(file.FileName)
                    || file.Content is null))
            {
                return DbcPersistenceLoadResult.Incompatible(
                    "The saved session is corrupt or uses an unsupported schema. It was discarded.");
            }

            return DbcPersistenceLoadResult.Restored(snapshot);
        }
        catch (JsonException)
        {
            return DbcPersistenceLoadResult.Incompatible(
                "The saved session is corrupt and could not be read. It was discarded.");
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return new DbcPersistenceLoadResult(
                false,
                false,
                $"The saved session could not be read: {ex.Message}",
                null);
        }
    }

    public async Task<DbcPersistenceResult> SaveAsync(
        DbcWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await GetModuleAsync(cancellationToken);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await module.InvokeVoidAsync("save", cancellationToken, json);
            return DbcPersistenceResult.Ok();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return DbcPersistenceResult.Fail(
                $"The session could not be saved locally: {ex.Message}");
        }
    }

    public async Task<DbcPersistenceResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await GetModuleAsync(cancellationToken);
            await module.InvokeVoidAsync("clear", cancellationToken);
            return DbcPersistenceResult.Ok();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return DbcPersistenceResult.Fail(
                $"The saved session could not be cleared: {ex.Message}");
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        return _module ??= await _js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./js/dbc-session.js");
    }
}
