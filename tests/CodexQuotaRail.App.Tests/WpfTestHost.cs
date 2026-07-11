using System.Windows;
using System.Windows.Threading;

namespace CodexQuotaRail.App.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfTestGroup : ICollectionFixture<WpfTestHost>
{
    public const string Name = "WPF STA";
}

public sealed class WpfTestHost : IDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private int _disposed;

    public WpfTestHost()
    {
        _thread = new Thread(RunDispatcher)
        {
            IsBackground = true,
            Name = "CodexQuotaRail.WpfTests",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = _ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _dispatcher!.InvokeAsync(action, DispatcherPriority.Send).Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            dispatcher.Invoke(
                () =>
                {
                    if (Application.Current is not { } application)
                    {
                        return;
                    }

                    foreach (var window in application.Windows.Cast<Window>().ToArray())
                    {
                        window.Close();
                    }

                    application.Shutdown();
                });
            if (!dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        }

        Assert.True(_thread.Join(TimeSpan.FromSeconds(5)));
        _dispatcher = null;
    }

    private void RunDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        _ready.TrySetResult(dispatcher);
        Dispatcher.Run();
    }
}
