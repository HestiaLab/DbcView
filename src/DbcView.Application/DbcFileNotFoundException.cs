namespace DbcView.Application;

/// <summary>Raised when a requested document does not exist in the workspace.</summary>
public sealed class DbcFileNotFoundException : InvalidOperationException
{
    public DbcFileNotFoundException(string message)
        : base(message)
    {
    }
}
