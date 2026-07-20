namespace Vex.TimelineSmith.Ir;

/// <summary>
/// Phase 3: lossy timing-only projection from a Unity Timeline skeleton fixture into Timeline IR.
/// Bindings become map stubs; non-DOTS / unsupported track kinds are rejected by default.
/// </summary>
public static class LossyTimingImport
{
    public sealed record TimingClip(string Id, double StartSeconds, double EndSeconds, string Kind = "phase");

    public sealed record TimingTrack(string Id, string Kind, IReadOnlyList<TimingClip> Clips, string? BindingPath = null);

    public sealed record TimingSkeleton(
        string Id,
        double DurationSeconds,
        IReadOnlyList<TimingTrack> Tracks,
        uint TickHz = 60,
        string Wrap = "hold");

    /// <summary>
    /// Allowed track kinds for offline import (DOTS-ish shells). Others rejected.
    /// </summary>
    public static readonly HashSet<string> AllowedTrackKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "phase",
        "activation",
        "signal",
        "animation", // timing shell only
        "dots",
        "body",
        "vfx",
    };

    public static readonly HashSet<string> RejectedTrackKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio",
        "control",
        "playable",
        "group",
        "marker",
        "cinemachine",
    };

    public static IrResult<TimelineDocument> Project(TimingSkeleton skel)
    {
        var errors = new List<IrError>();
        if (string.IsNullOrWhiteSpace(skel.Id))
        {
            errors.Add(new IrError("id", "skeleton", "id required."));
        }

        if (skel.TickHz == 0)
        {
            errors.Add(new IrError("time", "skeleton.tick_hz", "must be > 0."));
        }

        if (skel.DurationSeconds <= 0)
        {
            errors.Add(new IrError("director", "skeleton.duration_seconds", "must be > 0."));
        }

        var durationTicks = SecondsToTicks(skel.DurationSeconds, skel.TickHz);
        if (durationTicks <= 0)
        {
            errors.Add(new IrError("director", "duration", "lossy duration_ticks must be > 0."));
        }

        var tracksWire = new List<TrackWireDto>();
        for (var ti = 0; ti < skel.Tracks.Count; ti++)
        {
            var tr = skel.Tracks[ti];
            var path = $"tracks[{ti}]";
            if (RejectedTrackKinds.Contains(tr.Kind) || !AllowedTrackKinds.Contains(tr.Kind))
            {
                errors.Add(new IrError(
                    "track_kind",
                    path,
                    $"Track kind '{tr.Kind}' rejected by lossy importer (non-DOTS / unsupported)."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(tr.Id))
            {
                errors.Add(new IrError("track", path, "id required."));
                continue;
            }

            var clips = new List<ClipWireDto>();
            for (var ci = 0; ci < tr.Clips.Count; ci++)
            {
                var c = tr.Clips[ci];
                var start = SecondsToTicks(c.StartSeconds, skel.TickHz);
                var end = SecondsToTicks(c.EndSeconds, skel.TickHz);
                if (end <= start)
                {
                    // lossy: snap zero-length to 1 tick if start valid
                    end = start + 1;
                }

                if (end > durationTicks)
                {
                    end = durationTicks;
                }

                var kind = c.Kind switch
                {
                    "hitbox" => "hitbox",
                    "signal" => "signal",
                    _ => "phase",
                };

                var payload = kind switch
                {
                    "hitbox" => new Dictionary<string, object?> { ["damage"] = 0, ["hitbox"] = c.Id },
                    "signal" => new Dictionary<string, object?> { ["fx"] = c.Id },
                    _ => new Dictionary<string, object?> { ["phase"] = c.Id },
                };

                clips.Add(new ClipWireDto
                {
                    Id = string.IsNullOrWhiteSpace(c.Id) ? $"clip_{ci}" : c.Id,
                    StartTick = start,
                    EndTick = end,
                    Kind = kind,
                    Payload = payload,
                });
            }

            tracksWire.Add(new TrackWireDto
            {
                Id = tr.Id,
                // bindings left as map stubs
                BindingSlot = string.IsNullOrWhiteSpace(tr.BindingPath) ? "stub" : $"stub:{tr.BindingPath}",
                Overlap = "forbid",
                Clips = clips,
            });
        }

        if (errors.Count > 0)
        {
            return IrResult<TimelineDocument>.Fail(errors);
        }

        var dto = new TimelineWireDto
        {
            SchemaId = "vex.timeline-ir",
            SchemaVersion = 1,
            Id = skel.Id,
            Time = new TimeWireDto { Unit = "ticks", TickHz = skel.TickHz },
            Director = new DirectorWireDto
            {
                DurationTicks = durationTicks,
                Wrap = skel.Wrap,
                SampleLastFrame = true,
                InitialTimeTicks = 0,
            },
            Tracks = tracksWire,
            Events = new List<EventWireDto>(),
        };

        return TimelineLoader.Parse(dto, "lossy-skeleton");
    }

    /// <summary>Floor seconds → ticks (lossy). Matches offline fixture contract.</summary>
    public static long SecondsToTicks(double seconds, uint tickHz)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        return (long)Math.Floor(seconds * tickHz + 1e-9);
    }
}
