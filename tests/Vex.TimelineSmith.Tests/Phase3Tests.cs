using FluentAssertions;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;

namespace Vex.TimelineSmith.Tests;

public class Phase3Tests
{
    [Fact]
    public void Lossy_seconds_to_ticks_floor()
    {
        LossyTimingImport.SecondsToTicks(0.0, 60).Should().Be(0);
        LossyTimingImport.SecondsToTicks(1.0, 60).Should().Be(60);
        LossyTimingImport.SecondsToTicks(0.1166, 60).Should().Be(6); // ~7 frames @60 → floor
        LossyTimingImport.SecondsToTicks(0.8, 60).Should().Be(48);
    }

    [Fact]
    public void Lossy_fixture_projects_windows_and_stub_bindings()
    {
        // Combat timing skeleton (seconds) → IR ticks at 60 Hz
        var skel = new LossyTimingImport.TimingSkeleton(
            Id: "attack_light_lossy",
            DurationSeconds: 0.8,
            TickHz: 60,
            Wrap: "hold",
            Tracks: new[]
            {
                new LossyTimingImport.TimingTrack(
                    "body",
                    "phase",
                    new[]
                    {
                        new LossyTimingImport.TimingClip("startup", 0.0, 7.0 / 60.0),
                        new LossyTimingImport.TimingClip("active", 7.0 / 60.0, 17.0 / 60.0, "hitbox"),
                        new LossyTimingImport.TimingClip("recovery", 17.0 / 60.0, 0.8),
                    },
                    BindingPath: "Player/Body"),
                new LossyTimingImport.TimingTrack(
                    "vfx",
                    "signal",
                    new[]
                    {
                        new LossyTimingImport.TimingClip("swing_fx", 6.0 / 60.0, 21.0 / 60.0, "signal"),
                    },
                    BindingPath: null),
            });

        var r = LossyTimingImport.Project(skel);
        r.IsSuccess.Should().BeTrue(string.Join("; ", r.Errors));
        var doc = r.Value!;
        doc.Director.Duration.Ticks.Should().Be(48);
        doc.Tracks.Should().HaveCount(2);
        doc.Tracks[0].BindingSlot.Should().Be("stub:Player/Body");
        doc.Tracks[1].BindingSlot.Should().Be("stub");

        var body = doc.Tracks[0].Clips;
        body[0].Range.Start.Ticks.Should().Be(0);
        body[0].Range.End.Ticks.Should().Be(7);
        body[1].Range.Start.Ticks.Should().Be(7);
        body[1].Range.End.Ticks.Should().Be(17);
        body[2].Range.Start.Ticks.Should().Be(17);
        body[2].Range.End.Ticks.Should().Be(48);

        var tables = TimelineTables.FromDocument(doc);
        var log = new TimelineAgent(tables).RunScript(new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.RunToEnd(),
        });
        log.Frames.Last().Mode.Should().Be(DirectorMode.HoldComplete);
        log.Frames.Should().Contain(f => f.Edges.Any(e => e.Clip == "active" && e.Kind == EdgeKind.Enter));
    }

    [Fact]
    public void Lossy_rejects_audio_track_by_default()
    {
        var skel = new LossyTimingImport.TimingSkeleton(
            Id: "bad",
            DurationSeconds: 1.0,
            Tracks: new[]
            {
                new LossyTimingImport.TimingTrack(
                    "sfx",
                    "audio",
                    new[] { new LossyTimingImport.TimingClip("whoosh", 0, 0.5) }),
            });

        var r = LossyTimingImport.Project(skel);
        r.IsSuccess.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Code == "track_kind");
    }

    [Fact]
    public void Unity_editor_export_unavailable_offline_fixture_is_the_path()
    {
        // Honest fallback: no Unity Editor in this environment; fixture projection is the gate.
        var unityHint = Environment.GetEnvironmentVariable("UNITY_PATH");
        // Either no UNITY_PATH or we still rely on offline fixture (this test always runs offline path).
        var offlineOk = LossyTimingImport.Project(new LossyTimingImport.TimingSkeleton(
            "offline",
            0.1,
            new[]
            {
                new LossyTimingImport.TimingTrack(
                    "body",
                    "dots",
                    new[] { new LossyTimingImport.TimingClip("a", 0, 0.05) }),
            })).IsSuccess;
        offlineOk.Should().BeTrue();
        // Record env for scratch log consumers
        _ = unityHint;
    }
}
