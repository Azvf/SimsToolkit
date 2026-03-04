using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task AsyncRelayCommand_DoesNotStickWhenCanExecuteChangedHandlerThrows()
    {
        var runCount = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            Interlocked.Increment(ref runCount);
            await Task.Yield();
        });
        command.CanExecuteChanged += (_, _) => throw new InvalidOperationException("handler failure");

        command.Execute(null);
        await WaitForAsync(() => Volatile.Read(ref runCount) == 1);
        await WaitForAsync(() => command.CanExecute(null));
        Assert.True(command.CanExecute(null));

        command.Execute(null);
        await WaitForAsync(() => Volatile.Read(ref runCount) == 2);
        await WaitForAsync(() => command.CanExecute(null));
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommandOfT_DoesNotStickWhenCanExecuteChangedHandlerThrows()
    {
        var runCount = 0;
        var command = new AsyncRelayCommand<int>(async _ =>
        {
            Interlocked.Increment(ref runCount);
            await Task.Yield();
        });
        command.CanExecuteChanged += (_, _) => throw new InvalidOperationException("handler failure");

        command.Execute(1);
        await WaitForAsync(() => Volatile.Read(ref runCount) == 1);
        await WaitForAsync(() => command.CanExecute(1));
        Assert.True(command.CanExecute(1));

        command.Execute(2);
        await WaitForAsync(() => Volatile.Read(ref runCount) == 2);
        await WaitForAsync(() => command.CanExecute(2));
        Assert.True(command.CanExecute(2));
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMs)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
