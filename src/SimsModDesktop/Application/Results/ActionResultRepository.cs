using System.ComponentModel;

namespace SimsModDesktop.Application.Results;

public sealed class ActionResultRepository : IActionResultRepository
{
    private readonly List<ActionResultEnvelope> _history = [];
    private ActionResultEnvelope? _latest;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ActionResultEnvelope? Latest => _latest;
    public IReadOnlyList<ActionResultEnvelope> History => _history;

    public void Save(ActionResultEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _latest = envelope;
        _history.Insert(0, envelope);
        if (_history.Count > 20)
        {
            _history.RemoveRange(20, _history.Count - 20);
        }

        Raise(nameof(Latest));
        Raise(nameof(History));
    }

    public void Clear()
    {
        _latest = null;
        _history.Clear();
        Raise(nameof(Latest));
        Raise(nameof(History));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
