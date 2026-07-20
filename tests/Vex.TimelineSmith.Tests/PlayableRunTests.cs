using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

public class PlayableRunTests
{
    [Fact]
    public void Clips_are_in_frames_at_60fps()
    {
        var sim = PlayableRun.FromFile(PlayablePaths.TmpTimeline);
        sim.FrameRate.Should().Be(60);
        sim.Clips.Should().HaveCount(2);
        // ~1.817s * 60 ≈ 109f, duration 1s = 60f → end 169
        sim.Clips.Should().OnlyContain(c => c.StartFrame == 109 && c.EndFrame == 169);
        sim.DurationFrames.Should().Be(169);
    }

    [Fact]
    public void At_frame_15_nothing_running()
    {
        var sim = PlayableRun.FromFile(PlayablePaths.TmpTimeline);
        // 0.25s * 60 = 15f
        sim.ActiveAtFrame(15).Should().BeEmpty();
        sim.ActiveAtFrame(0).Should().BeEmpty();
    }

    [Fact]
    public void At_frame_120_both_running()
    {
        var sim = PlayableRun.FromFile(PlayablePaths.TmpTimeline);
        var active = sim.ActiveAtFrame(120);
        active.Should().HaveCount(2);
    }

    [Fact]
    public void At_end_frame_half_open_not_running()
    {
        var sim = PlayableRun.FromFile(PlayablePaths.TmpTimeline);
        var end = sim.DurationFrames;
        sim.ActiveAtFrame(end).Should().BeEmpty();
        sim.ActiveAtFrame(end - 1).Should().NotBeEmpty();
    }
}
