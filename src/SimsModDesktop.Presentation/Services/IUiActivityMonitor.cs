namespace SimsModDesktop.Presentation.Services;

public interface IUiActivityMonitor
{
    DateTimeOffset LastInteractionUtc { get; }

    void RecordInteraction();
}
