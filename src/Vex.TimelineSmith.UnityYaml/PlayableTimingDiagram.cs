using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vex.TimelineSmith.UnityYaml;

/// <summary>
/// Unity .playable → PlantUML timing diagram (frames).
/// <list type="bullet">
/// <item>Major axis labels every 60f (1s @ 60fps) + exact clip/blend edges</item>
/// <item>Width is 1px-scale per frame (no pad to next 60f → no empty tail)</item>
/// <item>No Idle bubbles — only real clips; {hidden} closes a clip</item>
/// <item>Duration arrows: bare frame count only</item>
/// <item>No title by default</item>
/// </list>
/// </summary>
public static class PlayableTimingDiagram
{
    public sealed record Options(
        bool Compact = false,
        string LaneStyle = "concise",
        string IdleState = "{hidden}",
        /// <summary>Axis label step + PlantUML tick unit (default = fps, usually 60). Avoid 1 — that draws a vline per frame.</summary>
        int? AxisLabelStep = null,
        /// <summary>Pixels per axis step (scale N as P). null = auto for clip text width.</summary>
        int? PixelsPerStep = null,
        /// <summary>Hide dense PlantUML timegrid dots/lines (default true).</summary>
        bool HideTimeGrid = true,
        bool ShowDurationMarkers = true,
        bool ShowBlendMarkers = true,
        bool ExpandBlendStates = true,
        string? Title = null,
        bool EmitTitle = false,
        string? Theme = null,
        /// <summary>Max chars in clip bubble; longer names are truncated with …</summary>
        int MaxClipLabelChars = 18);

    public static int DefaultAxisLabelStep(double frameRate) =>
        Math.Max(1, (int)Math.Round(frameRate > 0 ? frameRate : TimeUtil.DefaultFrameRate));

    /// <summary>
    /// Pixels per frame for width. ~3+ so a 60f clip fits labels; grid is hidden so scale 1 is safe.
    /// </summary>
    public static int PickPixelsPerFrame(double frameRate, int maxLabelChars)
    {
        var oneSecond = DefaultAxisLabelStep(frameRate);
        var minClipPx = Math.Max(160, maxLabelChars * 9);
        var ppf = (int)Math.Ceiling(minClipPx / (double)Math.Max(1, oneSecond));
        // ≥4px/frame keeps nearby labels (109 vs 120) from stacking on the axis
        return Math.Clamp(ppf, 4, 12);
    }

    // Keep name for tests/callers that still say Step
    public static int PickPixelsPerStep(double frameRate, int maxLabelChars) =>
        PickPixelsPerFrame(frameRate, maxLabelChars) * DefaultAxisLabelStep(frameRate);

    public static string Generate(PlayableRun.Sim sim, Options? options = null)
    {
        options ??= new Options();
        var labelStep = options.AxisLabelStep ?? DefaultAxisLabelStep(sim.FrameRate);
        if (labelStep < 1)
        {
            labelStep = 1;
        }

        // Width must be 1-frame units: scale N as P with N=60 pads the diagram to the next multiple
        // (169 → empty tail to 180). With timegrid hidden, scale 1 is fine (no sand of ticks).
        var ppf = options.PixelsPerStep is { } ps && ps > 0
            ? Math.Max(1, ps / Math.Max(1, labelStep))
            : PickPixelsPerFrame(sim.FrameRate, options.MaxClipLabelChars);

        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        if (!string.IsNullOrWhiteSpace(options.Theme))
        {
            sb.AppendLine($"!theme {options.Theme.Trim()}");
        }

        // Always hide timegrid when using per-frame scale (otherwise 1 vline per frame).
        if (options.HideTimeGrid)
        {
            sb.AppendLine("<style>");
            sb.AppendLine("timingDiagram {");
            sb.AppendLine("  timegrid {");
            sb.AppendLine("    LineColor transparent");
            sb.AppendLine("    LineThickness 0");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("</style>");
        }

        if (options.EmitTitle)
        {
            var title = options.Title ?? sim.TimelineName;
            if (!string.IsNullOrWhiteSpace(title))
            {
                sb.AppendLine(
                    $"title {Escape(title)} ({sim.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)} fps)");
            }
        }

        sb.AppendLine($"' Generated from Unity Timeline: {sim.Path}");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"' fps={sim.FrameRate:0.###}  duration={sim.DurationFrames}f  clips={sim.Clips.Count}  px/frame={ppf}");
        if (options.Compact)
        {
            sb.AppendLine("mode compact");
        }

