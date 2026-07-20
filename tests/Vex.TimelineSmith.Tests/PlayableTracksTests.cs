using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

public class PlayableTracksTests
{
    [Fact]
    public void TmpTimeline_has_two_root_tracks()
    {
        File.Exists(PlayablePaths.TmpTimeline).Should().BeTrue(PlayablePaths.TmpTimeline);
        var r = PlayableInspector.InspectFile(PlayablePaths.TmpTimeline);
        r.RootTrackCount.Should().Be(2);
        r.TimelineName.Should().Be("tmpTimeline");
        r.RootTracks.Select(t => t.Name).Should().BeEquivalentTo(
            "Particle System Track",
            "Visual Effect Track");
        r.RootTracks.Should().OnlyContain(t => t.ClipCount == 1);
    }

    [Fact]
    public void GameplaySequence_has_many_root_tracks()
    {
        if (!File.Exists(PlayablePaths.GameplaySequence))
        {
            return;
        }

        var r = PlayableInspector.InspectFile(PlayablePaths.GameplaySequence);
        r.RootTrackCount.Should().BeGreaterThan(2);
        r.AllTracks.Should().NotBeEmpty();
    }
}
