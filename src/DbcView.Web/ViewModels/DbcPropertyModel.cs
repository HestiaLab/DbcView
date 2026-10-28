using System.Globalization;
using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Web.ViewModels;

/// <summary>
/// PanelModel for the DBC Properties panel. Holds the General fields plus the
/// read-only semantic sections projected from the active document.
/// </summary>
public sealed class DbcPropertyModel
{
    private DbcFileEntry? _file;
    private DbcMessage? _message;
    private DbcSignal? _signal;
    private int? _highlightedCommentLine;
    private DbcPropertySections _sections =
        DbcPropertySections.Unavailable("Open a file to inspect its semantics.");

    public string? TargetName { get; private set; }

    public string? TargetType { get; private set; }

    public DbcDocumentId? DocumentId => _file?.Id;

    public uint? MessageId => _message?.Id;

    public string? SignalName => _signal?.Name;

    public DbcSemanticScope TargetScope => _signal is not null
        ? DbcSemanticScope.Signal
        : _message is not null
            ? DbcSemanticScope.Message
            : DbcSemanticScope.File;

    public string? FileKey { get; private set; }

    public string? Encoding { get; private set; }

    public int UndefinedByteCount { get; private set; }

    public int FileMessageCount { get; private set; }

    public int FileSignalCount { get; private set; }

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public string? Id { get; private set; }

    public string? IdHex { get; private set; }

    public string? Name { get; private set; }

    public string? Length { get; private set; }

    public string? Transmitter { get; private set; }

    public string? StartBit { get; private set; }

    public string? Factor { get; private set; }

    public string? Offset { get; private set; }

    public string? Min { get; private set; }

    public string? Max { get; private set; }

    public string? Unit { get; private set; }

    public string? Receiver { get; private set; }

    public string? ByteOrder { get; private set; }

    public string? Signed { get; private set; }

    public string? Multiplexer { get; private set; }

    public string? ParentMessage { get; private set; }

    public bool SemanticsAvailable => _sections.Available;

    public string? SemanticsUnavailableReason => _sections.UnavailableReason;

    public IReadOnlyList<DbcPropertyComment> Comments => _sections.Comments;

    public IReadOnlyList<DbcPropertyAttribute> Attributes => _sections.Attributes;

    public DbcPropertyValueTable? ValueTable => _sections.ValueTable;

    public bool HasExtendedMultiplexing => _sections.HasExtendedMultiplexing;

    public IReadOnlyList<DbcPropertyStatementGroup> NotDisplayed => _sections.NotDisplayed;

    public IReadOnlyList<DbcPropertyStatementGroup> NotInterpreted => _sections.NotInterpreted;

    public event Action? StateChanged;

    public void ShowFile(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
        _message = null;
        _signal = null;
        _highlightedCommentLine = null;
        ResetFields();

        TargetType = "File";
        TargetName = file.Id.Key;
        FileKey = file.Id.Key;
        Encoding = file.Encoding?.DisplayName;
        UndefinedByteCount = file.Encoding?.UndefinedByteCount ?? 0;
        Name = file.Id.Key;
        FileMessageCount = file.Document.Messages.Count;
        FileSignalCount = file.Document.Messages.Sum(message => message.Signals.Count);
        ErrorCount = file.ParseResult.Diagnostics.Count(
            diagnostic => diagnostic.Severity == DbcDiagnosticSeverity.Error);
        WarningCount = file.ParseResult.Diagnostics.Count(
            diagnostic => diagnostic.Severity == DbcDiagnosticSeverity.Warning);
        _sections = DbcSemanticPropertyBuilder.Build(file, null, null);
        Notify();
    }

    public void ShowMessage(DbcMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        _message = msg;
        _signal = null;
        _highlightedCommentLine = null;
        ResetFields();

        TargetType = "Message";
        TargetName = msg.Name;
        FileKey = _file?.Id.Key;
        Id = msg.Id.ToString(CultureInfo.InvariantCulture);
        IdHex = string.Create(CultureInfo.InvariantCulture, $"0x{msg.Id:X}");
        Name = msg.Name;
        Length = msg.Length.ToString(CultureInfo.InvariantCulture);
        Transmitter = msg.Transmitter;
        _sections = DbcSemanticPropertyBuilder.Build(_file, msg, null);
        Notify();
    }

