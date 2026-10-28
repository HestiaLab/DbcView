using DbcView.Domain;

namespace DbcView.Web.ViewModels;

public sealed record DbcEditorNavigationRequest(
    DbcDocumentId DocumentId,
    uint? MessageId = null,
    string? SignalName = null,
    int? SourceLine = null,
    bool Reload = false)
{
    public static DbcEditorNavigationRequest ForSource(
        DbcDocumentId documentId,
        int lineNumber)
    {
        return new DbcEditorNavigationRequest(documentId, SourceLine: lineNumber);
    }

    public static DbcEditorNavigationRequest ForReload(DbcDocumentId documentId)
    {
        return new DbcEditorNavigationRequest(documentId, Reload: true);
    }
}

/// <summary>
/// Workspace-level navigation handoff from the command palette to the active
/// editor panel. The request is kept pending until a matching panel consumes
/// it, so opening a tab and requesting a jump in the same tick still works.
/// </summary>
public sealed class DbcEditorNavigation
{
    public event Action<DbcEditorNavigationRequest>? Requested;

    public DbcEditorNavigationRequest? Pending { get; private set; }

    public void Request(DbcEditorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Pending = request;
        Requested?.Invoke(request);
    }

    public void Clear(DbcEditorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Pending == request)
        {
            Pending = null;
        }
    }
}
