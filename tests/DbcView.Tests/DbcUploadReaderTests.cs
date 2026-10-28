#nullable enable
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
        private readonly long _reportedSize;
        private readonly Exception? _openError;
        private readonly Stream? _readStream;

        public FakeBrowserFile(
            string name,
            string content,
            long? reportedSize = null,
            Exception? openError = null,
            Stream? readStream = null)
        {
            Name = name;
            _content = content;
            _reportedSize = reportedSize ?? System.Text.Encoding.UTF8.GetByteCount(content);
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

            return _readStream ?? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_content));
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
