using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

public class PlayableTimingDiagramTests
{
    [Fact]
    public void Generates_frames_with_60f_major_and_clip_edge_markers()
    {
        var puml = PlayableTimingDiagram.GenerateFromFile(PlayablePaths.TmpTimeline);
        puml.Should().Contain("manual time-axis");
        // per-frame width (no pad to next 60f); timegrid must be hidden
        puml.Should().Contain("scale 1 as ");
        puml.Should().Contain("timegrid");
        puml.Should().Contain("LineColor transparent");
        // Axis: majors + duration end only (dense edge labels cluttered the bottom)
        puml.Should().Contain("@0 as :0");
        puml.Should().Contain("@60 as :60");
        puml.Should().Contain("@120 as :120");
        puml.Should().Contain("@169 as :169"); // duration end
        puml.Should().NotContain("@109 as :109"); // clip edge — state diamond only
        // Clip edges still drive state rows (bare @f, not @:f — no axis label)
        puml.Should().Contain("\n@109\n");
        puml.Should().Contain("\n@169\n");
        // single-line major guides every 60f including 120
        puml.Should().Contain("highlight 60 to 60.01");
        puml.Should().Contain("highlight 0 to 0.01");
        puml.Should().Contain("highlight 120 to 120.01");
        puml.Should().Contain("<->");
        puml.Should().Contain(": 60");
        puml.Should().NotContain("{60");
        puml.Should().Contain("concise ");
        puml.Should().NotContain(" is Idle");
        puml.Should().NotContain("title ");
        puml.Should().NotContain("@1.817");
        puml.Should().NotContain("@:0");
    }

    [Fact]
    public void Unchanged_tracks_are_not_reemitted_at_foreign_edges()
    {
        // Long clip on one track + short clips on another must not re-declare the long clip
        // at every short-clip edge (that chopped Animation into unreadable capsules).
        var report = PlayableInspector.InspectFile(PlayablePaths.CanSubTimeline);
        var sim = PlayableRun.FromReport(report);
        var puml = PlayableTimingDiagram.Generate(sim);
        // Animation is continuous 0→491: only start (+ maybe blend) and end should set it
        var animLines = puml.Split('\n')
            .Where(l => l.StartsWith("AnimationTrack_0 is ", StringComparison.Ordinal))
            .ToList();
        animLines.Count.Should().BeLessThanOrEqualTo(3,
            "long continuous Animation must not be re-declared at every Audio/Particle edge");
        animLines.Should().Contain(l => l.Contains("Can Anim", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_axis_step_is_fps()
    {
        PlayableTimingDiagram.DefaultAxisLabelStep(60).Should().Be(60);
        PlayableTimingDiagram.DefaultAxisLabelStep(30).Should().Be(30);
        PlayableTimingDiagram.PickPixelsPerFrame(60, 18).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Theme_is_injected_after_startuml()
    {
        var puml = PlayableTimingDiagram.GenerateFromFile(
            PlayablePaths.TmpTimeline,
            new PlayableTimingDiagram.Options(Theme: "cyborg"));
        var lines = puml.Split('\n');
        lines[0].Trim().Should().Be("@startuml");
        lines[1].Trim().Should().Be("!theme cyborg");
    }

    [Fact]
    public void Blend_fields_parsed_and_markers_emitted_when_nonzero()
    {
        // synthetic: build sim with blend
        var report = PlayableInspector.InspectFile(PlayablePaths.TmpTimeline);
        var sim = PlayableRun.FromReport(report);
        // sample has blend 0 — inject by constructing flat clip
        var clip = sim.Clips[0];
        // 30f blend-in is long enough for an "(in)" label
        var withBlend = clip with { BlendInFrames = 30, BlendOutFrames = 5 };
        var sim2 = sim with { Clips = new[] { withBlend } };
        var puml = PlayableTimingDiagram.Generate(sim2);
        puml.Should().Contain(": 30");
        puml.Should().Contain("(in)"); // blend-in state label
        puml.Should().Contain("\n@139\n"); // 109+30 blend-in end as state edge
    }
}
