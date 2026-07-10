using System.Diagnostics;
using System.Runtime.CompilerServices;
using CodexQuotaRail.AppServer.Protocol;

namespace CodexQuotaRail.AppServer.Transport;

public sealed record ProcessLaunchSpec(string FileName, IReadOnlyList<string> Arguments);

public sealed record ProcessDiagnostic(string EventName, int CharacterCount);

public sealed class ProcessJsonLineTransport : IJsonLineTransport
{
    private readonly Action<ProcessDiagnostic>? _diagnosticSink;
    private readonly OrderedCallbackDispatcher _diagnosticDispatcher = new();
    private readonly ProcessLaunchSpec _launchSpec;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _disposeTask;
    private Process? _process;
    private int _readState;
    private Task? _stderrTask;

    public ProcessJsonLineTransport(
        ProcessLaunchSpec launchSpec,
        Action<ProcessDiagnostic>? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(launchSpec);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchSpec.FileName);
        ArgumentNullException.ThrowIfNull(launchSpec.Arguments);

        _launchSpec = launchSpec;
        _diagnosticSink = diagnosticSink;
    }

    public long DroppedDiagnosticCount => _diagnosticDispatcher.DroppedCount;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_process is not null)
            {
                throw new InvalidOperationException("App Server 进程已启动。");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _launchSpec.FileName,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in _launchSpec.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process
            {
                StartInfo = startInfo,
            };

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    throw new InvalidOperationException("无法启动 App Server 进程。");
                }
            }
            catch
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动 App Server 进程。");
            }

            _process = process;
            _stderrTask = DrainStandardErrorAsync(process, _lifetimeCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var process = GetStartedProcess();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _readState, 1) != 0)
        {
            throw new InvalidOperationException("只能启动一个 App Server stdout 读取循环。");
        }

        var process = GetStartedProcess();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? cleanupOwner = null;
        Task cleanupTask;
        lock (_stateLock)
        {
            if (_disposeTask is null)
            {
                cleanupOwner = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = cleanupOwner.Task;
            }

            cleanupTask = _disposeTask;
        }

        if (cleanupOwner is not null)
        {
            _diagnosticDispatcher.Stop();
            _ = CompleteDisposeAsync(cleanupOwner);
        }

        return new ValueTask(cleanupTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (AppServerProtocolException error)
        {
            completion.TrySetException(error);
        }
        catch
        {
            completion.TrySetException(
                new AppServerProtocolException("清理 App Server 进程失败。"));
        }
    }

    private async Task DisposeCoreAsync()
    {
        var cleanupFailed = false;
        Process? process;
        Task? stderrTask;
        lock (_stateLock)
        {
            process = _process;
            _process = null;
            stderrTask = _stderrTask;
            _stderrTask = null;
        }

        try
        {
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch
            {
                cleanupFailed = true;
            }

            if (process is not null)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                    cleanupFailed = true;
                }

                var shouldWaitForExit = false;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    shouldWaitForExit = true;
                }
                catch
                {
                    cleanupFailed = true;
                }

                if (shouldWaitForExit)
                {
                    try
                    {
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        cleanupFailed = true;
                    }
                }
            }

            if (stderrTask is not null)
            {
                try
                {
                    await stderrTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                }
                catch
                {
                    cleanupFailed = true;
                }
            }
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
                cleanupFailed = true;
            }

            try
            {
                _lifetimeCancellation.Dispose();
            }
            catch
            {
                cleanupFailed = true;
            }
        }

        if (cleanupFailed)
        {
            throw new AppServerProtocolException("清理 App Server 进程失败。");
        }
    }

    private async Task DrainStandardErrorAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                ReportDiagnostic(new ProcessDiagnostic("app_server_stderr", line.Length));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ReportDiagnostic(ProcessDiagnostic diagnostic)
    {
        var diagnosticSink = _diagnosticSink;
        if (diagnosticSink is null)
        {
            return;
        }

        _diagnosticDispatcher.Dispatch(() => diagnosticSink(diagnostic));
    }

    private Process GetStartedProcess()
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            ThrowIfDisposed();
            return _process ?? throw new InvalidOperationException("必须先启动 App Server 进程。");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeTask) is not null, this);
    }
}
