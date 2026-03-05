using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Diagnostics;

internal sealed class StartupLogBuffer
{
    private readonly object _sync = new();
    private readonly List<StartupBufferedLogEntry> _entries = [];

    public void Add(string eventName, string status, string milestone, long elapsedMs)
    {
        lock (_sync)
        {
            _entries.Add(new StartupBufferedLogEntry(eventName, status, milestone, elapsedMs));
        }
    }

    public IReadOnlyList<StartupBufferedLogEntry> Drain()
    {
        lock (_sync)
        {
            if (_entries.Count == 0)
            {
                return Array.Empty<StartupBufferedLogEntry>();
            }

            var snapshot = _entries.ToArray();
            _entries.Clear();
            return snapshot;
        }
    }
}

internal readonly record struct StartupBufferedLogEntry(
    string Event,
    string Status,
    string Milestone,
    long ElapsedMs);
