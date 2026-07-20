using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

public class PlayableTracksTests
{
    private static string TmpTimeline =>
        "/home/i/GitHub/com.vex.unity.vex-ee/Assets/tmpTimeline.playable";

    private static string GameplaySequence =>
        "/home/i/GitHub/com.vex.unity.vex-ee/Library/PackageCache/com.unity.timeline@a750b6f8e125/Samples~/GameplaySequenceDemo/Timelines/GameplaySequence.playable";

    [Fact]
    public void TmpTimeline_has_two_root_tracks()
    {
        File.Exists(TmpTimeline).Should().BeTrue();
        var r = PlayableInspector.InspectFile(TmpTimeline);
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
        if (!File.Exists(GameplaySequence))
        {
            return; // package cache may be absent
        }

        var r = PlayableInspector.InspectFile(GameplaySequence);
        r.RootTrackCount.Should().BeGreaterThan(2);
        r.AllTracks.Should().NotBeEmpty();
    }
}
