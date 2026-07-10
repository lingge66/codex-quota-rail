namespace CodexQuotaRail.AppServer.Discovery;

internal sealed partial class BoundedProcessRunner
{
    private async Task CleanupAfterLateStartAsync(
        IBoundedProcess process,
        Task<bool> startTask)
    {
        Task? exitTask = null;
        Task<BoundedText>? outputTask = null;
        Task<BoundedText>? errorTask = null;
        try
        {
            var started = await startTask.ConfigureAwait(false);
            if (!started)
            {
                return;
            }

            outputTask = DrainAsync(process.StandardOutput);
            errorTask = DrainAsync(process.StandardError);
            exitTask = process.WaitForExitAsync();
            TryKill(process);
            await Task.WhenAll(exitTask, outputTask, errorTask).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            TryDispose(process);
            ReleaseOperation();
            Observe(exitTask);
            Observe(outputTask);
            Observe(errorTask);
        }
    }

    private async Task CleanupAfterTimeoutAsync(
        IBoundedProcess process,
        Task completion,
        Task killTask)
    {
        try
        {
            await Task.WhenAll(completion, killTask).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            TryDispose(process);
            ReleaseOperation();
        }
    }

    private void ReleaseOperation() => Volatile.Write(ref _operationState, 0);

    private static void TryKill(IBoundedProcess process)
    {
        try
        {
            process.Kill();
        }
        catch
        {
        }
    }

    private static void TryDispose(IBoundedProcess? process)
    {
        try
        {
            process?.Dispose();
        }
        catch
        {
        }
    }

    private static void Observe(Task? task)
    {
        if (task is null || task.IsCompletedSuccessfully || task.IsCanceled)
        {
            return;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
