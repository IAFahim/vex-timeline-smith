namespace Vex.TimelineSmith.Ir;

// Wire DTOs for YAML/JSON — cold path only.

public sealed class TimelineWireDto
{
    public string? SchemaId { get; set; }
    public int SchemaVersion { get; set; }
    public string? Id { get; set; }
    public TimeWireDto? Time { get; set; }
    public DirectorWireDto? Director { get; set; }
    public List<TrackWireDto>? Tracks { get; set; }
    public List<EventWireDto>? Events { get; set; }
}

public sealed class TimeWireDto
{
    public string? Unit { get; set; }
    public uint TickHz { get; set; }
}

public sealed class DirectorWireDto
{
    public long DurationTicks { get; set; }
    public string? Wrap { get; set; }
    public bool SampleLastFrame { get; set; } = true;
    public long InitialTimeTicks { get; set; }
}

public sealed class TrackWireDto
{
    public string? Id { get; set; }
    public string? BindingSlot { get; set; }
    public string? Overlap { get; set; }
    public List<ClipWireDto>? Clips { get; set; }
}

public sealed class ClipWireDto
{
    public string? Id { get; set; }
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public string? Kind { get; set; }
    public Dictionary<string, object?>? Payload { get; set; }
}

public sealed class EventWireDto
{
    public string? Name { get; set; }
    public EventWhenWireDto? When { get; set; }
}

public sealed class EventWhenWireDto
{
    public string? Track { get; set; }
    public string? Clip { get; set; }
    public string? Edge { get; set; }
    public string? Director { get; set; }
}
