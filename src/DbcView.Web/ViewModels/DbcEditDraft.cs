using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Web.ViewModels;

public enum DbcEditDraftKind
{
    Comment,
    Attribute,
    ValueTable,
}

public sealed class DbcValueEntryDraft
{
    public string Value { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>Form state for one statement edit, including its before/after preview.</summary>
public sealed class DbcEditDraft
{
    private DbcStructuredEdit _template = null!;

    public DbcEditDraftKind Kind { get; private init; }

    public string Title { get; private init; } = string.Empty;

    public string? CommentText { get; set; }

    public string? AttributeName { get; private init; }

    public string? AttributeType { get; private init; }

    public string? AttributeRange { get; private init; }

    public string? AttributeValue { get; set; }

    public List<DbcValueEntryDraft> Entries { get; } = new();

    public int? ExistingLine { get; private init; }

    public bool CanDelete { get; private init; }

    public int DuplicateCount { get; private init; }

    public bool ConvertsReferencedTable { get; private init; }

    public string? OldStatement { get; private init; }

    public string PreviewStatement
    {
        get
        {
            var edit = Build(delete: false);
            return DbcStructuredEditor.RenderStatement(edit);
        }
    }

    public DbcStructuredEdit Build(bool delete)
    {
        return _template with
        {
            Delete = delete,
            CommentText = CommentText,
            AttributeValue = AttributeValue,
            ValueEntries = Entries
                .Select(entry => new DbcValueDescription(entry.Value, entry.Description))
                .ToArray(),
        };
    }

    public static DbcEditDraft ForComment(
        DbcSemanticScope scope,
        uint? messageId,
        string? signalName,
        int? lineNumber,
        string text,
        string? oldStatement,
        bool canDelete,
        int duplicateCount)
    {
        return new DbcEditDraft
        {
            Kind = DbcEditDraftKind.Comment,
            Title = lineNumber is null ? "Add comment" : "Edit comment",
            CommentText = text,
            ExistingLine = lineNumber,
            CanDelete = canDelete,
            DuplicateCount = duplicateCount,
            OldStatement = oldStatement,
            _template = new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                scope,
                messageId,
                signalName,
                lineNumber),
        };
    }

    public static DbcEditDraft ForAttribute(
        DbcSemanticScope scope,
        uint? messageId,
        string? signalName,
        int? lineNumber,
        string name,
        string? value,
        string? valueType,
        string? range,
        string? oldStatement,
        bool canDelete,
        int duplicateCount)
    {
        return new DbcEditDraft
        {
            Kind = DbcEditDraftKind.Attribute,
            Title = lineNumber is null ? "Set attribute value" : "Edit attribute value",
            AttributeName = name,
            AttributeType = valueType,
            AttributeRange = range,
            AttributeValue = value ?? string.Empty,
            ExistingLine = lineNumber,
            CanDelete = canDelete,
            DuplicateCount = duplicateCount,
            OldStatement = oldStatement,
            _template = new DbcStructuredEdit(
                DbcStructuredEditKind.Attribute,
                scope,
                messageId,
                signalName,
                lineNumber,
                AttributeName: name,
                AttributeValueType: valueType),
        };
    }

    public static DbcEditDraft ForValueTable(
        uint messageId,
        string signalName,
        int? lineNumber,
        IReadOnlyList<DbcValueDescription> entries,
        string? oldStatement,
        bool canDelete,
        bool convertsReferencedTable,
        int duplicateCount = 1)
    {
        var draft = new DbcEditDraft
        {
            Kind = DbcEditDraftKind.ValueTable,
            Title = lineNumber is null ? "Add value table" : "Edit value table",
            ExistingLine = lineNumber,
            CanDelete = canDelete,
            ConvertsReferencedTable = convertsReferencedTable,
            DuplicateCount = duplicateCount,
            OldStatement = oldStatement,
            _template = new DbcStructuredEdit(
                DbcStructuredEditKind.ValueTable,
                DbcSemanticScope.Signal,
                messageId,
                signalName,
                lineNumber),
        };

        foreach (var entry in entries)
        {
            draft.Entries.Add(new DbcValueEntryDraft
            {
                Value = entry.Value,
                Description = entry.Description,
            });
        }

        if (draft.Entries.Count == 0)
        {
            draft.Entries.Add(new DbcValueEntryDraft());
        }

        return draft;
    }
}
