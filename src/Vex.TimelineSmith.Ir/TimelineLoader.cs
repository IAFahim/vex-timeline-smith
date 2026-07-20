using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vex.TimelineSmith.Ir;

public static class TimelineLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IrResult<TimelineDocument> LoadFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return IrResult<TimelineDocument>.Fail(new IrError("io", path, ex.Message));
        }

        return LoadYaml(text, path);
    }

    public static IrResult<TimelineDocument> LoadYaml(string yaml, string pathLabel = "<yaml>")
    {
        TimelineWireDto dto;
        try
        {
            dto = Yaml.Deserialize<TimelineWireDto>(yaml)
                  ?? throw new InvalidOperationException("Empty document.");
        }
        catch (Exception ex)
        {
            return IrResult<TimelineDocument>.Fail(new IrError("parse", pathLabel, ex.Message));
        }

        return Parse(dto, pathLabel);
    }

    public static IrResult<TimelineDocument> Parse(TimelineWireDto dto, string pathLabel = "<doc>")
    {
        var errors = new List<IrError>();

        if (dto.SchemaId is not ("vex.timeline-ir" or "vex.timeline-ir/1"))
        {
            errors.Add(new IrError("schema", pathLabel, $"Unsupported schema_id '{dto.SchemaId}'."));
        }

        if (dto.SchemaVersion is not (0 or 1))
        {
            errors.Add(new IrError("schema", pathLabel, $"Unsupported schema_version {dto.SchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            errors.Add(new IrError("id", pathLabel, "Document id is required."));
        }

        var tickHz = dto.Time?.TickHz ?? 0;
        if (tickHz == 0)
        {
            errors.Add(new IrError("time", pathLabel, "time.tick_hz must be > 0."));
        }

        if (dto.Time?.Unit is not null and not "ticks")
        {
            errors.Add(new IrError("time", pathLabel, "time.unit must be 'ticks' in v1."));
        }

        if (dto.Director is null)
        {
            errors.Add(new IrError("director", pathLabel, "director is required."));
            return IrResult<TimelineDocument>.Fail(errors);
        }

        var wrap = ParseWrap(dto.Director.Wrap, errors, "director.wrap");
        if (dto.Director.DurationTicks <= 0)
        {
            errors.Add(new IrError("director", "director.duration_ticks", "Must be > 0."));
        }

        // Half-open director clock: 0 <= initial_time_ticks < duration_ticks
        if (dto.Director.DurationTicks > 0 &&
            (dto.Director.InitialTimeTicks < 0 || dto.Director.InitialTimeTicks >= dto.Director.DurationTicks))
        {
            errors.Add(new IrError("director", "director.initial_time_ticks", "Must satisfy 0 <= initial < duration_ticks."));
        }

        var tracksWire = dto.Tracks ?? new List<TrackWireDto>();
        var trackNames = new HashSet<string>(StringComparer.Ordinal);
        var tracks = new List<TrackDef>();

        for (var ti = 0; ti < tracksWire.Count; ti++)
        {
            var tw = tracksWire[ti];
            var tPath = $"tracks[{ti}]";
            if (string.IsNullOrWhiteSpace(tw.Id))
            {
                errors.Add(new IrError("track", tPath, "id required."));
                continue;
            }

            if (!trackNames.Add(tw.Id))
            {
                errors.Add(new IrError("track", tPath, $"Duplicate track id '{tw.Id}'."));
            }

            var overlap = OverlapPolicy.Forbid;
            if (tw.Overlap is not null and not "forbid")
            {
                errors.Add(new IrError("track", $"{tPath}.overlap", "v1 only supports overlap: forbid."));
            }

            var clipNames = new HashSet<string>(StringComparer.Ordinal);
            var clips = new List<ClipDef>();
            var cw = tw.Clips ?? new List<ClipWireDto>();
            for (var ci = 0; ci < cw.Count; ci++)
            {
                var c = cw[ci];
                var cPath = $"{tPath}.clips[{ci}]";
                if (string.IsNullOrWhiteSpace(c.Id))
                {
                    errors.Add(new IrError("clip", cPath, "id required."));
                    continue;
                }

                if (!clipNames.Add(c.Id))
                {
                    errors.Add(new IrError("clip", cPath, $"Duplicate clip id '{c.Id}'."));
                }

                if (c.EndTick <= c.StartTick)
                {
                    errors.Add(new IrError("clip", cPath, "end_tick must be > start_tick."));
                    continue;
                }

                if (c.StartTick < 0 || c.EndTick > dto.Director.DurationTicks)
                {
                    errors.Add(new IrError("clip", cPath, "Clip range must be within [0, duration_ticks)."));
                }

                var kind = ParseKind(c.Kind, errors, $"{cPath}.kind");
                var payload = ParsePayload(kind, c.Payload, errors, $"{cPath}.payload");
                if (payload is null)
                {
                    continue;
                }

                clips.Add(new ClipDef(
                    new ClipId(clips.Count, c.Id),
                    HalfOpenRange.Create(c.StartTick, c.EndTick),
                    kind,
                    payload));
            }

            // Overlap check (forbid)
            var ordered = clips.OrderBy(x => x.Range.Start.Ticks).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Range.Start.Ticks < ordered[i - 1].Range.End.Ticks)
                {
                    errors.Add(new IrError(
                        "overlap",
                        tPath,
                        $"Clips '{ordered[i - 1].Id.Name}' and '{ordered[i].Id.Name}' overlap."));
                }
            }

            // Re-index after sort for stable indices matching IR file order for edge ordering by track index
            var clipsInFileOrder = clips;
            tracks.Add(new TrackDef(
                new TrackId(tracks.Count, tw.Id),
                string.IsNullOrWhiteSpace(tw.BindingSlot) ? "self" : tw.BindingSlot!,
                overlap,
                clipsInFileOrder));
        }

        var events = new List<EventDef>();
        var ew = dto.Events ?? new List<EventWireDto>();
        for (var ei = 0; ei < ew.Count; ei++)
        {
            var e = ew[ei];
            var ePath = $"events[{ei}]";
            if (string.IsNullOrWhiteSpace(e.Name) || !IsEventName(e.Name))
            {
                errors.Add(new IrError("event", ePath, "Valid event name required ([A-Za-z_][A-Za-z0-9_]*)."));
                continue;
            }

            if (e.When is null)
            {
                errors.Add(new IrError("event", ePath, "when is required."));
                continue;
            }

            EventWhen? when = null;
            if (!string.IsNullOrWhiteSpace(e.When.Director))
            {
                when = e.When.Director switch
                {
                    "hold_reached" => new EventWhen.Director(DirectorSignal.HoldReached),
                    "range_stopped" => new EventWhen.Director(DirectorSignal.RangeStopped),
                    "looped" => new EventWhen.Director(DirectorSignal.Looped),
                    _ => null,
                };
                if (when is null)
                {
                    errors.Add(new IrError("event", $"{ePath}.when.director", $"Unknown director signal '{e.When.Director}'."));
                }
            }
            else if (!string.IsNullOrWhiteSpace(e.When.Track) && !string.IsNullOrWhiteSpace(e.When.Clip))
            {
                var track = tracks.FirstOrDefault(t => t.Id.Name == e.When.Track);
                if (track is null)
                {
                    errors.Add(new IrError("event", ePath, $"Unknown track '{e.When.Track}'."));
                }
                else
                {
                    var clip = track.Clips.FirstOrDefault(c => c.Id.Name == e.When.Clip);
                    if (clip is null)
                    {
                        errors.Add(new IrError("event", ePath, $"Unknown clip '{e.When.Clip}' on track '{e.When.Track}'."));
                    }
                    else
                    {
                        var edge = e.When.Edge switch
                        {
                            "enter" => EdgeKind.Enter,
                            "exit" => EdgeKind.Exit,
                            _ => (EdgeKind?)null,
                        };
                        if (edge is null)
                        {
                            errors.Add(new IrError("event", $"{ePath}.when.edge", "edge must be enter|exit."));
                        }
                        else
                        {
                            when = new EventWhen.ClipEdge(track.Id, clip.Id, edge.Value);
                        }
                    }
                }
            }
            else
            {
                errors.Add(new IrError("event", ePath, "when must be clip edge or director signal."));
            }

            if (when is not null)
            {
                events.Add(new EventDef(e.Name, when));
            }
        }

        if (errors.Count > 0)
        {
            return IrResult<TimelineDocument>.Fail(errors);
        }

        var director = new DirectorDef(
            TickDuration.FromTicks(dto.Director.DurationTicks),
            wrap,
            dto.Director.SampleLastFrame,
            TickInstant.FromTicks(dto.Director.InitialTimeTicks));

        var doc = new TimelineDocument(
            dto.SchemaId ?? "vex.timeline-ir",
            dto.SchemaVersion == 0 ? 1 : dto.SchemaVersion,
            dto.Id!,
            tickHz,
            director,
            tracks,
            events);

        return IrResult<TimelineDocument>.Ok(doc);
    }

    private static bool IsEventName(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] == '_') &&
        name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static WrapPolicy ParseWrap(string? wrap, List<IrError> errors, string path)
    {
        return wrap switch
        {
            null or "hold" => WrapPolicy.Hold,
            "none" => WrapPolicy.None,
            "loop" => WrapPolicy.Loop,
            _ => AddAndDefault(errors, path, $"Unknown wrap '{wrap}'.", WrapPolicy.Hold),
        };
    }

    private static ClipKind ParseKind(string? kind, List<IrError> errors, string path)
    {
        return kind switch
        {
            "phase" => ClipKind.Phase,
            "hitbox" => ClipKind.Hitbox,
            "signal" => ClipKind.Signal,
            _ => AddAndDefault(errors, path, $"Unknown kind '{kind}'.", ClipKind.Phase),
        };
    }

    private static ClipPayload? ParsePayload(
        ClipKind kind,
        Dictionary<string, object?>? payload,
        List<IrError> errors,
        string path)
    {
        payload ??= new Dictionary<string, object?>();
        try
        {
            return kind switch
            {
                ClipKind.Phase => new ClipPayload.Phase(new PhasePayload(ReqString(payload, "phase", path))),
                ClipKind.Hitbox => new ClipPayload.Hitbox(new HitboxPayload(
                    ReqInt(payload, "damage", path),
                    ReqString(payload, "hitbox", path))),
                ClipKind.Signal => new ClipPayload.Signal(new SignalPayload(ReqString(payload, "fx", path))),
                _ => null,
            };
        }
        catch (IrPayloadException ex)
        {
            errors.Add(new IrError("payload", path, ex.Message));
            return null;
        }
    }

    private static string ReqString(Dictionary<string, object?> p, string key, string path)
    {
        if (!p.TryGetValue(key, out var v) || v is null)
        {
            throw new IrPayloadException($"Missing string field '{key}'.");
        }

        return v.ToString() ?? throw new IrPayloadException($"Missing string field '{key}'.");
    }

    private static int ReqInt(Dictionary<string, object?> p, string key, string path)
    {
        if (!p.TryGetValue(key, out var v) || v is null)
        {
            throw new IrPayloadException($"Missing int field '{key}'.");
        }

        return v switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            long => throw new IrPayloadException($"Field '{key}' out of int range."),
            string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
            _ => throw new IrPayloadException($"Field '{key}' must be int."),
        };
    }

    private static T AddAndDefault<T>(List<IrError> errors, string path, string msg, T fallback)
    {
        errors.Add(new IrError("value", path, msg));
        return fallback;
    }

    private sealed class IrPayloadException(string message) : Exception(message);
}
