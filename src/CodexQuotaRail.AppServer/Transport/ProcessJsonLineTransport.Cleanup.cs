using System.Diagnostics;
using CodexQuotaRail.AppServer.Protocol;

namespace CodexQuotaRail.AppServer.Transport;

public sealed partial class ProcessJsonLineTransport
{
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
}
