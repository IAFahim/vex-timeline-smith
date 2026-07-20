namespace Vex.TimelineSmith.Ir;

/// <summary>Absolute position on a director timeline (affine point).</summary>
public readonly record struct TickInstant(long Ticks) : IComparable<TickInstant>
{
    public static TickInstant Zero => new(0);

    public static TickInstant FromTicks(long ticks) => new(ticks);

    public int CompareTo(TickInstant other) => Ticks.CompareTo(other.Ticks);

    public static TickInstant operator +(TickInstant t, TickDuration d) => new(t.Ticks + d.Ticks);

    public static TickDuration operator -(TickInstant a, TickInstant b) => new(a.Ticks - b.Ticks);

    public static bool operator <(TickInstant a, TickInstant b) => a.Ticks < b.Ticks;

    public static bool operator >(TickInstant a, TickInstant b) => a.Ticks > b.Ticks;

    public static bool operator <=(TickInstant a, TickInstant b) => a.Ticks <= b.Ticks;

    public static bool operator >=(TickInstant a, TickInstant b) => a.Ticks >= b.Ticks;

    public override string ToString() => Ticks.ToString();
}

/// <summary>Duration / step length (affine vector).</summary>
public readonly record struct TickDuration(long Ticks) : IComparable<TickDuration>
{
    public static TickDuration Zero => new(0);

    public static TickDuration FromTicks(long ticks) => new(ticks);

    public int CompareTo(TickDuration other) => Ticks.CompareTo(other.Ticks);

    public static TickDuration operator +(TickDuration a, TickDuration b) => new(a.Ticks + b.Ticks);

    public static TickDuration operator *(TickDuration d, int n) => new(d.Ticks * n);

    public override string ToString() => Ticks.ToString();
}

/// <summary>Half-open activity window: Start &lt;= t &lt; End.</summary>
public readonly record struct HalfOpenRange(TickInstant Start, TickInstant End)
{
    public bool Contains(TickInstant t) => t.Ticks >= Start.Ticks && t.Ticks < End.Ticks;

    public long LengthTicks => End.Ticks - Start.Ticks;

    public static HalfOpenRange Create(long startInclusive, long endExclusive)
    {
        if (endExclusive <= startInclusive)
        {
            throw new ArgumentException($"Invalid range [{startInclusive}, {endExclusive}).");
        }

        return new HalfOpenRange(TickInstant.FromTicks(startInclusive), TickInstant.FromTicks(endExclusive));
    }
}
