using System.Text;
using Vex.TimelineSmith.Ir;

namespace Vex.TimelineSmith.Runtime;

public enum DirectorMode : byte
{
    Stopped = 0,
    Playing = 1,
    Paused = 2,
    HoldComplete = 3, // wrap=hold, at end, still considered finished transport
}

public readonly record struct TraceEdge(
    long Tick,
    string Track,
    string Clip,
    EdgeKind Kind);

public readonly record struct TraceNamedEvent(long Tick, string Name);

public sealed class TraceFrame
{
    public required long Tick { get; init; }
    public required DirectorMode Mode { get; init; }
    /// <summary>Per track: active clip name or null.</summary>
    public required string?[] ActiveByTrack { get; init; }
    public required List<TraceEdge> Edges { get; init; }
    public required List<TraceNamedEvent> NamedEvents { get; init; }
}

public sealed class TraceLog
{
    public List<TraceFrame> Frames { get; } = new();

    public string CanonicalString()
    {
        var sb = new StringBuilder();
        foreach (var f in Frames)
        {
            sb.Append(f.Tick).Append('|').Append(f.Mode).Append('|');
            for (var i = 0; i < f.ActiveByTrack.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(f.ActiveByTrack[i] ?? "-");
            }

            sb.Append('|');
            foreach (var e in f.Edges)
            {
                sb.Append(e.Kind == EdgeKind.Enter ? 'E' : 'X')
                    .Append(':').Append(e.Track).Append('/').Append(e.Clip).Append(';');
            }

            sb.Append('|');
            foreach (var n in f.NamedEvents)
            {
                sb.Append(n.Name).Append(';');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    public override bool Equals(object? obj) =>
        obj is TraceLog other && CanonicalString() == other.CanonicalString();

    public override int GetHashCode() => CanonicalString().GetHashCode(StringComparison.Ordinal);
}
