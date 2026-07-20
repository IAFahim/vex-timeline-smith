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
        int MaxClipLabelChars = 12);

        /// <summary>
        /// How many label chars fit in a clip capsule (px/frame).
        /// Always ≥1 for a real clip — never hide a clip as PlantUML <c>{-}</c> (flat/unlabeled).
        /// Short clips also get the name on the duration arrow above the bar.
        /// </summary>
        public static int LabelCharsForClip(int durationFrames, int pixelsPerFrame, int maxChars)
        {
            if (maxChars < 1)
            {
                return 0;
            }

            // ~7px per character; margin for diamond ends.
            var fit = Math.Max(1, (durationFrames * pixelsPerFrame - 16) / 7);
            return Math.Min(maxChars, fit);
        }

        /// <summary>True when the capsule is too narrow for a readable name — put name on the constraint arrow.</summary>
        public static bool PreferNameOnArrow(int durationFrames, int pixelsPerFrame, int maxChars) =>
            durationFrames < 30
            || LabelCharsForClip(durationFrames, pixelsPerFrame, maxChars) < Math.Min(maxChars, 8);

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

        // ---- axis labels: major grid + duration end only ----
        // Clip/blend edges used to be labeled too; on dense package timelines (CanSubTimeline)
        // that flooded the bottom with overlapping numbers. Edges still show as state diamonds.
        var labelFrames = new SortedSet<int>();
        if (sim.DurationFrames > 0)
        {
            labelFrames.Add(0);
            labelFrames.Add(sim.DurationFrames);
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
        //
        // CRITICAL: only re-emit a track when ITS state actually changes.
        // Emitting every track at every global edge forced PlantUML to split long
        // continuous clips (e.g. Animation) into many labeled capsules — unreadable.
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

        var idleState = Idle();
        var prevState = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in stateFrames)
        {
            var changes = new List<(string Code, string State)>();
            foreach (var track in trackOrder)
            {
                var code = trackCodes[track];
                var state = ResolveStateAt(sim, track, f, options, Idle, ppf);
                if (!prevState.TryGetValue(track, out var prev))
                {
                    // First time: skip pure idle (no leading all-hidden rows).
                    if (state == idleState)
                    {
                        continue;
                    }

                    changes.Add((code, state));
                    prevState[track] = state;
                    continue;
                }

                if (prev == state)
                {
                    continue;
                }

                changes.Add((code, state));
                prevState[track] = state;
            }

            if (changes.Count == 0)
            {
                continue;
            }

            // Bare @f (not @:f): only majors are labeled on the axis. PlantUML still
            // accepts intermediate @edge times without an "as :N" bottom label.
            sb.AppendLine(CultureInfo.InvariantCulture, $"@{f}");
            foreach (var (code, state) in changes)
            {
                sb.AppendLine($"{code} is {state}");
            }

            sb.AppendLine();
        }

        // ---- duration / blend arrows ----
        // PlantUML constraint text sits ABOVE the bar — use it for short-clip names
        // (capsule text won't fit; {-} flat is wrong for a real clip).
        // Long clips: bare frame count; short clips: clip name.
        const int minBlendArrowFrames = 8;
        if (options.ShowDurationMarkers || options.ShowBlendMarkers)
        {
            sb.AppendLine("' --- duration / blend ---");
            foreach (var c in sim.Clips)
            {
                var code = trackCodes[c.TrackName];
                if (options.ShowDurationMarkers && c.DurationFrames > 0)
                {
                    var arrowLabel = PreferNameOnArrow(c.DurationFrames, ppf, options.MaxClipLabelChars)
                        ? Escape(Shorten(c.ClipName, options.MaxClipLabelChars))
                        : c.DurationFrames.ToString(CultureInfo.InvariantCulture);
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"{code}@{c.StartFrame} <-> @{c.EndFrame} : {arrowLabel}");
                }

                if (options.ShowBlendMarkers && c.BlendInFrames >= minBlendArrowFrames)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"{code}@{c.StartFrame} <-> @{c.BlendInEndFrame} : {c.BlendInFrames}");
                }

                if (options.ShowBlendMarkers && c.BlendOutFrames >= minBlendArrowFrames)
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
        Func<string> idle,
        int pixelsPerFrame)
    {
        var onTrack = sim.Clips
            .Where(c => c.TrackName == track && c.IsActiveAtFrame(f))
            .ToList();

        if (onTrack.Count == 0)
        {
            return idle();
        }

        var c = onTrack[0];

        // Size text by the *visible segment* (full clip, or just the blend-in/out slice).
        // Using full-clip width put "can (out)" into an 18f band → collision.
        var segmentFrames = c.DurationFrames;
        string? blendSuffix = null;
        if (options.ExpandBlendStates)
        {
            if (c.BlendInFrames > 0 && f >= c.StartFrame && f < c.BlendInEndFrame)
            {
                segmentFrames = c.BlendInFrames;
                blendSuffix = " (in)";
            }
            else if (c.BlendOutFrames > 0 && f >= c.BlendOutStartFrame && f < c.EndFrame)
            {
                segmentFrames = c.BlendOutFrames;
                blendSuffix = " (out)";
            }
        }

        // Always show the clip name in the capsule. PlantUML {-} is "flat" (unlabeled line)
        // — not "short clip". If the bar is narrow, name also goes on the duration arrow.
        var maxChars = Math.Max(1, LabelCharsForClip(segmentFrames, pixelsPerFrame, options.MaxClipLabelChars));

        if (blendSuffix is not null)
        {
            return QuoteState(ShortenWithSuffix(c.ClipName, blendSuffix, maxChars));
        }

        return QuoteState(Shorten(c.ClipName, maxChars));
    }

    private static string Shorten(string name, int maxChars)
    {
        if (maxChars < 1 || name.Length <= maxChars)
        {
            return name;
        }

        if (maxChars == 1)
        {
            return name[..1];
        }

        return name[..(maxChars - 1)] + "…";
    }

    private static string ShortenWithSuffix(string name, string suffix, int maxChars)
    {
        if (name.Length + suffix.Length <= maxChars)
        {
            return name + suffix;
        }

        // Prefer keeping the suffix when room is tight (e.g. "… (out)").
        var room = maxChars - suffix.Length - 1;
        if (room < 1)
        {
            return Shorten(name, maxChars);
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
