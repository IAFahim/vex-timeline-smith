namespace Vex.TimelineSmith.Runtime;

/// <summary>
/// Batch tick over many agents sharing the same tables (Phase 4 mass-sim path).
/// Per-agent state is independent; tables are read-only shared.
/// </summary>
public sealed class BatchAgents
{
    private readonly TimelineAgent[] _agents;

    public BatchAgents(TimelineTables tables, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _agents = new TimelineAgent[count];
        for (var i = 0; i < count; i++)
        {
            _agents[i] = new TimelineAgent(tables);
        }
    }

    public int Count => _agents.Length;

    public TimelineAgent this[int index] => _agents[index];

    public void PlayAll()
    {
        for (var i = 0; i < _agents.Length; i++)
        {
            _ = _agents[i].Play();
        }
    }

    /// <summary>Tick every agent that is still Playing. Returns frames in agent index order.</summary>
    public TraceFrame[] TickAll()
    {
        var frames = new TraceFrame[_agents.Length];
        for (var i = 0; i < _agents.Length; i++)
        {
            frames[i] = _agents[i].Tick();
        }

        return frames;
    }

    public TraceLog[] RunScriptAll(IReadOnlyList<ScriptOp> script, int maxTicks = 10_000)
    {
        var logs = new TraceLog[_agents.Length];
        for (var i = 0; i < _agents.Length; i++)
        {
            logs[i] = _agents[i].RunScript(script, maxTicks);
        }

        return logs;
    }
}
