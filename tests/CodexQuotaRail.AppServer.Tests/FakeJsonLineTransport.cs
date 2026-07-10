using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class FakeJsonLineTransport : IJsonLineTransport
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource _readerStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;
    private int _readCount;
    private int _startCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    public int StartCount => Volatile.Read(ref _startCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _startCount);
        return Task.CompletedTask;
    }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
        _outgoing.Writer.WriteAsync(line, cancellationToken);

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readCount);
        _readerStarted.TrySetResult();

        await foreach (var line in _incoming.Reader.ReadAllAsync(cancellationToken))
        {
            yield return line;
        }
    }

    public void QueueIncoming(string line)
    {
        if (!_incoming.Writer.TryWrite(line))
        {
            throw new InvalidOperationException("测试 transport 已关闭。");
        }
    }

    public ValueTask<string> ReadWrittenLineAsync(CancellationToken cancellationToken) =>
        _outgoing.Reader.ReadAsync(cancellationToken);

    public bool TryReadWrittenLine(out string? line) => _outgoing.Reader.TryRead(out line);

    public Task WaitUntilReadingAsync(CancellationToken cancellationToken) =>
        _readerStarted.Task.WaitAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            _incoming.Writer.TryComplete();
            _outgoing.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
