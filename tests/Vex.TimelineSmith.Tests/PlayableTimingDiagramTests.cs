using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

public class PlayableTimingDiagramTests
{
    private static string TmpTimeline =>
        "/home/i/GitHub/com.vex.unity.vex-ee/Assets/tmpTimeline.playable";

    [Fact]
    public void Generates_frames_with_60f_major_and_clip_edge_markers()
    {
        var puml = PlayableTimingDiagram.GenerateFromFile(TmpTimeline);
        puml.Should().Contain("manual time-axis");
        // per-frame width (no pad to next 60f); timegrid must be hidden
        puml.Should().Contain("scale 1 as ");
        puml.Should().Contain("timegrid");
        puml.Should().Contain("LineColor transparent");
        puml.Should().Contain("@0 as :0");
        puml.Should().Contain("@60 as :60");
        puml.Should().Contain("@109 as :109");
        puml.Should().Contain("@169 as :169");
        puml.Should().NotContain("@120 as :120");
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
            TmpTimeline,
            new PlayableTimingDiagram.Options(Theme: "cyborg"));
        var lines = puml.Split('\n');
        lines[0].Trim().Should().Be("@startuml");
        lines[1].Trim().Should().Be("!theme cyborg");
    }

    [Fact]
    public void Blend_fields_parsed_and_markers_emitted_when_nonzero()
    {
        // synthetic: build sim with blend
        var report = PlayableInspector.InspectFile(TmpTimeline);
        var sim = PlayableRun.FromReport(report);
        // sample has blend 0 — inject by constructing flat clip
        var clip = sim.Clips[0];
        var withBlend = clip with { BlendInFrames = 10, BlendOutFrames = 5 };
        var sim2 = sim with { Clips = new[] { withBlend } };
        var puml = PlayableTimingDiagram.Generate(sim2);
        // blend markers: bare frame counts only
        puml.Should().Contain(": 10");
        puml.Should().Contain(": 5");
        puml.Should().Contain("(in)"); // blend-in state label
        puml.Should().Contain("@119 as :119"); // start 109 + 10
    }
}
