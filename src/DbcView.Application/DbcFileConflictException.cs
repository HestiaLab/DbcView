namespace DbcView.Application;

/// <summary>Raised when a folder or file operation conflicts with existing state.</summary>
public sealed class DbcFileConflictException : InvalidOperationException
{
    public DbcFileConflictException(string message)
        : base(message)
    {
    }
}
