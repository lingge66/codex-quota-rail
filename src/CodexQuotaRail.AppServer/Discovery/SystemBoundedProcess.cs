using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

internal sealed class SystemBoundedProcessFactory : IBoundedProcessFactory
{
    public IBoundedProcess Create(ProcessStartInfo startInfo) =>
        new SystemBoundedProcess(startInfo);
}

internal sealed class SystemBoundedProcess : IBoundedProcess
{
    private readonly Process _process;

    public SystemBoundedProcess(ProcessStartInfo startInfo)
    {
        _process = new Process { StartInfo = startInfo };
    }

    public TextReader StandardOutput => _process.StandardOutput;

    public TextReader StandardError => _process.StandardError;

    public int ExitCode => _process.ExitCode;

    public bool Start() => _process.Start();

    public Task WaitForExitAsync() => _process.WaitForExitAsync(CancellationToken.None);

    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose() => _process.Dispose();
}
