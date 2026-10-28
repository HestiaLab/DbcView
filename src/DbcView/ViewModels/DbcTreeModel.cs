using DbcView.Application;
using DbcView.Domain;

namespace DbcView.ViewModels;

/// <summary>
/// PanelModel for the DBC Explorer tree. Owns folder/file selection and
/// exposes upload/create operations backed by the application workspace.
/// Raises <see cref="StateChanged"/> when the selection or the underlying
/// workspace changes.
/// </summary>
public sealed class DbcTreeModel : IDisposable
{
    private readonly DbcFileWorkspace _workspace;
    private bool _disposed;

    public DbcTreeModel(DbcFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _workspace.Changed += OnWorkspaceChanged;
    }

    public IReadOnlyList<string> Folders => _workspace.Folders;

    /// <summary>Folder that receives the next upload.</summary>
    public string? SelectedFolder { get; private set; }

    /// <summary>Fully-qualified document selection (folder + file name).</summary>
    public DbcDocumentId? SelectedDocument { get; private set; }

    public event Action? StateChanged;

    /// <summary>
    /// Raised only when the user selects a document. Folder selection and
    /// workspace mutations (upload/save) must not re-open or re-activate tabs.
    /// </summary>
    public event Action? DocumentSelectionChanged;

    public void SelectFolder(string? folder)
    {
        SelectedFolder = folder;
        StateChanged?.Invoke();
    }

    public void Select(DbcDocumentId document)
    {
        SelectedDocument = document;
        SelectedFolder = document.Folder;
        StateChanged?.Invoke();
        DocumentSelectionChanged?.Invoke();
    }

    public void NewFolder(string name)
    {
        _workspace.CreateFolder(name);
        SelectedFolder = name;
    }

    public DbcFileEntry Upload(DbcDocumentId id, string content)
    {
        return _workspace.Upload(id, content);
    }

    public IReadOnlyList<DbcFileEntry> FilesFor(string folder)
    {
        return _workspace.FilesIn(folder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _workspace.Changed -= OnWorkspaceChanged;
        _disposed = true;
    }

    private void OnWorkspaceChanged()
    {
        StateChanged?.Invoke();
    }
}
