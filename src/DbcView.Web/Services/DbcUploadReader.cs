using DbcView.Domain;
using Microsoft.AspNetCore.Components.Forms;

namespace DbcView.Web.Services;

/// <summary>Outcome of reading an uploaded browser file into text.</summary>
public sealed record DbcFileReadResult(
    bool Success,
    string Content,
    DbcFileEncoding? Encoding,
    string? Error)
{
    public static DbcFileReadResult Ok(string content, DbcFileEncoding encoding) =>
        new(true, content, encoding, null);

    public static DbcFileReadResult Fail(string error) =>
        new(false, string.Empty, null, error);
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
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > maxAllowedBytes)
            {
                return DbcFileReadResult.Fail(
                    $"is too large ({buffer.Length:N0} bytes); the limit is {maxAllowedBytes:N0} bytes.");
            }

            var decoded = DbcEncodingDetector.Decode(buffer.ToArray());
            return DbcFileReadResult.Ok(decoded.Content, decoded.Encoding);
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