        // scale 1 as P: diagram width ends ~last frame (+1 unit only). Labels from manual anchors only.
        sb.AppendLine("manual time-axis");
        sb.AppendLine(CultureInfo.InvariantCulture, $"scale 1 as {ppf} pixels");
        sb.AppendLine();

        var trackOrder = new List<string>();
        var trackCodes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in sim.Clips)
        {
            if (trackCodes.ContainsKey(c.TrackName))
            {
                continue;
            }

            var code = MakeCode(c.TrackName, trackCodes.Count);
            trackCodes[c.TrackName] = code;
            trackOrder.Add(c.TrackName);
        }

        var style = options.LaneStyle.Equals("robust", StringComparison.OrdinalIgnoreCase)
            ? "robust"
            : "concise";

        foreach (var track in trackOrder)
        {
            sb.AppendLine($"{style} \"{Escape(track)}\" as {trackCodes[track]}");
        }

        if (trackOrder.Count == 0)
        {
            sb.AppendLine("' (no clips)");
            sb.AppendLine("@enduml");
            return sb.ToString();
        }

        sb.AppendLine();

        // ---- axis labels: 60f grid + clip/blend edges (no trailing pad) ----
        var edgeFrames = new SortedSet<int>();
        foreach (var c in sim.Clips)
        {
            edgeFrames.Add(c.StartFrame);
            edgeFrames.Add(c.EndFrame);
            if (c.BlendInFrames > 0)
            {
                edgeFrames.Add(c.BlendInEndFrame);
            }

            if (c.BlendOutFrames > 0)
            {
                edgeFrames.Add(c.BlendOutStartFrame);
            }
        }

        // Bottom axis labels: always major grid (0,60,120,…) AND clip/blend edges.
        // Do not drop majors near edges — that hid "120" while a highlight still drew a line.
        var labelFrames = new SortedSet<int>();
        if (sim.DurationFrames > 0)
        {
            labelFrames.Add(0);
            labelFrames.Add(sim.DurationFrames);
        }

        foreach (var e in edgeFrames)
        {
            labelFrames.Add(e);
        }

        for (var f = 0; f <= sim.DurationFrames; f += labelStep)
        {
            labelFrames.Add(f);
        }

        foreach (var f in labelFrames)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"@{f} as :{f}");
        }

        sb.AppendLine();

        // ---- state changes: only clip activity (no Idle bubbles) ----
        // Do NOT emit a leading all-hidden @0 (empty start).
        // At clip end: {hidden} closes the capsule without a named Idle.
        var stateFrames = new SortedSet<int>();
        foreach (var c in sim.Clips)
        {
            stateFrames.Add(c.StartFrame);
            stateFrames.Add(c.EndFrame);
            if (options.ExpandBlendStates && c.BlendInFrames > 0)
            {
                stateFrames.Add(c.BlendInEndFrame);
            }

            if (options.ExpandBlendStates && c.BlendOutFrames > 0)
            {
                stateFrames.Add(c.BlendOutStartFrame);
            }
        }

        string Idle()
        {
            if (string.IsNullOrWhiteSpace(options.IdleState))
            {
                return "{hidden}";
            }

            return options.IdleState.StartsWith('{')
                ? options.IdleState
                : QuoteState(options.IdleState);
        }

        foreach (var f in stateFrames)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"@:{f}");
            foreach (var track in trackOrder)
            {
                var code = trackCodes[track];
                var state = ResolveStateAt(sim, track, f, options, Idle);
                sb.AppendLine($"{code} is {state}");
            }

            sb.AppendLine();
        }

        // ---- duration / blend: bare numbers only ----
        if (options.ShowDurationMarkers || options.ShowBlendMarkers)
        {
            sb.AppendLine("' --- duration / blend ---");
            foreach (var c in sim.Clips)
            {
                var code = trackCodes[c.TrackName];
                if (options.ShowDurationMarkers)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"{code}@{c.StartFrame} <-> @{c.EndFrame} : {c.DurationFrames}");
                }

                if (options.ShowBlendMarkers && c.BlendInFrames > 0)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"{code}@{c.StartFrame} <-> @{c.BlendInEndFrame} : {c.BlendInFrames}");
                }

                if (options.ShowBlendMarkers && c.BlendOutFrames > 0)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"{code}@{c.BlendOutStartFrame} <-> @{c.EndFrame} : {c.BlendOutFrames}");
                }
            }

            sb.AppendLine();
        }

        // Major-frame guide lines every 60f (0, 60, 120, …) — always, even near clip edges.
        // Use f → f+0.01 (not f → f+1): a 1-frame band draws two edges (double line).
        sb.AppendLine("' --- major frame guides ---");
        for (var f = 0; f <= sim.DurationFrames; f += labelStep)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"highlight {f} to {f.ToString(CultureInfo.InvariantCulture)}.01 #B0B0B0");
        }

        sb.AppendLine();
        sb.AppendLine("@enduml");
        return sb.ToString();
    }

    /// <summary>
    /// playable → puml. Visual diagram is independent; original file bytes are
    /// embedded losslessly for reverse (<see cref="PlayableEmbed"/>).
    /// If visual inspect fails, still emits a minimal stub diagram + full embed
    /// so reverse recovery never depends on the visual parser.
    /// </summary>
    public static string GenerateFromFile(string playablePath, Options? options = null)
    {
        var bytes = File.ReadAllBytes(playablePath);
        string visual;
        try
        {
            var text = File.ReadAllText(playablePath);
            if (text.Length >= 1 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            var report = PlayableInspector.InspectYaml(text, playablePath);
            var sim = PlayableRun.FromReport(report);
            visual = Generate(sim, options);
        }
        catch (Exception ex)
        {
            // Lossless reverse must still work; visual is best-effort only.
            visual =
                "@startuml\n" +
                $"' Visual inspect failed for {playablePath.Replace("\\", "\\\\", StringComparison.Ordinal)}: {ex.Message.Replace('\n', ' ')}\n" +
                "' (lossless embed still attached below)\n" +
                "@enduml\n";
        }

        // Embed original bytes (not re-encoded text) for 100% byte identity on reverse.
        return PlayableEmbed.AppendEmbed(visual, bytes);
    }

    private static string ResolveStateAt(
        PlayableRun.Sim sim,
        string track,
        int f,
        Options options,
        Func<string> idle)
    {
        var onTrack = sim.Clips
            .Where(c => c.TrackName == track && c.IsActiveAtFrame(f))
            .ToList();

        if (onTrack.Count == 0)
        {
            return idle();
        }

        var c = onTrack[0];
        var label = Shorten(c.ClipName, options.MaxClipLabelChars);

        if (!options.ExpandBlendStates)
        {
            return QuoteState(label);
        }

        if (c.BlendInFrames > 0 && f >= c.StartFrame && f < c.BlendInEndFrame)
        {
            return QuoteState(ShortenWithSuffix(c.ClipName, " (in)", options.MaxClipLabelChars));
        }

        if (c.BlendOutFrames > 0 && f >= c.BlendOutStartFrame && f < c.EndFrame)
        {
            return QuoteState(ShortenWithSuffix(c.ClipName, " (out)", options.MaxClipLabelChars));
        }

        return QuoteState(label);
    }

    private static string Shorten(string name, int maxChars)
    {
        if (maxChars < 4 || name.Length <= maxChars)
        {
            return name;
        }

        return name[..(maxChars - 1)] + "…";
    }

    private static string ShortenWithSuffix(string name, string suffix, int maxChars)
    {
        if (name.Length + suffix.Length <= maxChars)
        {
            return name + suffix;
        }

        var room = maxChars - suffix.Length - 1;
        if (room < 1)
        {
            return suffix.Trim();
        }

        return name[..room] + "…" + suffix;
    }

    private static string MakeCode(string trackName, int index)
    {
        var cleaned = Regex.Replace(trackName, @"[^A-Za-z0-9_]", "");
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0]))
        {
            cleaned = "T" + cleaned;
        }

        if (cleaned.Length > 24)
        {
            cleaned = cleaned[..24];
        }

        return $"{cleaned}_{index}";
    }

    private static string QuoteState(string name)
    {
        if (Regex.IsMatch(name, @"^[A-Za-z0-9_]+$"))
        {
            return name;
        }

        return $"\"{Escape(name)}\"";
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
