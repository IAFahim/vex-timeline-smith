using FluentAssertions;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;

namespace Vex.TimelineSmith.Tests;

public class Phase2Tests
{
    private static TimelineTables LoadYaml(string yaml)
    {
        var doc = TimelineLoader.LoadYaml(yaml);
        doc.IsSuccess.Should().BeTrue(string.Join("; ", doc.Errors));
        return TimelineTables.FromDocument(doc.Value!);
    }

    private static string Mini(string wrap, string? extraEvents = null) => $$"""
        schema_id: vex.timeline-ir
        schema_version: 1
        id: wrap_{{wrap}}
        time: { unit: ticks, tick_hz: 60 }
        director: { duration_ticks: 5, wrap: {{wrap}}, sample_last_frame: true, initial_time_ticks: 0 }
        tracks:
          - id: body
            clips:
              - id: clip_a
                start_tick: 0
                end_tick: 5
                kind: phase
                payload: { phase: a }
        events:
          - name: HOLD_EVT
            when: { director: hold_reached }
          - name: STOP_EVT
            when: { director: range_stopped }
          - name: LOOP_EVT
            when: { director: looped }
          - name: ENTER_A
            when: { track: body, clip: clip_a, edge: enter }
          - name: EXIT_A
            when: { track: body, clip: clip_a, edge: exit }
        {{extraEvents}}
        """;

    [Fact]
    public void Wrap_hold_HoldComplete_and_hold_reached()
    {
        var tables = LoadYaml(Mini("hold"));
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        var end = log.Frames.Last();
        end.Mode.Should().Be(DirectorMode.HoldComplete);
        end.Tick.Should().Be(5);
        end.NamedEvents.Select(n => n.Name).Should().Contain("HOLD_EVT");
        end.NamedEvents.Select(n => n.Name).Should().NotContain("STOP_EVT");
        end.Edges.Should().NotContain(e => e.Kind == EdgeKind.Enter);
        end.ActiveByTrack[0].Should().Be("clip_a"); // sample last
    }

    [Fact]
    public void Wrap_none_Stopped_and_range_stopped()
    {
        var tables = LoadYaml(Mini("none"));
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        var end = log.Frames.Last();
        end.Mode.Should().Be(DirectorMode.Stopped);
        end.Tick.Should().Be(5);
        end.NamedEvents.Select(n => n.Name).Should().Contain("STOP_EVT");
        end.ActiveByTrack[0].Should().BeNull();
    }

    [Fact]
    public void Wrap_loop_reenters_at_zero_and_looped()
    {
        var tables = LoadYaml(Mini("loop"));
        var agent = new TimelineAgent(tables);
        _ = agent.Play();
        TraceFrame? loopFrame = null;
        for (var i = 0; i < 6; i++)
        {
            var f = agent.Tick();
            if (f.NamedEvents.Any(n => n.Name == "LOOP_EVT"))
            {
                loopFrame = f;
                break;
            }
        }

        loopFrame.Should().NotBeNull();
        loopFrame!.Tick.Should().Be(0);
        loopFrame.Mode.Should().Be(DirectorMode.Playing);
        loopFrame.Edges.Should().Contain(e => e.Clip == "clip_a" && e.Kind == EdgeKind.Exit);
        loopFrame.Edges.Should().Contain(e => e.Clip == "clip_a" && e.Kind == EdgeKind.Enter);
        loopFrame.NamedEvents.Select(n => n.Name).Should().Contain("LOOP_EVT");
        // clip re-enter maps ENTER_A
        loopFrame.NamedEvents.Select(n => n.Name).Should().Contain("ENTER_A");
        loopFrame.ActiveByTrack[0].Should().Be("clip_a");
    }

    [Fact]
    public void Seek_jumps_and_diffs_membership()
    {
        var tables = LoadYaml(Mini("hold"));
        var agent = new TimelineAgent(tables);
        _ = agent.Play();
        agent.Tick(); // t=1
        var seek = agent.Seek(3);
        seek.Tick.Should().Be(3);
        seek.Mode.Should().Be(DirectorMode.Playing);
        seek.ActiveByTrack[0].Should().Be("clip_a");
        // still same clip — no exit/enter if continuous
        seek.Edges.Should().BeEmpty();

        var seekOut = agent.Seek(5);
        seekOut.Tick.Should().Be(5);
        // half-open empty at duration while Playing
        seekOut.ActiveByTrack[0].Should().BeNull();
        seekOut.Edges.Should().Contain(e => e.Kind == EdgeKind.Exit);
    }

    [Fact]
    public void HardReset_clears_to_stopped_initial()
    {
        var tables = LoadYaml(Mini("hold"));
        var agent = new TimelineAgent(tables);
        _ = agent.Play();
        for (var i = 0; i < 3; i++)
        {
            agent.Tick();
        }

        var reset = agent.HardReset();
        agent.Mode.Should().Be(DirectorMode.Stopped);
        agent.TimeTicks.Should().Be(0);
        reset.Edges.Should().Contain(e => e.Kind == EdgeKind.Exit);
        agent.Play().Edges.Should().Contain(e => e.Kind == EdgeKind.Enter);
    }

    [Fact]
    public void Named_events_drain_into_gameplay_inbox()
    {
        var tables = LoadYaml(Mini("hold"));
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        var inbox = new EventInbox();
        inbox.PushLog(log);
        var names = inbox.DrainNames();
        names.Should().Contain("ENTER_A");
        names.Should().Contain("EXIT_A");
        names.Should().Contain("HOLD_EVT");
        inbox.Count.Should().Be(0);
    }

    [Fact]
    public void Script_seek_and_hard_reset_ops()
    {
        var tables = LoadYaml(Mini("hold"));
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.Advance(2),
            new ScriptOp.Seek(4),
            new ScriptOp.HardReset(),
            new ScriptOp.Play(),
            new ScriptOp.Advance(1),
        });
        log.Frames.Should().Contain(f => f.Tick == 4);
        log.Frames.Last().Tick.Should().Be(1);
        log.Frames.Should().Contain(f => f.Mode == DirectorMode.Stopped);
    }
}
