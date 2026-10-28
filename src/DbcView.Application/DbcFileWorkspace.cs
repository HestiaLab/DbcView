using DbcView.Domain;

namespace DbcView.Application;

/// <summary>
/// Application use cases for the in-memory DBC workspace: folder creation,
/// upload, open (find) and save. Parsing and storage are ports, so these use
/// cases can run and be tested without Blazor, Atlas or the browser.
/// </summary>
public sealed class DbcFileWorkspace : IDisposable
{
    private readonly IDbcFileRepository _repository;
    private readonly IDbcCodec _codec;
    private bool _disposed;

    public DbcFileWorkspace(IDbcFileRepository repository, IDbcCodec codec)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _repository.Changed += OnRepositoryChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<string> Folders => _repository.Folders;

    public IReadOnlyList<DbcFileEntry> FilesIn(string folder) => _repository.FilesIn(folder);

    public DbcFileEntry? Find(DbcDocumentId id) => _repository.Find(id);

    public DbcParseResult Parse(string content) => _codec.Parse(content);

    public DbcStructuredEditOutcome ApplyStructuredEdit(
        DbcDocumentId id,
        DbcStructuredEdit edit)
    {
        var existing = _repository.Find(id)
            ?? throw new DbcFileNotFoundException($"File '{id.Key}' does not exist.");
        var build = DbcStructuredEditor.Build(existing, edit);
        if (!build.Success)
        {
            return DbcStructuredEditOutcome.Fail(build.Error ?? "The edit could not be applied.");
        }

        var parse = _codec.Parse(build.Content);
        if (parse.HasErrors)
        {
            return DbcStructuredEditOutcome.Fail(
                $"The edit would introduce {parse.ErrorCount} DBC error(s).",
                parse.Diagnostics);
        }

        var updated = existing with
        {
            Content = build.Content,
            ParseResult = parse,
        };
        _repository.Replace(updated);
        return new DbcStructuredEditOutcome(
            true,
            null,
            build.Diff,
            parse,
            Array.Empty<DbcDiagnostic>());
    }

    public void CreateFolder(string name) => _repository.CreateFolder(name);

    /// <summary>
    /// Adds a new document. Invalid DBC text is stored as an explicit draft:
    /// the entry keeps the raw content and the diagnostics so the UI can show
    /// both instead of pretending the file is a valid empty document.
    /// </summary>
    public DbcFileEntry Upload(
        DbcDocumentId id,
        string content,
        DbcFileEncoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_repository.Find(id) is not null)
        {
            throw new DbcFileConflictException($"File '{id.Key}' already exists.");
        }

        if (!_repository.Folders.Contains(id.Folder, StringComparer.Ordinal))
        {
            throw new DbcFileConflictException($"Folder '{id.Folder}' does not exist.");
        }

        var entry = new DbcFileEntry(id, content, _codec.Parse(content), encoding);
        _repository.Add(entry);
        return entry;
    }

    /// <summary>
    /// Saves the raw draft and returns the new parse result. The content is
    /// always retained; diagnostics make an invalid draft visible instead of
    /// rejecting the user's work or silently accepting it as valid.
    /// </summary>
    public DbcFileEntry Save(DbcDocumentId id, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var existing = _repository.Find(id)
            ?? throw new DbcFileNotFoundException($"File '{id.Key}' does not exist.");

        var updated = existing with
        {
            Content = content,
            ParseResult = _codec.Parse(content),
        };
        _repository.Replace(updated);
        return updated;
    }

    public void Rename(DbcDocumentId id, string newFileName)
    {
        if (_repository.Find(id) is null)
        {
            throw new DbcFileNotFoundException($"File '{id.Key}' does not exist.");
        }

        _repository.Rename(id, newFileName);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _repository.Changed -= OnRepositoryChanged;
        _disposed = true;
    }

    private void OnRepositoryChanged()
    {
        Changed?.Invoke();
    }
}
