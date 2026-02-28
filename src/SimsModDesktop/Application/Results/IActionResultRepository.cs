using System.ComponentModel;

namespace SimsModDesktop.Application.Results;

public interface IActionResultRepository : INotifyPropertyChanged
{
    ActionResultEnvelope? Latest { get; }
    IReadOnlyList<ActionResultEnvelope> History { get; }

    void Save(ActionResultEnvelope envelope);
    void Clear();
}
