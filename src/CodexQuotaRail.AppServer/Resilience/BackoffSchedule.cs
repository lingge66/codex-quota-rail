namespace CodexQuotaRail.AppServer.Resilience;

public sealed class BackoffSchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    private int _index;

    public TimeSpan NextDelay()
    {
        var current = Math.Min(_index, Delays.Length - 1);
        if (_index < Delays.Length - 1)
        {
            _index++;
        }

        return Delays[current];
    }

    public void Reset() => _index = 0;
}
