namespace Vex.TimelineSmith.Runtime;

/// <summary>
/// Pure gameplay consumer of named timeline events (StateSmith host or stub).
/// Drains in FIFO order; no StateSmith dependency.
/// </summary>
public sealed class EventInbox
{
    private readonly Queue<TraceNamedEvent> _q = new();

    public int Count => _q.Count;

    public void PushFrame(TraceFrame frame)
    {
        foreach (var n in frame.NamedEvents)
        {
            _q.Enqueue(n);
        }
    }

    public void PushLog(TraceLog log)
    {
        foreach (var f in log.Frames)
        {
            PushFrame(f);
        }
    }

    public bool TryDequeue(out TraceNamedEvent ev) => _q.TryDequeue(out ev);

    public IReadOnlyList<string> DrainNames()
    {
        var list = new List<string>(_q.Count);
        while (_q.TryDequeue(out var ev))
        {
            list.Add(ev.Name);
        }

        return list;
    }

    public void Clear() => _q.Clear();
}
