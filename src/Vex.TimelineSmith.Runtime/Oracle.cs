using Vex.TimelineSmith.Ir;

namespace Vex.TimelineSmith.Runtime;

/// <summary>
/// Reference evaluator: pure membership + scripted director. No StateSmith.
/// Shares the same semantics as <see cref="TimelineAgent"/> for Phase 1.
/// </summary>
public static class Oracle
{
    public static TraceLog Run(TimelineTables tables, IReadOnlyList<ScriptOp> script, int maxTicks = 10_000)
    {
        var agent = new TimelineAgent(tables);
        return agent.RunScript(script, maxTicks);
    }

    public static string?[] ActiveSetAt(TimelineTables tables, long timeTicks, DirectorMode mode)
    {
        var active = new string?[tables.TrackCount];
        if (mode is DirectorMode.Stopped)
        {
            return active;
        }

        var evalT = timeTicks;
        if (mode == DirectorMode.HoldComplete && tables.SampleLastFrame)
        {
            evalT = Math.Max(0, tables.DurationTicks - 1);
        }

        foreach (var clip in tables.Clips)
        {
            if (evalT >= clip.StartTick && evalT < clip.EndTick)
            {
                active[clip.TrackIndex] = clip.ClipName;
            }
        }

        return active;
    }
}
