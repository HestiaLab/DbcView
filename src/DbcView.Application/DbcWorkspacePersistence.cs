namespace DbcView.Application;

public sealed record DbcPersistedFile(
    string Folder,
    string FileName,
    string Content,
    string? EncodingName,
    bool EncodingFallback,
    int UndefinedByteCount,
    string ViewMode);

public sealed record DbcPersistedDocument(
    string Folder,
    string FileName);

public sealed record DbcWorkspaceSnapshot(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    IReadOnlyList<string> Folders,
    IReadOnlyList<DbcPersistedFile> Files,
    IReadOnlyList<DbcPersistedDocument> OpenDocuments,
    DbcPersistedDocument? ActiveDocument)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record DbcPersistenceLoadResult(
    bool Found,
    bool IsCompatible,
    string? Warning,
    DbcWorkspaceSnapshot? Snapshot)
{
    public static DbcPersistenceLoadResult None() => new(false, true, null, null);

    public static DbcPersistenceLoadResult Restored(DbcWorkspaceSnapshot snapshot) =>
        new(true, true, null, snapshot);

    public static DbcPersistenceLoadResult Incompatible(string warning) =>
        new(true, false, warning, null);
}

public sealed record DbcPersistenceResult(bool Success, string? Error)
{
    public static DbcPersistenceResult Ok() => new(true, null);

    public static DbcPersistenceResult Fail(string error) => new(false, error);
}

/// <summary>Local browser session persistence port; implementations must never upload.</summary>
public interface IWorkspacePersistence
{
    Task<DbcPersistenceLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<DbcPersistenceResult> SaveAsync(
        DbcWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<DbcPersistenceResult> ClearAsync(CancellationToken cancellationToken = default);
}
