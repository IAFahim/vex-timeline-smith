using FluentAssertions;
using Vex.TimelineSmith.Compiler;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;

namespace Vex.TimelineSmith.Tests;

public class Phase1Tests
{
    private static string SamplePath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples", "light_attack", "light_attack.timeline.yaml"));

    private static TimelineTables LoadSample()
    {
        var r = TimelineLoader.LoadFile(SamplePath);
        r.IsSuccess.Should().BeTrue(string.Join("; ", r.Errors.Select(e => e.ToString())));
        return TimelineTables.FromDocument(r.Value!);
    }

    [Fact]
    public void Load_light_attack_ok()
    {
        var tables = LoadSample();
        tables.DurationTicks.Should().Be(48);
        tables.TrackCount.Should().Be(2);
        tables.Boundaries.Should().NotBeEmpty();
    }

    [Fact]
    public void Play_to_end_agent_equals_oracle()
    {
        var tables = LoadSample();
        var script = new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() };
        var a = new TimelineAgent(tables).RunScript(script);
        var o = Oracle.Run(tables, script);
        a.CanonicalString().Should().Be(o.CanonicalString());
    }

    [Fact]
    public void Active_set_matches_half_open_windows_every_frame()
    {
        var tables = LoadSample();
        var script = new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() };
        var log = new TimelineAgent(tables).RunScript(script);
        foreach (var f in log.Frames)
        {
            var expected = Oracle.ActiveSetAt(tables, f.Tick, f.Mode);
            f.ActiveByTrack.Should().Equal(expected);
        }
    }

    [Fact]
    public void Abutting_clips_exit_then_enter_same_tick()
    {
        var tables = LoadSample();
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.Advance(8),
        });

        var frame = log.Frames.FirstOrDefault(f => f.Edges.Any(e => e.Clip == "startup" && e.Kind == EdgeKind.Exit));
        frame.Should().NotBeNull();
        var edges = frame!.Edges.Where(e => e.Track == "body").ToList();
        var exitIdx = edges.FindIndex(e => e.Clip == "startup" && e.Kind == EdgeKind.Exit);
        var enterIdx = edges.FindIndex(e => e.Clip == "active" && e.Kind == EdgeKind.Enter);
        exitIdx.Should().BeGreaterThanOrEqualTo(0);
        enterIdx.Should().BeGreaterThanOrEqualTo(0);
        exitIdx.Should().BeLessThan(enterIdx);
        frame.Tick.Should().Be(7);
    }

    [Fact]
    public void Multi_track_aligned_starts_same_tick_ordered_by_track_name()
    {
        var yaml = """
            schema_id: vex.timeline-ir
            schema_version: 1
            id: align
            time: { unit: ticks, tick_hz: 60 }
            director: { duration_ticks: 10, wrap: hold, sample_last_frame: true, initial_time_ticks: 0 }
            tracks:
              - id: body
                binding_slot: self
                overlap: forbid
                clips:
                  - id: a
                    start_tick: 3
                    end_tick: 8
                    kind: phase
                    payload: { phase: a }
              - id: vfx
                binding_slot: self
                overlap: forbid
                clips:
                  - id: b
                    start_tick: 3
                    end_tick: 8
                    kind: signal
                    payload: { fx: b }
            """;
        var doc = TimelineLoader.LoadYaml(yaml);
        doc.IsSuccess.Should().BeTrue(string.Join("; ", doc.Errors));
        var tables = TimelineTables.FromDocument(doc.Value!);
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.Advance(4),
        });
        var f = log.Frames.Single(x => x.Edges.Count >= 2 && x.Edges.Any(e => e.Kind == EdgeKind.Enter));
        f.Tick.Should().Be(3);
        var enters = f.Edges.Where(e => e.Kind == EdgeKind.Enter).Select(e => e.Track).ToList();
        enters.Should().Equal("body", "vfx");
    }

    [Fact]
    public void Pause_mid_clip_freezes_time_and_actives_resume_no_reenter()
    {
        var tables = LoadSample();
        var agent = new TimelineAgent(tables);
        _ = agent.Play();
        TraceFrame? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = agent.Tick();
        }

        agent.TimeTicks.Should().Be(10);
        agent.Mode.Should().Be(DirectorMode.Playing);
        last!.ActiveByTrack[0].Should().Be("active");

        agent.Pause();
        var tPause = agent.TimeTicks;
        var activePause = last.ActiveByTrack[0];
        for (var i = 0; i < 5; i++)
        {
            var f = agent.Tick();
            f.Tick.Should().Be(tPause);
            f.Mode.Should().Be(DirectorMode.Paused);
            f.ActiveByTrack[0].Should().Be(activePause);
            f.Edges.Should().BeEmpty();
        }

        // Script Advance while paused: no frames added / time frozen
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.Advance(10),
            new ScriptOp.Pause(),
            new ScriptOp.Advance(5),
        });
        log.Frames.Last(f => f.Mode == DirectorMode.Paused || f.Tick == 10).Tick.Should().Be(10);
        log.Frames.Count(f => f.Tick > 10).Should().Be(0);

        agent.Resume();
        var after = agent.Tick();
        after.Mode.Should().Be(DirectorMode.Playing);
        after.Tick.Should().Be(tPause + 1);
        after.Edges.Should().NotContain(e => e.Clip == "active" && e.Kind == EdgeKind.Enter);
    }

    [Fact]
    public void Play_public_api_enters_at_initial_time_zero()
    {
        var tables = LoadSample();
        var agent = new TimelineAgent(tables);
        var playFrame = agent.Play();
        playFrame.Tick.Should().Be(0);
        playFrame.Edges.Should().Contain(e => e.Clip == "startup" && e.Kind == EdgeKind.Enter);
        playFrame.ActiveByTrack[0].Should().Be("startup");
        agent.TimeTicks.Should().Be(0);
    }

    [Fact]
    public void Hold_complete_no_spurious_reenter_of_last_frame_clip()
    {
        var tables = LoadSample();
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        var end = log.Frames.Last();
        end.Mode.Should().Be(DirectorMode.HoldComplete);
        end.Tick.Should().Be(48);
        // sample_last_frame: recovery still displayed
        end.ActiveByTrack[0].Should().Be("recovery");
        // no re-Enter recovery on hold frame
        end.Edges.Should().NotContain(e => e.Clip == "recovery" && e.Kind == EdgeKind.Enter);
        end.Edges.Should().Contain(e => e.Clip == "recovery" && e.Kind == EdgeKind.Exit);
        end.NamedEvents.Select(n => n.Name).Should().Contain("ATTACK_DONE");
    }

    [Fact]
    public void Hold_complete_fires_ATTACK_DONE()
    {
        var tables = LoadSample();
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        log.Frames.Last().Mode.Should().Be(DirectorMode.HoldComplete);
        log.Frames.SelectMany(f => f.NamedEvents).Select(n => n.Name)
            .Should().Contain("ATTACK_DONE");
        log.Frames.SelectMany(f => f.NamedEvents).Select(n => n.Name)
            .Should().Contain("ACTIVE_BEGIN")
            .And.Contain("ACTIVE_END");
    }

    [Fact]
    public void Determinism_two_runs_identical()
    {
        var tables = LoadSample();
        var script = new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() };
        var a = new TimelineAgent(tables).RunScript(script).CanonicalString();
        var b = new TimelineAgent(tables).RunScript(script).CanonicalString();
        a.Should().Be(b);
    }

    [Fact]
    public void Multi_agent_isolation_64()
    {
        var tables = LoadSample();
        var script = new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() };
        var traces = Enumerable.Range(0, 64)
            .Select(_ => new TimelineAgent(tables).RunScript(script).CanonicalString())
            .ToList();
        traces.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Compile_succeeds()
    {
        var r = Emitter.CompileFile(SamplePath);
        r.IsSuccess.Should().BeTrue(string.Join("; ", r.Errors.Select(e => e.ToString())));
        r.Value!.Tables.Id.Should().Be("attack_light");
        r.Value.AgentSource.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Overlap_rejected()
    {
        var yaml = """
            schema_id: vex.timeline-ir
            schema_version: 1
            id: bad
            time: { unit: ticks, tick_hz: 60 }
            director: { duration_ticks: 10, wrap: hold, initial_time_ticks: 0 }
            tracks:
              - id: body
                clips:
                  - id: a
                    start_tick: 0
                    end_tick: 5
                    kind: phase
                    payload: { phase: a }
                  - id: b
                    start_tick: 3
                    end_tick: 8
                    kind: phase
                    payload: { phase: b }
            """;
        var r = TimelineLoader.LoadYaml(yaml);
        r.IsSuccess.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Code == "overlap");
    }

    [Fact]
    public void Initial_time_equal_duration_rejected()
    {
        var yaml = """
            schema_id: vex.timeline-ir
            schema_version: 1
            id: bad_init
            time: { unit: ticks, tick_hz: 60 }
            director: { duration_ticks: 10, wrap: hold, initial_time_ticks: 10 }
            tracks:
              - id: body
                clips:
                  - id: a
                    start_tick: 0
                    end_tick: 5
                    kind: phase
                    payload: { phase: a }
            """;
        var r = TimelineLoader.LoadYaml(yaml);
        r.IsSuccess.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Path.Contains("initial_time"));
    }

    [Fact]
    public void Missing_file_is_ir_error_not_throw()
    {
        var r = TimelineLoader.LoadFile("/nonexistent/path/nope.timeline.yaml");
        r.IsSuccess.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Code == "io");
    }
}
