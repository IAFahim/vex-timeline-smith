using Vex.TimelineSmith.Ir;

namespace Vex.TimelineSmith.Runtime;

/// <summary>
/// Offline timeline agent: integer clock + half-open membership Diff + ordered edges.
/// StateSmith is not used on this hot path. Boundaries[] is a compile/dump schedule only.
/// </summary>
public sealed class TimelineAgent
{
    private readonly TimelineTables _tables;
    private readonly string?[] _activeByTrack;

    public long TimeTicks { get; private set; }
    public DirectorMode Mode { get; private set; } = DirectorMode.Stopped;
    public TimelineTables Tables => _tables;

    public TimelineAgent(TimelineTables tables)
    {
        _tables = tables;
        _activeByTrack = new string?[tables.TrackCount];
        TimeTicks = tables.InitialTimeTicks;
    }

    /// <summary>
    /// Enter Playing and establish membership at current time (zero-advance).
    /// Returns the observation frame including Enter edges for windows covering <see cref="TimeTicks"/>.
    /// </summary>
    public TraceFrame Play()
    {
        if (Mode is DirectorMode.HoldComplete or DirectorMode.Stopped)
        {
            if (Mode == DirectorMode.HoldComplete || TimeTicks >= _tables.DurationTicks)
            {
                ResetClocksAndActives();
            }
        }

        Mode = DirectorMode.Playing;
        return SnapshotAtCurrentTime();
    }

    public void Pause()
    {
        if (Mode == DirectorMode.Playing)
        {
            Mode = DirectorMode.Paused;
        }
    }

    public void Resume()
    {
        if (Mode == DirectorMode.Paused)
        {
            Mode = DirectorMode.Playing;
        }
    }

    public TraceFrame Stop()
    {
        Mode = DirectorMode.Stopped;
        var edges = new List<TraceEdge>();
        ForceExitAll(edges);
        OrderEdges(edges);
        TimeTicks = _tables.InitialTimeTicks;
        Array.Clear(_activeByTrack);
        return Frame(edges, new List<TraceNamedEvent>());
    }

    /// <summary>
    /// Hard seek: jump time, re-sync membership, emit Exit then Enter for changes (no director wrap).
    /// Mode becomes Playing unless currently HoldComplete/Stopped without play intent — seek implies Playing.
    /// </summary>
    public TraceFrame Seek(long timeTicks)
    {
        if (timeTicks < 0)
        {
            timeTicks = 0;
        }

        if (timeTicks > _tables.DurationTicks)
        {
            timeTicks = _tables.DurationTicks;
        }

        var edges = new List<TraceEdge>();
        var named = new List<TraceNamedEvent>();
        var prev = (string?[])_activeByTrack.Clone();

        TimeTicks = timeTicks;
        // Seek does not auto-fire wrap; if at/ past end, leave clock and let Tick/Play wrap policy apply on next advance.
        if (Mode is DirectorMode.Stopped or DirectorMode.HoldComplete)
        {
            Mode = DirectorMode.Playing;
        }

        if (Mode == DirectorMode.Paused)
        {
            // stay paused at new time
        }

        RefreshActiveFromMembership();
        DiffActives(prev, edges);
        OrderEdges(edges);
        MapNamedEvents(edges, named);
        return Frame(edges, named);
    }

    /// <summary>Hard reset to initial time, Stopped, all actives cleared (with Exit edges).</summary>
    public TraceFrame HardReset()
    {
        var edges = new List<TraceEdge>();
        ForceExitAll(edges);
        OrderEdges(edges);
        TimeTicks = _tables.InitialTimeTicks;
        Mode = DirectorMode.Stopped;
        Array.Clear(_activeByTrack);
        return Frame(edges, new List<TraceNamedEvent>());
    }

