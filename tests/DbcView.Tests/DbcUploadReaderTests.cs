#nullable enable
using System.Text;
using DbcView.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcUploadReaderTests
{
    private const long Limit = 4 * 1024 * 1024;

    [Fact]
    public async Task ReadAsync_returns_file_text()
    {
        var file = new FakeBrowserFile("engine.dbc", "VERSION \"\"\nBU_: XXX\n");

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.True(result.Success);
        Assert.Equal("VERSION \"\"\nBU_: XXX\n", result.Content);
        Assert.Null(result.Error);
        Assert.Equal("UTF-8", result.Encoding?.Name);
        Assert.False(result.Encoding?.IsFallback);
    }

    [Fact]
    public async Task ReadAsync_detects_utf8_and_utf16_boms()
    {
        var utf8Body = Encoding.UTF8.GetBytes("VERSION \"\"\nBU_: XXX\n");
        var utf8Bom = new FakeBrowserFile(
            "bom.dbc",
            string.Empty,
            bytes: Encoding.UTF8.GetPreamble().Concat(utf8Body).ToArray());
        var utf8Result = await DbcUploadReader.ReadAsync(utf8Bom, Limit);
        Assert.True(utf8Result.Success);
        Assert.Equal("UTF-8 (BOM)", utf8Result.Encoding?.Name);
        Assert.StartsWith("VERSION", utf8Result.Content, StringComparison.Ordinal);

        var utf16 = Encoding.Unicode.GetBytes("VERSION \"\"\nBU_: XXX\n");
        var utf16File = new FakeBrowserFile(
            "utf16.dbc",
            string.Empty,
            bytes: Encoding.Unicode.GetPreamble().Concat(utf16).ToArray());
        var utf16Result = await DbcUploadReader.ReadAsync(utf16File, Limit);
        Assert.True(utf16Result.Success);
        Assert.Equal("UTF-16 LE (BOM)", utf16Result.Encoding?.Name);
        Assert.Contains("BU_: XXX", utf16Result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_falls_back_to_windows_1252_and_counts_undefined_bytes()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("VERSION \"\"\nBU_: XXX\nCM_ \"caf"));
        bytes.Add(0xE9);
        bytes.AddRange(Encoding.ASCII.GetBytes(" "));
        bytes.Add(0x93);
        bytes.AddRange(Encoding.ASCII.GetBytes("quoted"));
        bytes.Add(0x94);
        bytes.AddRange(Encoding.ASCII.GetBytes(" "));
        bytes.Add(0x81);
        bytes.AddRange(Encoding.ASCII.GetBytes("\";\n"));
        var file = new FakeBrowserFile("legacy.dbc", string.Empty, bytes: bytes.ToArray());

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.True(result.Success);
        Assert.Equal("Windows-1252", result.Encoding?.Name);
        Assert.True(result.Encoding?.IsFallback);
        Assert.Equal(1, result.Encoding?.UndefinedByteCount);
        Assert.Contains("café", result.Content, StringComparison.Ordinal);
        Assert.Contains("\u201Cquoted\u201D", result.Content, StringComparison.Ordinal);
        Assert.Contains("\uFFFD", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_keeps_valid_utf8_non_ascii_text()
    {
        var file = new FakeBrowserFile(
            "utf8.dbc",
            "VERSION \"\"\nBU_: XXX\nCM_ \"café\";\n");

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.True(result.Success);
        Assert.Equal("UTF-8", result.Encoding?.Name);
        Assert.Contains("café", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_decodes_the_legacy_windows_1252_sample()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "legacy-windows1252.dbc");
        var file = new FakeBrowserFile(
            "legacy-windows1252.dbc",
            string.Empty,
            bytes: File.ReadAllBytes(path));

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.True(result.Success);
        Assert.Equal("Windows-1252", result.Encoding?.Name);
        Assert.True(result.Encoding?.IsFallback);
        Assert.Equal(1, result.Encoding?.UndefinedByteCount);
        Assert.Contains("café", result.Content, StringComparison.Ordinal);
        Assert.Contains("\u20AC", result.Content, StringComparison.Ordinal);
        Assert.Contains("\u201Cquoted\u201D", result.Content, StringComparison.Ordinal);
        Assert.Contains("\uFFFD", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_reports_oversized_file_without_opening_stream()
    {
        var file = new FakeBrowserFile("big.dbc", new string('x', 32), reportedSize: Limit + 1);

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.False(result.Success);
        Assert.Contains("too large", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, file.OpenCount);
    }

    [Fact]
    public async Task ReadAsync_reports_stream_open_failure()
    {
        var file = new FakeBrowserFile("broken.dbc", "content", openError: new IOException("stream open failed"));

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.False(result.Success);
        Assert.Contains("could not be read", result.Error, StringComparison.Ordinal);
        Assert.Contains("stream open failed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_reports_read_failure_after_open()
    {
        var file = new FakeBrowserFile(
            "broken.dbc",
            "content",
            readStream: new FailingReadStream(new IOException("read failed")));

        var result = await DbcUploadReader.ReadAsync(file, Limit);

        Assert.False(result.Success);
        Assert.Contains("could not be read", result.Error, StringComparison.Ordinal);
        Assert.Contains("read failed", result.Error, StringComparison.Ordinal);
    }

    private sealed class FakeBrowserFile : IBrowserFile
    {
        private readonly string _content;
        private readonly byte[]? _bytes;
        private readonly long _reportedSize;
        private readonly Exception? _openError;
        private readonly Stream? _readStream;

        public FakeBrowserFile(
            string name,
            string content,
            long? reportedSize = null,
            Exception? openError = null,
            Stream? readStream = null,
            byte[]? bytes = null)
        {
            Name = name;
            _content = content;
            _bytes = bytes;
            _reportedSize = reportedSize
                ?? bytes?.Length
                ?? System.Text.Encoding.UTF8.GetByteCount(content);
            _openError = openError;
            _readStream = readStream;
        }

        public string Name { get; }

        public string ContentType => "text/plain";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => _reportedSize;

        public int OpenCount { get; private set; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            if (_openError is not null)
            {
                throw _openError;
            }

            return _readStream ?? new MemoryStream(
                _bytes ?? System.Text.Encoding.UTF8.GetBytes(_content));
        }
    }

    private sealed class FailingReadStream : Stream
    {
        private readonly Exception _error;

        public FailingReadStream(Exception error)
        {
            _error = error;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw _error;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => throw _error;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
