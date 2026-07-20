using System.Security.Cryptography;
using FluentAssertions;
using Vex.TimelineSmith.UnityYaml;

namespace Vex.TimelineSmith.Tests;

/// <summary>
/// Byte-identical round-trip: playable → timing puml (embed) → playable.
/// Drives shipped PlayableTimingDiagram.GenerateFromFile + PlayableEmbed.ExtractBytes.
/// </summary>
public class PlayableRoundTripTests
{
    private static string FixtureDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Playables"));

    public static IEnumerable<object[]> FixtureFiles()
    {
        if (!Directory.Exists(FixtureDir))
        {
            yield break;
        }

        foreach (var f in Directory.GetFiles(FixtureDir, "*.playable").OrderBy(x => x, StringComparer.Ordinal))
        {
            yield return new object[] { f };
        }
    }

    [Fact]
    public void Fixtures_directory_has_all_unique_corpus_playables()
    {
        Directory.Exists(FixtureDir).Should().BeTrue(FixtureDir);
        var files = Directory.GetFiles(FixtureDir, "*.playable");
        files.Length.Should().BeGreaterThanOrEqualTo(8,
            "expected committed copies of all unique /home/i/GitHub playables");
        // all distinct content
        var hashes = files.Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToHashSet();
        hashes.Count.Should().Be(files.Length);
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void RoundTrip_playable_puml_playable_byte_identical(string playablePath)
    {
        var original = File.ReadAllBytes(playablePath);
        original.Length.Should().BeGreaterThan(0);

        var puml = PlayableTimingDiagram.GenerateFromFile(playablePath);
        puml.Should().Contain(PlayableEmbed.BeginMarker);
        puml.Should().Contain(PlayableEmbed.EndMarker);
        puml.Should().Contain("@startuml");

        var recovered = PlayableEmbed.ExtractBytes(puml);
        recovered.Should().Equal(original,
            because: $"{Path.GetFileName(playablePath)} must recover byte-identical playable");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Double_roundTrip_still_byte_identical(string playablePath)
    {
        var original = File.ReadAllBytes(playablePath);
        var puml1 = PlayableTimingDiagram.GenerateFromFile(playablePath);
        var mid = PlayableEmbed.ExtractBytes(puml1);
        mid.Should().Equal(original);

        // Write mid to temp and re-export (simulates playable→puml→playable→puml→playable)
        var tmpPlay = Path.Combine(Path.GetTempPath(), "vex-rt-" + Guid.NewGuid().ToString("N") + ".playable");
        try
        {
            File.WriteAllBytes(tmpPlay, mid);
            var puml2 = PlayableTimingDiagram.GenerateFromFile(tmpPlay);
            var final = PlayableEmbed.ExtractBytes(puml2);
            final.Should().Equal(original);
        }
        finally
        {
            if (File.Exists(tmpPlay))
            {
                File.Delete(tmpPlay);
            }
        }
    }

    [Fact]
    public void Missing_embed_throws()
    {
        var bare = "@startuml\nconcise \"T\" as T\n@0\nT is X\n@enduml\n";
        var act = () => PlayableEmbed.ExtractBytes(bare);
        act.Should().Throw<PlayableEmbed.EmbedException>()
            .WithMessage("*Missing lossless payload*");
    }

    [Fact]
    public void Corrupt_sha_throws()
    {
        var playable = Directory.GetFiles(FixtureDir, "*.playable").OrderBy(x => x).First();
        var puml = PlayableTimingDiagram.GenerateFromFile(playable);
        // Flip a hex digit in sha line
        var broken = puml.Replace("sha256:", "sha256:0", StringComparison.Ordinal);
        // only if that lengthens; force bad sha
        broken = System.Text.RegularExpressions.Regex.Replace(
            broken,
            @"sha256:[0-9a-f]+",
            "sha256:0000000000000000000000000000000000000000000000000000000000000000");
        var act = () => PlayableEmbed.ExtractBytes(broken);
        act.Should().Throw<PlayableEmbed.EmbedException>()
            .WithMessage("*SHA256 mismatch*");
    }

    [Fact]
    public void WritePlayableFromPumlText_matches_source()
    {
        var playable = Directory.GetFiles(FixtureDir, "*tmpTimeline*.playable")
            .Concat(Directory.GetFiles(FixtureDir, "*.playable"))
            .First();
        var original = File.ReadAllBytes(playable);
        var puml = PlayableTimingDiagram.GenerateFromFile(playable);
        var outPath = Path.Combine(Path.GetTempPath(), "vex-write-" + Guid.NewGuid().ToString("N") + ".playable");
        try
        {
            PlayableEmbed.WritePlayableFromPumlText(puml, outPath);
            File.ReadAllBytes(outPath).Should().Equal(original);
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }
}
