namespace Vex.TimelineSmith.Ir;

public enum WrapPolicy : byte
{
    /// <summary>AutoStop: deactivate when range ends.</summary>
    None = 0,

    /// <summary>AutoPause / hold at end while still "active".</summary>
    Hold = 1,

    /// <summary>Loop to start.</summary>
    Loop = 2,
}

public enum OverlapPolicy : byte
{
    Forbid = 0,
}

public enum ClipKind : byte
{
    Phase = 0,
    Hitbox = 1,
    Signal = 2,
}

public enum EdgeKind : byte
{
    /// <summary>Must sort before Enter at equal tick.</summary>
    Exit = 0,
    Enter = 1,
}

public enum DirectorSignal : byte
{
    HoldReached = 0,
    RangeStopped = 1,
    Looped = 2,
}

public readonly record struct TrackId(int Index, string Name);

public readonly record struct ClipId(int Index, string Name);

public sealed record PhasePayload(string Phase);

public sealed record HitboxPayload(int Damage, string Hitbox);

public sealed record SignalPayload(string Fx);

public abstract record ClipPayload
{
    public sealed record Phase(PhasePayload Value) : ClipPayload;

    public sealed record Hitbox(HitboxPayload Value) : ClipPayload;

    public sealed record Signal(SignalPayload Value) : ClipPayload;
}

public sealed record ClipDef(
    ClipId Id,
    HalfOpenRange Range,
    ClipKind Kind,
    ClipPayload Payload);

public sealed record TrackDef(
    TrackId Id,
    string BindingSlot,
    OverlapPolicy Overlap,
    IReadOnlyList<ClipDef> Clips);

public abstract record EventWhen
{
    public sealed record ClipEdge(TrackId Track, ClipId Clip, EdgeKind Edge) : EventWhen;

    public sealed record Director(DirectorSignal Signal) : EventWhen;
}

public sealed record EventDef(string Name, EventWhen When);

public sealed record DirectorDef(
    TickDuration Duration,
    WrapPolicy Wrap,
    bool SampleLastFrame,
    TickInstant InitialTime);

public sealed record TimelineDocument(
    string SchemaId,
    int SchemaVersion,
    string Id,
    uint TickHz,
    DirectorDef Director,
    IReadOnlyList<TrackDef> Tracks,
    IReadOnlyList<EventDef> Events);
