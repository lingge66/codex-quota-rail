namespace CodexQuotaRail.AppServer.Transport;

public interface IJsonLineTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);

    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
}