    /// <summary>Advance one fixed tick when Playing. Returns frame observation.</summary>
    public TraceFrame Tick()
    {
        var edges = new List<TraceEdge>();
        var named = new List<TraceNamedEvent>();

        if (Mode == DirectorMode.Playing)
        {
            TimeTicks += 1;

            var prev = (string?[])_activeByTrack.Clone();
            RefreshActiveFromMembership();
            DiffActives(prev, edges);

            if (TimeTicks >= _tables.DurationTicks)
            {
                ApplyDirectorEnd(edges, named);
            }
            else
            {
                OrderEdges(edges);
                MapNamedEvents(edges, named);
            }
        }
        else
        {
            RefreshActiveFromMembership();
        }

        return Frame(edges, named);
    }

    /// <summary>
    /// Run scripted ops. Frame 0 after Play captures t=initial enters without advancing.
    /// </summary>
    public TraceLog RunScript(IReadOnlyList<ScriptOp> script, int maxTicks = 10_000)
    {
        var log = new TraceLog();
        ResetClocksAndActives();
        Mode = DirectorMode.Stopped;

        foreach (var op in script)
        {
            switch (op)
            {
                case ScriptOp.Play:
                    log.Frames.Add(Play());
                    break;
                case ScriptOp.Pause:
                    Pause();
                    log.Frames.Add(Frame(new List<TraceEdge>(), new List<TraceNamedEvent>()));
                    break;
                case ScriptOp.Resume:
                    Resume();
                    log.Frames.Add(Frame(new List<TraceEdge>(), new List<TraceNamedEvent>()));
                    break;
                case ScriptOp.Stop:
                    log.Frames.Add(Stop());
                    break;
                case ScriptOp.Seek s:
                    log.Frames.Add(Seek(s.TimeTicks));
                    break;
                case ScriptOp.HardReset:
                    log.Frames.Add(HardReset());
                    break;
                case ScriptOp.Advance adv:
                    for (var i = 0; i < adv.Ticks && Mode == DirectorMode.Playing; i++)
                    {
                        log.Frames.Add(Tick());
                    }

                    break;
                case ScriptOp.RunToEnd:
                {
                    var guard = 0;
                    while (Mode == DirectorMode.Playing && guard++ < maxTicks)
                    {
                        log.Frames.Add(Tick());
                    }

                    break;
                }
            }
        }

        return log;
    }

    private TraceFrame SnapshotAtCurrentTime()
    {
        var edges = new List<TraceEdge>();
        var named = new List<TraceNamedEvent>();
        var prevActive = (string?[])_activeByTrack.Clone();
        RefreshActiveFromMembership();
        DiffActives(prevActive, edges);
        OrderEdges(edges);
        MapNamedEvents(edges, named);
        return Frame(edges, named);
    }

    private TraceFrame Frame(List<TraceEdge> edges, List<TraceNamedEvent> named) =>
        new()
        {
            Tick = TimeTicks,
            Mode = Mode,
            ActiveByTrack = (string?[])_activeByTrack.Clone(),
            Edges = edges,
            NamedEvents = named,
        };

    private void DiffActives(string?[] prev, List<TraceEdge> edges)
    {
        for (var t = 0; t < _tables.TrackCount; t++)
        {
            var before = prev[t];
            var after = _activeByTrack[t];
            if (before == after)
            {
                continue;
            }

            if (before is not null)
            {
                edges.Add(new TraceEdge(TimeTicks, _tables.TrackNames[t], before, EdgeKind.Exit));
            }

            if (after is not null)
            {
                edges.Add(new TraceEdge(TimeTicks, _tables.TrackNames[t], after, EdgeKind.Enter));
            }
        }
    }

    /// <summary>Global same-tick order: Exit before Enter, then ordinal track name.</summary>
    internal static void OrderEdges(List<TraceEdge> edges)
    {
        edges.Sort(static (a, b) =>
        {
            var ka = a.Kind == EdgeKind.Exit ? 0 : 1;
            var kb = b.Kind == EdgeKind.Exit ? 0 : 1;
            var c = ka.CompareTo(kb);
            if (c != 0)
            {
                return c;
            }

            return string.CompareOrdinal(a.Track, b.Track);
        });
    }

