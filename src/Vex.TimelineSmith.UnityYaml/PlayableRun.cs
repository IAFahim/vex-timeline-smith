using System.Globalization;
using System.Text;

namespace Vex.TimelineSmith.UnityYaml;

/// <summary>
/// Offline "what is playing at frame f?" for a Unity Timeline .playable.
/// Unity stores seconds; we convert with timeline frame rate (default 60).
/// Clip active when startFrame &lt;= f &lt; endFrame (half-open frames).
/// </summary>
public static class PlayableRun
{
    public sealed record FlatClip(
        string TrackName,
        string TrackType,
        string ClipName,
        int StartFrame,
        int EndFrame,
        double StartSec,
        double EndSec,
        int BlendInFrames = 0,
        int BlendOutFrames = 0,
        int EaseInFrames = 0,
        int EaseOutFrames = 0)
    {
        public int DurationFrames => EndFrame - StartFrame;

        public bool IsActiveAtFrame(int f) => f >= StartFrame && f < EndFrame;

        /// <summary>Blend-in region: [Start, Start+BlendIn).</summary>
        public int BlendInEndFrame => Math.Min(EndFrame, StartFrame + Math.Max(0, BlendInFrames));

        /// <summary>Blend-out region: [End-BlendOut, End).</summary>
        public int BlendOutStartFrame => Math.Max(StartFrame, EndFrame - Math.Max(0, BlendOutFrames));
    }

    public sealed record Sim(
        string Path,
        string TimelineName,
        double FrameRate,
        IReadOnlyList<FlatClip> Clips,
        int DurationFrames)
    {
        public IReadOnlyList<FlatClip> ActiveAtFrame(int f) =>
            Clips.Where(c => c.IsActiveAtFrame(f)).ToList();
    }

    public static Sim FromReport(PlayableInspector.Report report)
    {
        var fps = report.FrameRate > 0 ? report.FrameRate : TimeUtil.DefaultFrameRate;
        var clips = new List<FlatClip>();
        foreach (var track in report.AllTracks)
        {
            foreach (var c in track.Clips)
            {
                var startSec = c.Start;
                var endSec = c.Start + c.Duration;
                var sf = TimeUtil.SecToFrame(startSec, fps);
                var ef = TimeUtil.SecToFrame(endSec, fps);
                if (ef <= sf)
                {
                    ef = sf + 1; // at least 1 frame
                }

                var bi = TimeUtil.SecToFrame(c.BlendInDuration, fps);
                var bo = TimeUtil.SecToFrame(c.BlendOutDuration, fps);
                var ei = TimeUtil.SecToFrame(c.EaseInDuration, fps);
                var eo = TimeUtil.SecToFrame(c.EaseOutDuration, fps);
                // clamp blend windows to clip length
                var len = ef - sf;
                if (bi > len)
                {
                    bi = len;
                }

                if (bo > len - bi)
                {
                    bo = Math.Max(0, len - bi);
                }

                clips.Add(new FlatClip(
                    track.Name,
                    track.TypeName,
                    c.DisplayName,
                    sf,
                    ef,
                    startSec,
                    endSec,
                    bi,
                    bo,
                    ei,
                    eo));
            }
        }

        clips.Sort((a, b) =>
        {
            var c = a.StartFrame.CompareTo(b.StartFrame);
            return c != 0 ? c : string.CompareOrdinal(a.TrackName, b.TrackName);
        });

        var duration = clips.Count == 0 ? 0 : clips.Max(c => c.EndFrame);
        return new Sim(report.Path, report.TimelineName, fps, clips, duration);
    }

    public static Sim FromFile(string path) =>
        FromReport(PlayableInspector.InspectFile(path));

    public static string FormatFull(Sim sim)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"file: {sim.Path}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"timeline: {sim.TimelineName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"frame_rate: {sim.FrameRate:0.###}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"duration: {sim.DurationFrames} frames");
        sb.AppendLine($"clip_count: {sim.Clips.Count}");
        sb.AppendLine();
        sb.AppendLine("## clip windows (start → end frames)");
        if (sim.Clips.Count == 0)
        {
            sb.AppendLine("(no clips)");
        }
        else
        {
            foreach (var c in sim.Clips)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  f {c.StartFrame,5} → {c.EndFrame,5}   [{c.TrackName}] {c.ClipName}  ({c.DurationFrames}f)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## edges (when something starts or ends)");
        foreach (var line in EdgeLines(sim))
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine("## active set at each boundary frame");
        foreach (var f in BoundaryFrames(sim))
        {
            sb.Append(FormatAtFrame(sim, f));
        }

        return sb.ToString();
    }

    public static string FormatAtFrame(Sim sim, int f)
    {
        var sb = new StringBuilder();
        var active = sim.ActiveAtFrame(f);
        sb.AppendLine(CultureInfo.InvariantCulture, $"--- at frame {f} ---");
        if (active.Count == 0)
        {
            sb.AppendLine("  (nothing running)");
        }
        else
        {
            foreach (var c in active.OrderBy(x => x.TrackName, StringComparer.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  RUNNING  [{c.TrackName}] {c.ClipName}  (f{c.StartFrame} → f{c.EndFrame})");
            }
        }

        return sb.ToString();
    }

    /// <summary>Sample every <paramref name="stepFrames"/> frames from 0 → duration.</summary>
    public static string FormatStepped(Sim sim, int stepFrames)
    {
        if (stepFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepFrames), "step must be > 0 frames");
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"file: {sim.Path}  step={stepFrames}f  duration={sim.DurationFrames}f  fps={sim.FrameRate:0.###}");
        sb.AppendLine();

        for (var f = 0; f < sim.DurationFrames; f += stepFrames)
        {
            sb.Append(FormatAtFrame(sim, f));
        }

        sb.Append(FormatAtFrame(sim, sim.DurationFrames));
        return sb.ToString();
    }

    private static IEnumerable<string> EdgeLines(Sim sim)
    {
        var edges = new List<(int F, string Kind, FlatClip Clip)>();
        foreach (var c in sim.Clips)
        {
            edges.Add((c.StartFrame, "START", c));
            edges.Add((c.EndFrame, "END", c));
        }

        edges.Sort((a, b) =>
        {
            var c = a.F.CompareTo(b.F);
            if (c != 0)
            {
                return c;
            }

            var ka = a.Kind == "END" ? 0 : 1;
            var kb = b.Kind == "END" ? 0 : 1;
            c = ka.CompareTo(kb);
            return c != 0 ? c : string.CompareOrdinal(a.Clip.TrackName, b.Clip.TrackName);
        });

        foreach (var e in edges)
        {
            yield return string.Create(CultureInfo.InvariantCulture,
                $"  f={e.F,5}  {e.Kind,-5}  [{e.Clip.TrackName}] {e.Clip.ClipName}");
        }
    }

    private static List<int> BoundaryFrames(Sim sim)
    {
        var set = new SortedSet<int> { 0 };
        foreach (var c in sim.Clips)
        {
            set.Add(c.StartFrame);
            set.Add(c.EndFrame);
        }

        return set.ToList();
    }
}
