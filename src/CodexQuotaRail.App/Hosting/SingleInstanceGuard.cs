using System.IO;
using System.IO.Pipes;

namespace CodexQuotaRail.App.Hosting;

public sealed class SingleInstanceGuard : IDisposable, IAsyncDisposable
{
    public const string DefaultMutexName = @"Local\CodexQuotaRail";
    public const string DefaultPipeName = "CodexQuotaRail.Activate";
    private readonly Action _activate;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Mutex _mutexHandle;
    private readonly string _pipeName;
    private readonly Task? _listenerTask;
    private int _disposed;

    private SingleInstanceGuard(
        Mutex mutexHandle,
        string pipeName,
        bool isPrimary,
        Action activate)
    {
        _mutexHandle = mutexHandle;
        _pipeName = pipeName;
        IsPrimary = isPrimary;
        _activate = activate;
        if (isPrimary)
        {
            _listenerTask = Task.Run(() => ListenAsync(_shutdown.Token));
        }
    }

    public bool IsPrimary { get; }

    public static SingleInstanceGuard Acquire(Action activate) =>
        Acquire(DefaultMutexName, DefaultPipeName, activate);

    public static SingleInstanceGuard Acquire(
        string mutexName,
        string pipeName,
        Action activate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(activate);
        var mutex = new Mutex(initiallyOwned: false, mutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, pipeName, createdNew, activate);
    }

    public async ValueTask<bool> SignalPrimaryAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IsPrimary)
        {
            return false;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            byte[] signal = [1];
            await client.WriteAsync(signal, timeoutSource.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (
            error is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
        _mutexHandle.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var signal = new byte[1];
                var count = await server.ReadAsync(signal, cancellationToken).ConfigureAwait(false);
                if (count > 0)
                {
                    TryActivate();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private void TryActivate()
    {
        try
        {
            _activate();
        }
        catch (Exception error) when (error is not StackOverflowException)
        {
        }
    }
}
