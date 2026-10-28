namespace DbcView.Domain;

/// <summary>
/// Stable identity of a DBC document inside the workspace: folder plus file
/// name. File names alone are not unique because two folders may contain the
/// same name, and save/tab state must target the exact document.
/// </summary>
public readonly record struct DbcDocumentId
{
    public DbcDocumentId(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("A folder name is required.", nameof(folder));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A file name is required.", nameof(fileName));
        }

        if (folder.Contains('/') || folder.Contains('\\'))
        {
            throw new ArgumentException("Folder names cannot contain path separators.", nameof(folder));
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            throw new ArgumentException("File names cannot contain path separators.", nameof(fileName));
        }

        Folder = folder;
        FileName = fileName;
    }

    public string Folder { get; }

    public string FileName { get; }

    /// <summary>Human-readable key suitable for UI state and content references.</summary>
    public string Key => $"{Folder}/{FileName}";

    public override string ToString() => Key;

    public static bool TryParseKey(string? key, out DbcDocumentId documentId)
    {
        documentId = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var separator = key.IndexOf('/');
        if (separator <= 0 || separator == key.Length - 1)
        {
            return false;
        }

        try
        {
            documentId = new DbcDocumentId(key[..separator], key[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
