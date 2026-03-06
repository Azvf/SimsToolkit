namespace SimsModDesktop.Presentation.Services;

public sealed class UiActivityMonitor : IUiActivityMonitor
{
    private long _lastInteractionUtcTicks = DateTimeOffset.UtcNow.UtcTicks;

    public DateTimeOffset LastInteractionUtc =>
        new(Interlocked.Read(ref _lastInteractionUtcTicks), TimeSpan.Zero);

    public void RecordInteraction()
    {
        Interlocked.Exchange(ref _lastInteractionUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
