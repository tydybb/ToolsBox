using System.Windows.Threading;

namespace ToolsBox.App.NetworkTraffic;

public interface INetworkTrafficDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class WpfNetworkTrafficDispatcher(Dispatcher dispatcher) : INetworkTrafficDispatcher
{
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}
