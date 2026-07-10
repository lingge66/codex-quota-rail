using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

internal interface IBoundedProcessRunner
{
    BoundedProcessResult Run(ProcessStartInfo startInfo, TimeSpan timeout);
}

internal interface IBoundedProcessFactory
{
    IBoundedProcess Create(ProcessStartInfo startInfo);
}

internal interface IBoundedProcess : IDisposable
{
    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    int ExitCode { get; }

    bool Start();

    Task WaitForExitAsync();

    void Kill();
}

internal sealed record BoundedProcessResult(
    bool Started,
    bool Exited,
    bool TimedOut,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated)
{
    public bool Succeeded => Started && Exited && !TimedOut && ExitCode == 0;
}
