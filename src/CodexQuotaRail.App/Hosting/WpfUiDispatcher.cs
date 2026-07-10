using System.Windows.Threading;

namespace CodexQuotaRail.App.Hosting;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public ValueTask InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            action();
            return ValueTask.CompletedTask;
        }

        return new ValueTask(
            _dispatcher.InvokeAsync(
                    action,
                    DispatcherPriority.DataBind,
                    cancellationToken)
                .Task);
    }
}
