namespace Vex.TimelineSmith.Tests;

/// <summary>Fixture paths portable on CI (no absolute machine paths).</summary>
internal static class PlayablePaths
{
    public static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Playables"));

    public static string Fixture(string fileName) =>
        Path.Combine(FixtureDir, fileName);

    /// <summary>tmpTimeline: 2 tracks, clips 109→169 @ 60fps.</summary>
    public static string TmpTimeline => Fixture("05_tmpTimeline.playable");

    public static string CanSubTimeline => Fixture("01_CanSubTimeline.playable");

    public static string GameplaySequence => Fixture("02_GameplaySequence.playable");
}
