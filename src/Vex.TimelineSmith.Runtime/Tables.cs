using Vex.TimelineSmith.Ir;

namespace Vex.TimelineSmith.Runtime;

public readonly record struct Boundary(
    long Tick,
    EdgeKind Kind,
    int TrackIndex,
    int ClipIndex,
    string TrackName,
    string ClipName);

public readonly record struct ClipRow(
    int TrackIndex,
    int ClipIndex,
    string TrackName,
    string ClipName,
    long StartTick,
    long EndTick,
    ClipKind Kind,
    ClipPayload Payload);

public sealed class TimelineTables
{
    public required string Id { get; init; }
    public required uint TickHz { get; init; }
    public required long DurationTicks { get; init; }
    public required WrapPolicy Wrap { get; init; }
    public required bool SampleLastFrame { get; init; }
    public required long InitialTimeTicks { get; init; }
    public required IReadOnlyList<string> TrackNames { get; init; }
    public required IReadOnlyList<ClipRow> Clips { get; init; }

    /// <summary>Global boundaries sorted by (Tick, Kind Exit&lt;Enter, TrackIndex, ClipIndex).</summary>
    public required Boundary[] Boundaries { get; init; }

    /// <summary>Named events: name → when.</summary>
    public required IReadOnlyList<(string Name, EventWhen When)> NamedEvents { get; init; }

    public int TrackCount => TrackNames.Count;

    public static TimelineTables FromDocument(TimelineDocument doc)
    {
        var clips = new List<ClipRow>();
        var boundaries = new List<Boundary>();
        var trackNames = new List<string>();

        foreach (var track in doc.Tracks)
        {
            trackNames.Add(track.Id.Name);
            foreach (var clip in track.Clips)
            {
                clips.Add(new ClipRow(
                    track.Id.Index,
                    clip.Id.Index,
                    track.Id.Name,
                    clip.Id.Name,
                    clip.Range.Start.Ticks,
                    clip.Range.End.Ticks,
                    clip.Kind,
                    clip.Payload));

                boundaries.Add(new Boundary(
                    clip.Range.Start.Ticks,
                    EdgeKind.Enter,
                    track.Id.Index,
                    clip.Id.Index,
                    track.Id.Name,
                    clip.Id.Name));

                boundaries.Add(new Boundary(
                    clip.Range.End.Ticks,
                    EdgeKind.Exit,
                    track.Id.Index,
                    clip.Id.Index,
                    track.Id.Name,
                    clip.Id.Name));
            }
        }

        // Exit before Enter at equal tick; then ordinal track name (matches live edge order); then clip index
        boundaries.Sort(static (a, b) =>
        {
            var c = a.Tick.CompareTo(b.Tick);
            if (c != 0)
            {
                return c;
            }

            c = ((byte)a.Kind).CompareTo((byte)b.Kind);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(a.TrackName, b.TrackName);
            return c != 0 ? c : a.ClipIndex.CompareTo(b.ClipIndex);
        });

        return new TimelineTables
        {
            Id = doc.Id,
            TickHz = doc.TickHz,
            DurationTicks = doc.Director.Duration.Ticks,
            Wrap = doc.Director.Wrap,
            SampleLastFrame = doc.Director.SampleLastFrame,
            InitialTimeTicks = doc.Director.InitialTime.Ticks,
            TrackNames = trackNames,
            Clips = clips,
            Boundaries = boundaries.ToArray(),
            NamedEvents = doc.Events.Select(e => (e.Name, e.When)).ToList(),
        };
    }
}
