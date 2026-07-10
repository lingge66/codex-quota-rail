using System.Diagnostics;
using System.Text;

namespace CodexQuotaRail.AppServer.Discovery;

internal sealed partial class BoundedProcessRunner : IBoundedProcessRunner
{
    internal const int OutputCharacterLimit = 8 * 1024;
    internal static readonly TimeSpan KillGrace = TimeSpan.FromMilliseconds(250);
    private static readonly BoundedText EmptyText = new(string.Empty, Truncated: false);
    private readonly IBoundedProcessFactory _factory;
    private readonly TimeProvider _timeProvider;
    private int _operationState;

    public BoundedProcessRunner(
        IBoundedProcessFactory? factory = null,
        TimeProvider? timeProvider = null)
    {
        _factory = factory ?? new SystemBoundedProcessFactory();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public BoundedProcessResult Run(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromSeconds(30));
        return RunCoreAsync(startInfo, timeout).GetAwaiter().GetResult();
    }

    private async Task<BoundedProcessResult> RunCoreAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout)
    {
        if (Interlocked.CompareExchange(ref _operationState, 1, 0) != 0)
        {
            return CreateResult(
                started: false,
                exited: false,
                timedOut: true,
                exitCode: null,
                outputTask: null,
                errorTask: null);
        }

        IBoundedProcess? process = null;
        Task<bool>? startTask = null;
        Task? killTask = null;
        Task? exitTask = null;
        Task<BoundedText>? outputTask = null;
        Task<BoundedText>? errorTask = null;
        var started = false;
        var exited = false;
        var timedOut = false;
        var cleanupTransferred = false;
        int? exitCode = null;
        try
        {
            using var deadline = new CancellationTokenSource(timeout, _timeProvider);
            process = _factory.Create(startInfo);
            startTask = Task.Run(process.Start);
            try
            {
                started = await startTask.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                timedOut = true;
                cleanupTransferred = true;
                Observe(CleanupAfterLateStartAsync(process, startTask));
                return CreateResult(started, exited, timedOut, exitCode, null, null);
            }

            if (!started)
            {
                return CreateResult(started, exited, timedOut, exitCode, null, null);
            }

            outputTask = DrainAsync(process.StandardOutput);
            errorTask = DrainAsync(process.StandardError);
            exitTask = process.WaitForExitAsync();
            var completion = Task.WhenAll(exitTask, outputTask, errorTask);
            try
            {
                await completion.WaitAsync(deadline.Token).ConfigureAwait(false);
                exited = true;
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                timedOut = true;
                using var killDeadline = new CancellationTokenSource(KillGrace, _timeProvider);
                killTask = Task.Run(() => TryKill(process));
                try
                {
                    await Task.WhenAll(completion, killTask)
                        .WaitAsync(killDeadline.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (killDeadline.IsCancellationRequested)
                {
                    cleanupTransferred = true;
                    Observe(CleanupAfterTimeoutAsync(process, completion, killTask));
                }
                catch
                {
                }

                if (exitTask.IsCompletedSuccessfully)
                {
                    exited = true;
                    exitCode = process.ExitCode;
                }
            }
            catch
            {
            }

            return CreateResult(
                started,
                exited,
                timedOut,
                exitCode,
                outputTask,
                errorTask);
        }
        catch
        {
            return CreateResult(started, exited, timedOut, exitCode, outputTask, errorTask);
        }
        finally
        {
            if (!cleanupTransferred)
            {
                TryDispose(process);
                ReleaseOperation();
            }

            Observe(startTask);
            Observe(killTask);
            Observe(exitTask);
            Observe(outputTask);
            Observe(errorTask);
        }
    }

    private static async Task<BoundedText> DrainAsync(TextReader reader)
    {
        var retained = new StringBuilder(OutputCharacterLimit);
        var buffer = new char[2048];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                return new BoundedText(retained.ToString(), truncated);
            }

            var remaining = OutputCharacterLimit - retained.Length;
            if (remaining > 0)
            {
                retained.Append(buffer, 0, Math.Min(remaining, read));
            }

            truncated |= read > remaining;
        }
    }

    private static BoundedProcessResult CreateResult(
        bool started,
        bool exited,
        bool timedOut,
        int? exitCode,
        Task<BoundedText>? outputTask,
        Task<BoundedText>? errorTask)
    {
        var output = GetCompletedText(outputTask);
        var error = GetCompletedText(errorTask);
        return new BoundedProcessResult(
            started,
            exited,
            timedOut,
            exitCode,
            output.Value,
            error.Value,
            output.Truncated,
            error.Truncated);
    }

    private static BoundedText GetCompletedText(Task<BoundedText>? task)
    {
        if (task is null || !task.IsCompletedSuccessfully)
        {
            return EmptyText;
        }

        return task.Result;
    }

    private sealed record BoundedText(string Value, bool Truncated);
}