    private void RefreshActiveFromMembership()
    {
        Array.Clear(_activeByTrack);
        var t = TimeTicks;
        var evalT = t;
        if (Mode == DirectorMode.HoldComplete && _tables.SampleLastFrame)
        {
            evalT = Math.Max(0, _tables.DurationTicks - 1);
        }

        if (Mode is DirectorMode.Stopped)
        {
            return;
        }

        if (Mode is DirectorMode.Playing or DirectorMode.Paused or DirectorMode.HoldComplete)
        {
            foreach (var clip in _tables.Clips)
            {
                if (evalT >= clip.StartTick && evalT < clip.EndTick)
                {
                    _activeByTrack[clip.TrackIndex] = clip.ClipName;
                }
            }
        }
    }

    private void ApplyDirectorEnd(List<TraceEdge> edges, List<TraceNamedEvent> named)
    {
        if (Mode != DirectorMode.Playing)
        {
            return;
        }

        if (TimeTicks < _tables.DurationTicks)
        {
            return;
        }

        switch (_tables.Wrap)
        {
            case WrapPolicy.Hold:
                TimeTicks = _tables.DurationTicks;
                Mode = DirectorMode.HoldComplete;
                // Membership Diff already ran at t=duration while Playing (empty half-open → Exits).
                // sample_last_frame: hold display at duration-1 WITHOUT re-Enter edges.
                RefreshActiveFromMembership();
                OrderEdges(edges);
                MapNamedEvents(edges, named);
                AppendDirectorNamed(DirectorSignal.HoldReached, named);
                break;

            case WrapPolicy.None:
                TimeTicks = _tables.DurationTicks;
                Mode = DirectorMode.Stopped;
                ForceExitAll(edges);
                OrderEdges(edges);
                MapNamedEvents(edges, named);
                AppendDirectorNamed(DirectorSignal.RangeStopped, named);
                break;

            case WrapPolicy.Loop:
                // Exits at duration already in edges from half-open empty Diff.
                TimeTicks = 0;
                var prevLoop = (string?[])_activeByTrack.Clone();
                RefreshActiveFromMembership();
                DiffActives(prevLoop, edges);
                OrderEdges(edges);
                MapNamedEvents(edges, named);
                AppendDirectorNamed(DirectorSignal.Looped, named);
                break;
        }
    }

    private void AppendDirectorNamed(DirectorSignal signal, List<TraceNamedEvent> named)
    {
        foreach (var (name, when) in _tables.NamedEvents)
        {
            if (when is EventWhen.Director d && d.Signal == signal)
            {
                named.Add(new TraceNamedEvent(TimeTicks, name));
            }
        }
    }

    private void MapNamedEvents(List<TraceEdge> edges, List<TraceNamedEvent> named)
    {
        foreach (var (name, when) in _tables.NamedEvents)
        {
            if (when is not EventWhen.ClipEdge ce)
            {
                continue;
            }

            foreach (var e in edges)
            {
                if (e.Track == ce.Track.Name && e.Clip == ce.Clip.Name && e.Kind == ce.Edge)
                {
                    named.Add(new TraceNamedEvent(TimeTicks, name));
                }
            }
        }
    }

    private void ForceExitAll(List<TraceEdge>? edges = null)
    {
        for (var t = 0; t < _tables.TrackCount; t++)
        {
            if (_activeByTrack[t] is { } clip)
            {
                edges?.Add(new TraceEdge(TimeTicks, _tables.TrackNames[t], clip, EdgeKind.Exit));
                _activeByTrack[t] = null;
            }
        }
    }

    private void ResetClocksAndActives()
    {
        TimeTicks = _tables.InitialTimeTicks;
        Array.Clear(_activeByTrack);
    }
}

public abstract record ScriptOp
{
    public sealed record Play : ScriptOp;

    public sealed record Pause : ScriptOp;

    public sealed record Resume : ScriptOp;

    public sealed record Stop : ScriptOp;

    public sealed record Advance(int Ticks) : ScriptOp;

    public sealed record RunToEnd : ScriptOp;

    public sealed record Seek(long TimeTicks) : ScriptOp;

    public sealed record HardReset : ScriptOp;
}
