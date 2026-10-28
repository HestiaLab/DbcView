namespace DbcView.Domain;

/// <summary>
/// Encoding detected for uploaded bytes. Undefined Windows-1252 bytes are
/// counted instead of being silently replaced.
/// </summary>
public sealed record DbcFileEncoding(
    string Name,
    bool IsFallback,
    int UndefinedByteCount)
{
    public string DisplayName => IsFallback ? $"{Name} (fallback)" : Name;
}
