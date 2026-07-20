using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Vex.TimelineSmith.UnityYaml;

/// <summary>
/// Lossless .playable recovery via PlantUML-safe comment payload.
/// Forward: wrap original file bytes (base64) in the generated .puml.
/// Reverse: extract raw bytes only — never re-serialize YAML.
/// </summary>
public static class PlayableEmbed
{
    public const string BeginMarker = "===VEX_PLAYABLE_BYTES_V1===";
    public const string EndMarker = "===END_VEX_PLAYABLE_BYTES_V1===";
    public const string ShaPrefix = "sha256:";
    private const int LineWidth = 76;

    public sealed class EmbedException : Exception
    {
        public EmbedException(string message) : base(message) { }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Hex(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Append lossless payload as PlantUML line comments after the diagram body.
    /// </summary>
    public static string AppendEmbed(string plantUmlSource, ReadOnlySpan<byte> playableBytes)
    {
        if (playableBytes.IsEmpty)
        {
            throw new EmbedException("Cannot embed empty playable bytes.");
        }

        var b64 = Convert.ToBase64String(playableBytes);
        var sha = Sha256Hex(playableBytes);
        var sb = new StringBuilder(plantUmlSource.Length + b64.Length + 256);

        // Ensure trailing newline before payload
        sb.Append(plantUmlSource.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.Append("' ").AppendLine(BeginMarker);
        sb.Append("' ").Append(ShaPrefix).AppendLine(sha);
        for (var i = 0; i < b64.Length; i += LineWidth)
        {
            var len = Math.Min(LineWidth, b64.Length - i);
            sb.Append("' ").Append(b64, i, len).AppendLine();
        }

        sb.Append("' ").AppendLine(EndMarker);
        return sb.ToString();
    }

    public static string AppendEmbedFromFile(string plantUmlSource, string playablePath)
    {
        var bytes = File.ReadAllBytes(playablePath);
        return AppendEmbed(plantUmlSource, bytes);
    }

    /// <summary>Extract original playable bytes from a .puml with embed block.</summary>
    public static byte[] ExtractBytes(string plantUmlSource)
    {
        if (string.IsNullOrEmpty(plantUmlSource))
        {
            throw new EmbedException("Empty PlantUML source.");
        }

        var begin = plantUmlSource.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (begin < 0)
        {
            throw new EmbedException(
                $"Missing lossless payload ({BeginMarker} … {EndMarker}). Re-export with timing from a .playable.");
        }

        // Search end only after begin — avoids false hit if diagram/path text contains EndMarker.
        var end = plantUmlSource.IndexOf(EndMarker, begin + BeginMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new EmbedException(
                $"Missing lossless payload end marker ({EndMarker}). Re-export with timing from a .playable.");
        }

        // Slice between markers (after begin line, before end line)
        var afterBegin = plantUmlSource.IndexOf('\n', begin);
        if (afterBegin < 0)
        {
            throw new EmbedException("Corrupt embed: no newline after begin marker.");
        }

        var block = plantUmlSource.Substring(afterBegin + 1, end - afterBegin - 1);
        string? expectedSha = null;
        var b64 = new StringBuilder();

        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            // Strip leading comment marker and optional space
            if (line.StartsWith("'", StringComparison.Ordinal))
            {
                line = line[1..];
                if (line.StartsWith(' '))
                {
                    line = line[1..];
                }
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(ShaPrefix, StringComparison.Ordinal))
            {
                expectedSha = line[ShaPrefix.Length..].Trim().ToLowerInvariant();
                continue;
            }

            // Base64 alphabet only
            if (!Regex.IsMatch(line, @"^[A-Za-z0-9+/=]+$"))
            {
                throw new EmbedException($"Corrupt embed line (not base64): {line[..Math.Min(40, line.Length)]}…");
            }

            b64.Append(line);
        }

        if (b64.Length == 0)
        {
            throw new EmbedException("Corrupt embed: empty base64 payload.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(b64.ToString());
        }
        catch (FormatException ex)
        {
            throw new EmbedException($"Corrupt embed: invalid base64 ({ex.Message}).");
        }

        if (bytes.Length == 0)
        {
            throw new EmbedException("Corrupt embed: decoded zero bytes.");
        }

        if (expectedSha is not null)
        {
            var actual = Sha256Hex(bytes);
            if (!string.Equals(actual, expectedSha, StringComparison.Ordinal))
            {
                throw new EmbedException(
                    $"Embed SHA256 mismatch: expected {expectedSha}, got {actual}.");
            }
        }

        return bytes;
    }

    public static void WritePlayableFromPuml(string plantUmlPath, string playableOutPath)
    {
        var puml = File.ReadAllText(plantUmlPath);
        var bytes = ExtractBytes(puml);
        var dir = Path.GetDirectoryName(playableOutPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(playableOutPath, bytes);
    }

    public static void WritePlayableFromPumlText(string plantUmlText, string playableOutPath)
    {
        var bytes = ExtractBytes(plantUmlText);
        var dir = Path.GetDirectoryName(playableOutPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(playableOutPath, bytes);
    }
}
