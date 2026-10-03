using Avalonia.Threading;

namespace DraftTG.App;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action) =>
        await Dispatcher.UIThread.InvokeAsync(action);
}
