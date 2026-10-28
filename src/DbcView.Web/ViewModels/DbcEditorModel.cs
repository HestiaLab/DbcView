using System.Globalization;
using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Web.ViewModels;

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
    private readonly HashSet<uint> _expandedMessageIds = new();
    private string? _sessionBaselineContent;
    private bool _expandAll;
    private int[] _messageStarts = Array.Empty<int>();
    private int _rowCount;
    private int _messageCount;
    private int _signalCount;
    private bool _allMessagesExpanded;
    private bool _hasExpandedMessages;

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

    public bool IsTextDirty => !string.Equals(EditedContent, RawContent, StringComparison.Ordinal);

    public bool HasSessionEdits => DocumentId is not null
        && (_sessionBaselineContent is null
            || !string.Equals(RawContent, _sessionBaselineContent, StringComparison.Ordinal)
            || IsTextDirty);

    public string? DownloadFileName => DocumentId is null
        ? null
        : DocumentId.Value.FileName + ".reviewed.dbc";

    public string? DownloadEncodingNotice
    {
        get
        {
            var encoding = DocumentId is { } id ? _workspace.Find(id)?.Encoding : null;
            if (encoding is null || string.Equals(encoding.Name, "UTF-8", StringComparison.Ordinal))
            {
                return null;
            }

            return $"Source encoding {encoding.DisplayName} will be converted to UTF-8 without BOM.";
        }
    }

    /// <summary>Total flattened message/signal rows for the current expansion state.</summary>
    public int RowCount => _rowCount;

    public int MessageCount => _messageCount;

    public int SignalCount => _signalCount;

    public bool AllMessagesExpanded => _allMessagesExpanded;

    public bool HasExpandedMessages => _hasExpandedMessages;

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
        _sessionBaselineContent = file.Content;
        LastSaveResult = null;
        SaveError = null;
        _expandAll = false;
        _expandedMessageIds.Clear();
        RebuildLayout();
        Notify();
    }

    public void Reload(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        DocumentId = file.Id;
        RawContent = file.Content;
        EditedContent = file.Content;
        ParseResult = file.ParseResult;
        LastSaveResult = null;
        SaveError = null;
        RebuildLayout();
        Notify();
    }

    public bool RevertAllEdits()
    {
        if (DocumentId is not { } id || _sessionBaselineContent is null)
        {
            return false;
        }

        try
        {
            var updated = _workspace.Save(id, _sessionBaselineContent);
            RawContent = updated.Content;
            EditedContent = updated.Content;
            ParseResult = updated.ParseResult;
            LastSaveResult = null;
            SaveError = null;
            RebuildLayout();
            Notify();
            return true;
        }
        catch (DbcFileNotFoundException ex)
        {
            SaveError = ex.Message;
            Notify();
            return false;
        }
    }

    public DbcParseResult ValidateDownload()
    {
        return _workspace.Parse(EditedContent);
    }

    public bool IsExpanded(DbcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _expandAll || _expandedMessageIds.Contains(message.Id);
    }

    public void ToggleExpanded(DbcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_expandAll)
        {
            _expandAll = false;
            if (Document is { } document)
            {
                foreach (var candidate in document.Messages)
                {
                    if (candidate.Id != message.Id)
                    {
                        _expandedMessageIds.Add(candidate.Id);
                    }
                }
            }
        }
        else if (!_expandedMessageIds.Remove(message.Id))
        {
            _expandedMessageIds.Add(message.Id);
        }

        RebuildLayout();
        Notify();
    }

    public void ExpandAll()
    {
        if (Document is not { Messages.Count: > 0 } document || _allMessagesExpanded)
        {
            return;
        }

        _expandAll = true;
        _expandedMessageIds.Clear();
        RebuildLayout();
        Notify();
    }

    public void ExpandMessage(DbcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_expandAll || _expandedMessageIds.Contains(message.Id))
        {
            return;
        }

        _expandedMessageIds.Add(message.Id);
        RebuildLayout();
        Notify();
    }

    public void CollapseAll()
    {
        if (!_hasExpandedMessages)
        {
            return;
        }

        _expandAll = false;
        _expandedMessageIds.Clear();
        RebuildLayout();
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
            RebuildLayout();
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

    /// <summary>Materializes one flattened row without building the full row list.</summary>
    public DbcTableRow GetRow(int index)
    {
        if (index < 0 || index >= _rowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var messages = ParseResult!.Document.Messages;
        var low = 0;
        var high = _messageStarts.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_messageStarts[middle] <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var messageIndex = low;
        var message = messages[messageIndex];
        var offset = index - _messageStarts[messageIndex];
        if (offset == 0)
        {
            return new DbcTableRow(
                index,
                DbcTableRowKind.Message,
                message,
                messageIndex,
                null,
                -1,
                string.Create(CultureInfo.InvariantCulture, $"m:{messageIndex}"),
                IsExpanded(message));
        }

        var signalIndex = offset - 1;
        return new DbcTableRow(
            index,
            DbcTableRowKind.Signal,
            message,
            messageIndex,
            message.Signals[signalIndex],
            signalIndex,
            string.Create(CultureInfo.InvariantCulture, $"s:{messageIndex}:{signalIndex}"),
            true);
    }

    public IEnumerable<DbcTableRow> EnumerateRows()
    {
        for (var index = 0; index < _rowCount; index++)
        {
            yield return GetRow(index);
        }
    }

    /// <summary>Resolves a row key to its current flat index without scanning the row list.</summary>
    public bool TryGetRowIndex(string? key, out int index)
    {
        index = -1;
        if (string.IsNullOrEmpty(key) || ParseResult is not { HasErrors: false } parseResult)
        {
            return false;
        }

        var messages = parseResult.Document.Messages;
        if (key.StartsWith("m:", StringComparison.Ordinal))
        {
            if (!int.TryParse(
                    key.AsSpan(2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var messageIndex)
                || messageIndex < 0
                || messageIndex >= messages.Count)
            {
                return false;
            }

            index = _messageStarts[messageIndex];
            return true;
        }

        if (!key.StartsWith("s:", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = key.IndexOf(':', 2);
        if (separator < 0
            || !int.TryParse(
                key.AsSpan(2, separator - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var signalMessageIndex)
            || !int.TryParse(
                key.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var signalIndex)
            || signalMessageIndex < 0
            || signalMessageIndex >= messages.Count)
        {
            return false;
        }

        var message = messages[signalMessageIndex];
        if (!IsExpanded(message)
            || signalIndex < 0
            || signalIndex >= message.Signals.Count)
        {
            return false;
        }

        index = _messageStarts[signalMessageIndex] + 1 + signalIndex;
        return true;
    }

    private void RebuildLayout()
    {
        _rowCount = 0;
        _messageCount = 0;
        _signalCount = 0;
        _allMessagesExpanded = false;
        _hasExpandedMessages = false;

        if (ParseResult is not { HasErrors: false } parseResult)
        {
            _messageStarts = Array.Empty<int>();
            return;
        }

        var messages = parseResult.Document.Messages;
        if (_messageStarts.Length != messages.Count)
        {
            _messageStarts = new int[messages.Count];
        }

        var rowCount = 0;
        var signalCount = 0;
        var expandedCount = 0;
        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            _messageStarts[messageIndex] = rowCount;
            signalCount += message.Signals.Count;

            if (_expandAll || _expandedMessageIds.Contains(message.Id))
            {
                expandedCount++;
                rowCount += 1 + message.Signals.Count;
            }
            else
            {
                rowCount++;
            }
        }

        _rowCount = rowCount;
        _messageCount = messages.Count;
        _signalCount = signalCount;
        _allMessagesExpanded = messages.Count > 0 && expandedCount == messages.Count;
        _hasExpandedMessages = expandedCount > 0;
    }


    private void Notify()
    {
        StateChanged?.Invoke();
    }

}

/// <summary>Outcome of an editor save attempt.</summary>
public sealed record DbcSaveOutcome(bool Saved, DbcParseResult? ParseResult, string? Error);
