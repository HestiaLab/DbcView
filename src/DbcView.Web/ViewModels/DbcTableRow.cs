using DbcView.Domain;

namespace DbcView.Web.ViewModels;

public enum DbcTableRowKind
{
    Message,
    Signal,
}

/// <summary>One rendered table row: a message header or one of its expanded signals.</summary>
public sealed record DbcTableRow(
    int Index,
    DbcTableRowKind Kind,
    DbcMessage Message,
    int MessageIndex,
    DbcSignal? Signal,
    int SignalIndex,
    string Key,
    bool IsExpanded)
{
    public bool IsMessage => Kind == DbcTableRowKind.Message;
}
