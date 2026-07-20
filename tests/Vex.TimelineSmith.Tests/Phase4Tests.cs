using FluentAssertions;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;

namespace Vex.TimelineSmith.Tests;

public class Phase4Tests
{
    private static string SamplePath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples", "light_attack", "light_attack.timeline.yaml"));

    private static TimelineTables LoadSample()
    {
        var r = TimelineLoader.LoadFile(SamplePath);
        r.IsSuccess.Should().BeTrue(string.Join("; ", r.Errors));
        return TimelineTables.FromDocument(r.Value!);
    }

    [Fact]
    public void Batch_RunScriptAll_64_matches_single_agent_and_no_bleed()
    {
        var tables = LoadSample();
        var script = new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() };
        var single = new TimelineAgent(tables).RunScript(script).CanonicalString();

        var batch = new BatchAgents(tables, 64);
        var logs = batch.RunScriptAll(script);
        logs.Should().HaveCount(64);
        foreach (var log in logs)
        {
            log.CanonicalString().Should().Be(single);
        }

        logs.Select(l => l.CanonicalString()).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Batch_TickAll_advances_without_static_bleed()
    {
        var tables = LoadSample();
        var batch = new BatchAgents(tables, 64);
        batch.PlayAll();

        // Partial run: 20 ticks, then pause agent 0 only and keep others going
        for (var t = 0; t < 20; t++)
        {
            batch.TickAll();
        }

        batch[0].Pause();
        var t0 = batch[0].TimeTicks;
        for (var t = 0; t < 5; t++)
        {
            batch.TickAll();
        }

        batch[0].TimeTicks.Should().Be(t0);
        batch[1].TimeTicks.Should().Be(t0 + 5);
        batch[63].TimeTicks.Should().Be(t0 + 5);
        batch[0].Mode.Should().Be(DirectorMode.Paused);
        batch[1].Mode.Should().Be(DirectorMode.Playing);
    }

    [Fact]
    public void Batch_vs_N_sequential_identical_on_representative_script()
    {
        var tables = LoadSample();
        var script = new ScriptOp[]
        {
            new ScriptOp.Play(),
            new ScriptOp.Advance(10),
            new ScriptOp.Pause(),
            new ScriptOp.Resume(),
            new ScriptOp.Advance(5),
            new ScriptOp.RunToEnd(),
        };

        var n = 32;
        var sequential = Enumerable.Range(0, n)
            .Select(_ => new TimelineAgent(tables).RunScript(script).CanonicalString())
            .ToList();

        var batch = new BatchAgents(tables, n);
        var batchLogs = batch.RunScriptAll(script).Select(l => l.CanonicalString()).ToList();

        batchLogs.Should().Equal(sequential);
    }
}