    public void ShowSignal(DbcSignal sig)
    {
        ArgumentNullException.ThrowIfNull(sig);

        _signal = sig;
        _highlightedCommentLine = null;
        _message = _file?.Document.Messages.FirstOrDefault(
            message => message.Signals.Contains(sig));
        ResetFields();

        TargetType = "Signal";
        TargetName = sig.Name;
        FileKey = _file?.Id.Key;
        Name = sig.Name;
        StartBit = sig.StartBit.ToString(CultureInfo.InvariantCulture);
        Length = sig.Length.ToString(CultureInfo.InvariantCulture);
        Factor = FormatNumber(sig.Factor);
        Offset = FormatNumber(sig.Offset);
        Min = FormatNumber(sig.Min);
        Max = FormatNumber(sig.Max);
        Unit = string.IsNullOrEmpty(sig.Unit) ? "(none)" : sig.Unit;
        Receiver = sig.Receiver;
        ByteOrder = sig.IsBigEndian ? "Motorola (big-endian)" : "Intel (little-endian)";
        Signed = sig.IsSigned ? "Yes" : "No";
        Multiplexer = sig.Multiplexer ?? "None";
        ParentMessage = _message is null
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{_message.Name} (0x{_message.Id:X})");

        _sections = _file is null
            ? DbcPropertySections.Unavailable("Open a file to inspect signal semantics.")
            : _message is null
                ? DbcPropertySections.Unavailable(
                    "The parent message for this signal was not found in the active document.")
                : DbcSemanticPropertyBuilder.Build(_file, _message, sig);
        Notify();
    }

    public void HighlightComment(DbcCommentEntry? comment)
    {
        _highlightedCommentLine = comment?.LineNumber;
        if (_file is not null)
        {
            _sections = _signal is not null
                ? DbcSemanticPropertyBuilder.Build(
                    _file,
                    _message,
                    _signal,
                    _highlightedCommentLine)
                : _message is not null
                    ? DbcSemanticPropertyBuilder.Build(
                        _file,
                        _message,
                        null,
                        _highlightedCommentLine)
                    : DbcSemanticPropertyBuilder.Build(
                        _file,
                        null,
                        null,
                        _highlightedCommentLine);
        }

        Notify();
    }

    public void Refresh(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var targetType = TargetType;
        var messageId = _message?.Id;
        var signalName = _signal?.Name;
        _file = file;

        if (targetType == "Message"
            && messageId is uint messageIdentifier
            && file.Document.Messages.FirstOrDefault(
                message => message.Id == messageIdentifier) is { } message)
        {
            ShowMessage(message);
            return;
        }

        if (targetType == "Signal"
            && messageId is uint signalMessageId
            && signalName is { Length: > 0 }
            && file.Document.Messages.FirstOrDefault(
                candidate => candidate.Id == signalMessageId) is { } parent)
        {
            var signal = parent.Signals.FirstOrDefault(
                candidate => string.Equals(candidate.Name, signalName, StringComparison.Ordinal));
            if (signal is not null)
            {
                ShowMessage(parent);
                ShowSignal(signal);
                return;
            }
        }

        ShowFile(file);
    }

    public string? GetStatementText(int lineNumber)
    {
        if (_file is null || lineNumber <= 0)
        {
            return null;
        }

        foreach (var span in _file.ParseResult.StatementSpans)
        {
            if (span.LineNumber == lineNumber && span.Length > 0)
            {
                return _file.Content.Substring(span.Start, span.Length).TrimEnd();
            }
        }

        return null;
    }

    private void ResetFields()
    {
        TargetName = null;
        TargetType = null;
        FileKey = null;
        Encoding = null;
        UndefinedByteCount = 0;
        FileMessageCount = 0;
        FileSignalCount = 0;
        ErrorCount = 0;
        WarningCount = 0;
        Id = null;
        IdHex = null;
        Name = null;
        Length = null;
        Transmitter = null;
        StartBit = null;
        Factor = null;
        Offset = null;
        Min = null;
        Max = null;
        Unit = null;
        Receiver = null;
        ByteOrder = null;
        Signed = null;
        Multiplexer = null;
        ParentMessage = null;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private void Notify()
    {
        StateChanged?.Invoke();
    }
}
