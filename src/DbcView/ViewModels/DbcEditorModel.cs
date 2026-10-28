using DbcView.Application;
using DbcView.Domain;

namespace DbcView.ViewModels;

/// <summary>
/// PanelModel for the DBC editor document. Tracks the open file's raw text
/// and parse result and supports text/table view switching. Saving always
/// writes the raw draft back through the application workspace and exposes the
/// resulting diagnostics; invalid drafts are stored explicitly as unvalidated
/// content instead of being rejected or accepted silently.
/// </summary>
public sealed class DbcEditorModel
{
    public enum ViewMode
    {
        Text,
        Table,
    }

    private readonly DbcFileWorkspace _workspace;

    public DbcEditorModel(DbcFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public DbcDocumentId? DocumentId { get; private set; }

    public string? RawContent { get; private set; }

    /// <summary>
    /// Writable edit buffer bound by the editor panel. Initialized from the
    /// opened file's content and only replaced when the model opens another
    /// file or saves, so unrelated host re-renders cannot discard unsaved
    /// input (FR-08).
    /// </summary>
    public string EditedContent { get; set; } = string.Empty;

    public DbcParseResult? ParseResult { get; private set; }

    public DbcDocument? Document => ParseResult?.Document;

    public ViewMode Mode { get; private set; } = ViewMode.Table;

    /// <summary>Set after a successful save; holds the diagnostics of the saved draft.</summary>
    public DbcParseResult? LastSaveResult { get; private set; }

    /// <summary>Set when the save operation itself failed (for example, the file disappeared).</summary>
    public string? SaveError { get; private set; }

    public event Action? StateChanged;

    public void Open(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        DocumentId = file.Id;
        RawContent = file.Content;
        EditedContent = file.Content;
        ParseResult = file.ParseResult;
        LastSaveResult = null;
        SaveError = null;
        Notify();
    }

    public void SetMode(ViewMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        Notify();
    }

    public DbcSaveOutcome Save()
    {
        if (DocumentId is null)
        {
            SaveError = "Cannot save without an open file.";
            Notify();
            return new DbcSaveOutcome(false, null, SaveError);
        }

        try
        {
            var updated = _workspace.Save(DocumentId.Value, EditedContent);
            RawContent = updated.Content;
            ParseResult = updated.ParseResult;
            LastSaveResult = updated.ParseResult;
            SaveError = null;
            Notify();
            return new DbcSaveOutcome(true, updated.ParseResult, null);
        }
        catch (DbcFileNotFoundException ex)
        {
            SaveError = ex.Message;
            Notify();
            return new DbcSaveOutcome(false, null, ex.Message);
        }
    }

    private void Notify()
    {
        StateChanged?.Invoke();
    }
}

/// <summary>Outcome of an editor save attempt.</summary>
public sealed record DbcSaveOutcome(bool Saved, DbcParseResult? ParseResult, string? Error);
