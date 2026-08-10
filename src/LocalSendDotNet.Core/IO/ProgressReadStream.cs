namespace LocalSendDotNet;

internal sealed class ProgressReadStream(Stream inner, Action<long> onRead) : Stream
{
    private long _totalRead;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => _totalRead; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); Report(read); return read; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Report(read); return read; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { await inner.DisposeAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
    private void Report(int read) { if (read <= 0) return; _totalRead += read; onRead(_totalRead); }
}
