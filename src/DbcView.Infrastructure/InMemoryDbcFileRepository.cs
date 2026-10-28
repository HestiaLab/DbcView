using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Infrastructure;

/// <summary>In-memory implementation of the DBC file repository port.</summary>
public sealed class InMemoryDbcFileRepository : IDbcFileRepository
{
    private readonly Dictionary<string, List<DbcFileEntry>> _filesByFolder = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyList<string> Folders
    {
        get
        {
            return _filesByFolder.Keys
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public void CreateFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A folder name is required.", nameof(name));
        }

        if (_filesByFolder.ContainsKey(name))
        {
            throw new DbcFileConflictException($"Folder '{name}' already exists.");
        }

        _filesByFolder.Add(name, new List<DbcFileEntry>());
        Changed?.Invoke();
    }

    public void Add(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var files = RequireFolder(file.Id.Folder);
        if (files.Any(entry => entry.Id.FileName == file.Id.FileName))
        {
            throw new DbcFileConflictException($"File '{file.Id.Key}' already exists.");
        }

        files.Add(file);
        Changed?.Invoke();
    }

    public void Replace(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var files = RequireFolder(file.Id.Folder);
        var index = files.FindIndex(entry => entry.Id.FileName == file.Id.FileName);
        if (index < 0)
        {
            throw new DbcFileNotFoundException($"File '{file.Id.Key}' does not exist.");
        }

        files[index] = file;
        Changed?.Invoke();
    }

    public void Rename(DbcDocumentId id, string newFileName)
    {
        var files = RequireFolder(id.Folder);
        var index = files.FindIndex(entry => entry.Id.FileName == id.FileName);
        if (index < 0)
        {
            throw new DbcFileNotFoundException($"File '{id.Key}' does not exist.");
        }

        var renamedId = new DbcDocumentId(id.Folder, newFileName);
        if (files.Any(entry => entry.Id.FileName == renamedId.FileName))
        {
            throw new DbcFileConflictException($"File '{renamedId.Key}' already exists.");
        }

        files[index] = files[index] with { Id = renamedId };
        Changed?.Invoke();
    }

    public IReadOnlyList<DbcFileEntry> FilesIn(string folder)
    {
        return _filesByFolder.TryGetValue(folder, out var files)
            ? files.OrderBy(file => file.Id.FileName, StringComparer.Ordinal).ToArray()
            : Array.Empty<DbcFileEntry>();
    }

    public DbcFileEntry? Find(DbcDocumentId id)
    {
        return _filesByFolder.TryGetValue(id.Folder, out var files)
            ? files.FirstOrDefault(entry => entry.Id.FileName == id.FileName)
            : null;
    }

    private List<DbcFileEntry> RequireFolder(string folder)
    {
        if (!_filesByFolder.TryGetValue(folder, out var files))
        {
            throw new DbcFileConflictException($"Folder '{folder}' does not exist.");
        }

        return files;
    }
}
