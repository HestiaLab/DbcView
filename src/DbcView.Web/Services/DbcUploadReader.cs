using Microsoft.AspNetCore.Components.Forms;

namespace DbcView.Web.Services;

/// <summary>Outcome of reading an uploaded browser file into text.</summary>
public sealed record DbcFileReadResult(bool Success, string Content, string? Error)
{
    public static DbcFileReadResult Ok(string content) => new(true, content, null);

    public static DbcFileReadResult Fail(string error) => new(false, string.Empty, error);
}

/// <summary>
/// Reads an uploaded <see cref="IBrowserFile"/> with explicit size and I/O
/// boundaries. Failures are returned as visible diagnostics instead of
/// escaping the Blazor event handler.
/// </summary>
public static class DbcUploadReader
{
    public static async Task<DbcFileReadResult> ReadAsync(
        IBrowserFile file,
        long maxAllowedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Size > maxAllowedBytes)
        {
            return DbcFileReadResult.Fail(
                $"is too large ({file.Size:N0} bytes); the limit is {maxAllowedBytes:N0} bytes.");
        }

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedBytes, cancellationToken);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            return DbcFileReadResult.Ok(content);
        }
        catch (Exception ex)
        {
            // This is the upload boundary: I/O, browser and decoding failures
            // must become a visible per-file message, never an unhandled
            // renderer exception.
            return DbcFileReadResult.Fail($"could not be read: {ex.Message}");
        }
    }
}
