using DbcView.Domain;

namespace DbcView.Web.ViewModels;

/// <summary>
/// Tracks the editor model for each open document so workspace-level actions
/// can disable semantic editing while the Text buffer has unsaved changes.
/// </summary>
public sealed class DbcEditorSessionRegistry
{
    private readonly Dictionary<DbcDocumentId, DbcEditorModel> _models = new();
    private Dictionary<DbcDocumentId, DbcEditorModel.ViewMode> _restoredModes = new();

    public event Action? Changed;

    public void SetRestoredModes(
        IReadOnlyDictionary<DbcDocumentId, DbcEditorModel.ViewMode> modes)
    {
        _restoredModes = new Dictionary<DbcDocumentId, DbcEditorModel.ViewMode>(modes);
    }

    public bool TryTakeRestoredMode(
        DbcDocumentId documentId,
        out DbcEditorModel.ViewMode mode)
    {
        if (_restoredModes.TryGetValue(documentId, out mode))
        {
            _restoredModes.Remove(documentId);
            return true;
        }

        return false;
    }

    public void Register(DbcEditorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.DocumentId is not { } documentId)
        {
            return;
        }

        foreach (var pair in _models.ToArray())
        {
            if (ReferenceEquals(pair.Value, model) && pair.Key != documentId)
            {
                _models.Remove(pair.Key);
            }
        }

        _models[documentId] = model;
        Changed?.Invoke();
    }

    public void Unregister(DbcEditorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (var pair in _models.ToArray())
        {
            if (ReferenceEquals(pair.Value, model))
            {
                _models.Remove(pair.Key);
            }
        }

        Changed?.Invoke();
    }

    public DbcEditorModel? Find(DbcDocumentId documentId)
    {
        return _models.TryGetValue(documentId, out var model) ? model : null;
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
