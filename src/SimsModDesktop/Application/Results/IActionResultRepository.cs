using System.ComponentModel;

namespace SimsModDesktop.Application.Results;

public interface IActionResultRepository : INotifyPropertyChanged
{
    ActionResultEnvelope? Latest { get; }
    IReadOnlyList<ActionResultEnvelope> History { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ActionResultEnvelope envelope, string? relatedOperationId = null, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    void Save(ActionResultEnvelope envelope);
    void Clear();
}
