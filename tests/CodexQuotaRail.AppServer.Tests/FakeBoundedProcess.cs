using System.Diagnostics;
using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class SingleProcessFactory(FakeBoundedProcess process)
    : IBoundedProcessFactory
{
    public IBoundedProcess Create(ProcessStartInfo startInfo) => process;
}

internal sealed class FakeBoundedProcess : IBoundedProcess
{
    private readonly bool _completeWhenKilled;
    private readonly TrackingReader _error;
    private readonly TaskCompletionSource _exit = NewSignal();
    private readonly TrackingReader _output;
    private readonly TaskCompletionSource _startRelease = NewSignal();
    private readonly bool _startWaitsForRelease;
    private readonly bool _killWaitsForRelease;
    private readonly TaskCompletionSource _killRelease = NewSignal();

    private FakeBoundedProcess(
        string output,
        string error,
        bool exited,
        bool completeWhenKilled,
        bool startWaitsForRelease = false,
        bool killWaitsForRelease = false)
    {
        _output = new TrackingReader(output);
        _error = new TrackingReader(error);
        _completeWhenKilled = completeWhenKilled;
        _startWaitsForRelease = startWaitsForRelease;
        _killWaitsForRelease = killWaitsForRelease;
        if (exited)
        {
            _exit.TrySetResult();
        }
    }

    public TaskCompletionSource Started { get; } = NewSignal();

    public TaskCompletionSource StartCompleted { get; } = NewSignal();

    public TaskCompletionSource Killed { get; } = NewSignal();

    public TaskCompletionSource KillCompleted { get; } = NewSignal();

    public TextReader StandardOutput => _output;

    public TextReader StandardError => _error;

    public int ExitCode => 0;

    public int KillCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool OutputReadCompleted => _output.ReadCompleted;

    public bool ErrorReadCompleted => _error.ReadCompleted;

    public static FakeBoundedProcess Exited(string output, string error) =>
        new(output, error, exited: true, completeWhenKilled: true);

    public static FakeBoundedProcess Running(bool completeWhenKilled) =>
        new(string.Empty, string.Empty, exited: false, completeWhenKilled);

    public static FakeBoundedProcess Starting() =>
        new(
            string.Empty,
            string.Empty,
            exited: false,
            completeWhenKilled: false,
            startWaitsForRelease: true);

    public static FakeBoundedProcess Killing() =>
        new(
            string.Empty,
            string.Empty,
            exited: false,
            completeWhenKilled: false,
            killWaitsForRelease: true);

    public bool Start()
    {
        Started.TrySetResult();
        if (_startWaitsForRelease)
        {
            _startRelease.Task.GetAwaiter().GetResult();
        }

        StartCompleted.TrySetResult();
        return true;
    }

    public void ReleaseStart() => _startRelease.TrySetResult();

    public void ReleaseKill() => _killRelease.TrySetResult();

    public Task WaitForExitAsync() => _exit.Task;

    public void Kill()
    {
        KillCount++;
        Killed.TrySetResult();
        if (_killWaitsForRelease)
        {
            _killRelease.Task.GetAwaiter().GetResult();
        }

        if (_completeWhenKilled)
        {
            _exit.TrySetResult();
        }

        KillCompleted.TrySetResult();
    }

    public void Dispose()
    {
        IsDisposed = true;
        _output.Dispose();
        _error.Dispose();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TrackingReader(string value) : StringReader(value)
    {
        public bool ReadCompleted { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                ReadCompleted = true;
            }

            return read;
        }
    }
}
