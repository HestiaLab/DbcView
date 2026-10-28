using DbcView.Domain;

namespace DbcView.Application;

/// <summary>An in-memory DBC file: document identity, raw text and parse result.</summary>
public sealed record DbcFileEntry(DbcDocumentId Id, string Content, DbcParseResult ParseResult)
{
    public DbcDocument Document => ParseResult.Document;

    public bool HasParseErrors => ParseResult.HasErrors;
}

/// <summary>Port for parsing and serializing DBC text.</summary>
public interface IDbcCodec
{
    DbcParseResult Parse(string content);

    string Serialize(DbcDocument document);
}

/// <summary>Port for the folder/file workspace storage.</summary>
public interface IDbcFileRepository
{
    event Action? Changed;

    IReadOnlyList<string> Folders { get; }

    IReadOnlyList<DbcFileEntry> FilesIn(string folder);

    DbcFileEntry? Find(DbcDocumentId id);

    void CreateFolder(string name);

    void Add(DbcFileEntry file);

    void Replace(DbcFileEntry file);

    void Rename(DbcDocumentId id, string newFileName);
}
