using System.Globalization;
using System.Text.RegularExpressions;

namespace Vex.TimelineSmith.UnityYaml;

/// <summary>
/// Offline inspect of Unity Timeline .playable assets saved as text YAML.
/// No Unity Editor / engine required.
/// </summary>
public static class PlayableInspector
{
    public sealed record ClipInfo(
        string DisplayName,
        double Start,
        double Duration,
        double BlendInDuration = 0,
        double BlendOutDuration = 0,
        double EaseInDuration = 0,
        double EaseOutDuration = 0);

    public sealed record TrackInfo(
        long FileId,
        string Name,
        string TypeName,
        int ClipCount,
        IReadOnlyList<ClipInfo> Clips,
        IReadOnlyList<long> ChildTrackIds);

    public sealed record Report(
        string Path,
        string TimelineName,
        int RootTrackCount,
        int TotalTrackCount,
        IReadOnlyList<TrackInfo> RootTracks,
        IReadOnlyList<TrackInfo> AllTracks,
        /// <summary>From TimelineAsset m_EditorSettings.m_Framerate (default 60).</summary>
        double FrameRate = 60);

    public static Report InspectFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Playable not found.", path);
        }

        var text = File.ReadAllText(path);
        if (text.Length >= 1 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        // Binary / non-text assets are not supported offline.
        if (!text.Contains("%YAML", StringComparison.Ordinal) &&
            !text.Contains("MonoBehaviour:", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "File is not a text YAML Unity asset. Force text serialization or open in Editor.");
        }

        return InspectYaml(text, path);
    }

    public static Report InspectYaml(string yaml, string pathLabel = "<yaml>")
    {
        var blocks = SplitBlocks(yaml);
        var byId = new Dictionary<long, UnityBlock>();

        foreach (var b in blocks)
        {
            if (b.FileId != 0)
            {
                byId[b.FileId] = b;
            }
        }

        var timeline = blocks.FirstOrDefault(b =>
            b.ClassIdentifier.Contains("TimelineAsset", StringComparison.Ordinal) ||
            b.ScriptGuid is "bfda56da833e2384a9677cd3c976a436");

        if (timeline is null)
        {
            throw new InvalidDataException(
                $"No TimelineAsset found in '{pathLabel}'. Is this a .playable Timeline?");
        }

        var rootIds = ParseFileIdList(timeline.Body, "m_Tracks");
        var allTracks = new List<TrackInfo>();
        var rootTracks = new List<TrackInfo>();
        var seen = new HashSet<long>();

        void Visit(long id, bool isRoot)
        {
            if (!seen.Add(id))
            {
                return;
            }

            if (!byId.TryGetValue(id, out var block))
            {
                var missing = new TrackInfo(id, $"<missing fileID {id}>", "?", 0, Array.Empty<ClipInfo>(), Array.Empty<long>());
                allTracks.Add(missing);
                if (isRoot)
                {
                    rootTracks.Add(missing);
                }

                return;
            }

            var children = ParseFileIdList(block.Body, "m_Children");
            var clips = ParseClips(block.Body);
            var typeName = ShortType(block.ClassIdentifier);
            var info = new TrackInfo(
                id,
                string.IsNullOrWhiteSpace(block.Name) ? "(unnamed)" : block.Name,
                typeName,
                clips.Count,
                clips,
                children);

            allTracks.Add(info);
            if (isRoot)
            {
                rootTracks.Add(info);
            }

            foreach (var c in children)
            {
                Visit(c, isRoot: false);
            }
        }

        foreach (var id in rootIds)
        {
            Visit(id, isRoot: true);
        }

        var fps = ParseFrameRate(timeline.Body);
        return new Report(
            pathLabel,
            string.IsNullOrWhiteSpace(timeline.Name) ? "(timeline)" : timeline.Name,
            rootTracks.Count,
            allTracks.Count,
            rootTracks,
            allTracks,
            fps);
    }

    public static string Format(Report r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"file: {r.Path}");
        sb.AppendLine($"timeline: {r.TimelineName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"frame_rate: {r.FrameRate:0.###}");
        sb.AppendLine($"root_tracks: {r.RootTrackCount}");
        sb.AppendLine($"total_tracks: {r.TotalTrackCount}");
        sb.AppendLine("tracks:");
        foreach (var t in r.AllTracks)
        {
            var root = r.RootTracks.Any(x => x.FileId == t.FileId) ? "root" : "child";
            sb.AppendLine($"  - [{root}] {t.Name}  type={t.TypeName}  clips={t.ClipCount}");
            foreach (var c in t.Clips)
            {
                var sf = TimeUtil.SecToFrame(c.Start, r.FrameRate);
                var ef = TimeUtil.SecToFrame(c.Start + c.Duration, r.FrameRate);
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"      clip: {c.DisplayName}  frames={sf}→{ef}  ({c.Start:0.###}s + {c.Duration:0.###}s)");
            }
        }

        return sb.ToString();
    }

    private static double ParseFrameRate(string timelineBody)
    {
        // m_EditorSettings:\n    m_Framerate: 60
        var m = Regex.Match(timelineBody, @"m_Framerate:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.Multiline);
        if (m.Success &&
            double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) &&
            fps > 0)
        {
            return fps;
        }

        return 60;
    }

    private sealed class UnityBlock
    {
        public long FileId;
        public string Name = "";
        public string ClassIdentifier = "";
        public string? ScriptGuid;
        public string Body = "";
    }

    private static List<UnityBlock> SplitBlocks(string yaml)
    {
        // Documents start with --- !u!114 &id  (MonoBehaviour) or --- !u!74 etc.
        var parts = Regex.Split(yaml, @"(?=^---\s*!u!\d+)", RegexOptions.Multiline);
        var list = new List<UnityBlock>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || part.StartsWith("%YAML", StringComparison.Ordinal))
            {
                continue;
            }

            var header = part.Split('\n')[0];
            var idMatch = Regex.Match(header, @"&(-?\d+)");
            if (!idMatch.Success)
            {
                continue;
            }

            if (!part.Contains("MonoBehaviour:", StringComparison.Ordinal))
            {
                continue;
            }

            var fileId = long.Parse(idMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var name = MatchField(part, "m_Name") ?? "";
            var classId = MatchField(part, "m_EditorClassIdentifier") ?? "";
            var scriptLine = Regex.Match(part, @"m_Script:\s*\{[^}]*guid:\s*([a-f0-9]+)", RegexOptions.IgnoreCase);
            list.Add(new UnityBlock
            {
                FileId = fileId,
                Name = name,
                ClassIdentifier = classId,
                ScriptGuid = scriptLine.Success ? scriptLine.Groups[1].Value : null,
                Body = part,
            });
        }

        return list;
    }

    private static string? MatchField(string body, string field)
    {
        var m = Regex.Match(body, @"^\s*" + Regex.Escape(field) + @":\s*(.*)$", RegexOptions.Multiline);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups[1].Value.Trim();
    }

    private static List<long> ParseFileIdList(string body, string field)
    {
        // m_Tracks:\n  - {fileID: 123}\n  - {fileID: 456}
        var m = Regex.Match(
            body,
            @"^\s*" + Regex.Escape(field) + @":\s*\n((?:\s*-\s*\{fileID:\s*-?\d+\}\s*\n)*)",
            RegexOptions.Multiline);

        if (!m.Success)
        {
            // empty list: m_Tracks: [] 
            var empty = Regex.Match(body, @"^\s*" + Regex.Escape(field) + @":\s*\[\]\s*$", RegexOptions.Multiline);
            return empty.Success ? new List<long>() : new List<long>();
        }

        var ids = new List<long>();
        foreach (Match fm in Regex.Matches(m.Groups[1].Value, @"fileID:\s*(-?\d+)"))
        {
            ids.Add(long.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        return ids;
    }

    private static List<ClipInfo> ParseClips(string body)
    {
        // Within a track block, m_Clips: then entries with m_Start, m_Duration, m_DisplayName
        var clipsIdx = body.IndexOf("\n  m_Clips:", StringComparison.Ordinal);
        if (clipsIdx < 0)
        {
            clipsIdx = body.IndexOf("\nm_Clips:", StringComparison.Ordinal);
        }

        if (clipsIdx < 0)
        {
            return new List<ClipInfo>();
        }

        var section = body[(clipsIdx + 1)..];
        // stop at next top-ish field at 2-space indent that isn't part of clip
        // clips entries are "  - m_Version" under m_Clips
        var clipChunks = Regex.Split(section, @"(?=^\s+-\s+m_Version:)", RegexOptions.Multiline);
        var list = new List<ClipInfo>();
        foreach (var chunk in clipChunks)
        {
            if (!chunk.Contains("m_Start:", StringComparison.Ordinal))
            {
                continue;
            }

            var start = ParseDoubleField(chunk, "m_Start") ?? 0;
            var dur = ParseDoubleField(chunk, "m_Duration") ?? 0;
            var display = MatchField(chunk, "m_DisplayName") ?? "(clip)";
            var blendIn = ParseDoubleField(chunk, "m_BlendInDuration") ?? 0;
            var blendOut = ParseDoubleField(chunk, "m_BlendOutDuration") ?? 0;
            var easeIn = ParseDoubleField(chunk, "m_EaseInDuration") ?? 0;
            var easeOut = ParseDoubleField(chunk, "m_EaseOutDuration") ?? 0;
            list.Add(new ClipInfo(display, start, dur, blendIn, blendOut, easeIn, easeOut));
        }

        return list;
    }

    private static double? ParseDoubleField(string body, string field)
    {
        var s = MatchField(body, field);
        if (s is null)
        {
            return null;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string ShortType(string classIdentifier)
    {
        if (string.IsNullOrWhiteSpace(classIdentifier))
        {
            return "?";
        }

        // Assembly::Namespace.Type or just Type
        var t = classIdentifier;
        var col = t.LastIndexOf(':');
        if (col >= 0 && col + 1 < t.Length)
        {
            t = t[(col + 1)..];
        }

        var dot = t.LastIndexOf('.');
        return dot >= 0 ? t[(dot + 1)..] : t;
    }
}
