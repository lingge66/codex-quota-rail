using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class FakeJsonLineTransport : IJsonLineTransport
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource _disposeStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseDispose =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseStart =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readerStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _startEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration _startCancellationRegistration;
    private int _disposeCount;
    private int _readCount;
    private int _startCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    public int StartCount => Volatile.Read(ref _startCount);

    public Exception? DisposeException { get; init; }

    public bool PauseDispose { get; init; }

    public bool PauseStart { get; init; }

    public Action? StartCancellationCallback { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _startCount);
        _startEntered.TrySetResult();
        if (StartCancellationCallback is not null)
        {
            _startCancellationRegistration = cancellationToken.UnsafeRegister(
                static state => ((Action)state!).Invoke(),
                StartCancellationCallback);
        }

        if (PauseStart)
        {
            await _releaseStart.Task.WaitAsync(cancellationToken);
        }
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

    public void CompleteIncoming(Exception? error = null) =>
        _incoming.Writer.TryComplete(error);

    public ValueTask<string> ReadWrittenLineAsync(CancellationToken cancellationToken) =>
        _outgoing.Reader.ReadAsync(cancellationToken);

    public bool TryReadWrittenLine(out string? line) => _outgoing.Reader.TryRead(out line);

    public Task WaitUntilReadingAsync(CancellationToken cancellationToken) =>
        _readerStarted.Task.WaitAsync(cancellationToken);

    public Task WaitUntilDisposeStartsAsync(CancellationToken cancellationToken) =>
        _disposeStarted.Task.WaitAsync(cancellationToken);

    public Task WaitUntilStartEntersAsync(CancellationToken cancellationToken) =>
        _startEntered.Task.WaitAsync(cancellationToken);

    public void ReleaseDispose() => _releaseDispose.TrySetResult();

    public void ReleaseStart() => _releaseStart.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _incoming.Writer.TryComplete();
                _outgoing.Writer.TryComplete();
                _disposeStarted.TrySetResult();
                if (PauseDispose)
                {
                    await _releaseDispose.Task;
                }

                if (DisposeException is not null)
                {
                    throw DisposeException;
                }
            }
        }
        finally
        {
            _startCancellationRegistration.Dispose();
        }
    }
}
