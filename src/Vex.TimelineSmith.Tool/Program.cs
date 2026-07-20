using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using Vex.TimelineSmith.Compiler;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;
using Vex.TimelineSmith.UnityYaml;

var irArg = new Argument<FileInfo>("ir", "Path to timeline IR YAML");
var outOpt = new Option<DirectoryInfo?>("--out", "Output directory for compile");

var validate = new Command("validate", "Validate timeline IR")
{
    irArg,
};
validate.SetHandler((InvocationContext ctx) =>
{
    var ir = ctx.ParseResult.GetValueForArgument(irArg);
    var r = TimelineLoader.LoadFile(ir.FullName);
    if (!r.IsSuccess)
    {
        foreach (var e in r.Errors)
        {
            Console.Error.WriteLine(e);
        }

        ctx.ExitCode = 1;
        return;
    }

    Console.WriteLine($"OK {r.Value!.Id} tracks={r.Value.Tracks.Count} events={r.Value.Events.Count}");
});

var compile = new Command("compile", "Compile IR to tables dump + stubs")
{
    irArg,
    outOpt,
};
compile.SetHandler((InvocationContext ctx) =>
{
    var ir = ctx.ParseResult.GetValueForArgument(irArg);
    var outDir = ctx.ParseResult.GetValueForOption(outOpt);
    var r = Emitter.CompileFile(ir.FullName);
    if (!r.IsSuccess)
    {
        foreach (var e in r.Errors)
        {
            Console.Error.WriteLine(e);
        }

        ctx.ExitCode = 1;
        return;
    }

    var dest = outDir?.FullName ?? Path.Combine(ir.DirectoryName ?? ".", "gen");
    Emitter.WriteToDirectory(r.Value!, dest);
    Console.WriteLine($"Wrote {dest}");
});

var playableArg = new Argument<FileInfo>("playable", "Path to Unity Timeline .playable (text YAML)");
var atOpt = new Option<int?>("--at", "Frame index; print which clips are RUNNING (default unit: frames @ timeline fps)");
var stepOpt = new Option<int?>("--step", "Sample every N frames from 0 → duration");

var tracksCmd = new Command("tracks", "Read a Unity .playable and list how many tracks (and clips)")
{
    playableArg,
};
tracksCmd.SetHandler((InvocationContext ctx) =>
{
    var playable = ctx.ParseResult.GetValueForArgument(playableArg);
    try
    {
        var report = PlayableInspector.InspectFile(playable.FullName);
        Console.Write(PlayableInspector.Format(report));
        Console.WriteLine($"TRACK_COUNT={report.RootTrackCount}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        ctx.ExitCode = 1;
    }
});

// Primary: Unity .playable — schedule in frames + "what's running at frame f"
var run = new Command("run", "Unity .playable in frames: schedule, or --at <frame> / --step <frames>")
{
    playableArg,
    atOpt,
    stepOpt,
};
run.SetHandler((InvocationContext ctx) =>
{
    var playable = ctx.ParseResult.GetValueForArgument(playableArg);
    var at = ctx.ParseResult.GetValueForOption(atOpt);
    var step = ctx.ParseResult.GetValueForOption(stepOpt);
    try
    {
        var sim = PlayableRun.FromFile(playable.FullName);
        if (at is { } f)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"file: {sim.Path}  timeline: {sim.TimelineName}  fps={sim.FrameRate:0.###}  duration={sim.DurationFrames}f"));
            Console.Write(PlayableRun.FormatAtFrame(sim, f));
        }
        else if (step is { } s)
        {
            Console.Write(PlayableRun.FormatStepped(sim, s));
        }
        else
        {
            Console.Write(PlayableRun.FormatFull(sim));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        ctx.ExitCode = 1;
    }
});

// Old offline IR path (legacy)
var runIr = new Command("run-ir", "Legacy: play IR YAML to end and print edge trace")
{
    irArg,
};
runIr.SetHandler((InvocationContext ctx) =>
{
    var ir = ctx.ParseResult.GetValueForArgument(irArg);
    var loaded = TimelineLoader.LoadFile(ir.FullName);
    if (!loaded.IsSuccess)
    {
        foreach (var e in loaded.Errors)
        {
            Console.Error.WriteLine(e);
        }

        ctx.ExitCode = 1;
        return;
    }

    var tables = TimelineTables.FromDocument(loaded.Value!);
    var agent = new TimelineAgent(tables);
    var log = agent.RunScript(new ScriptOp[] { new ScriptOp.Play(), new ScriptOp.RunToEnd() });
    Console.Write(log.CanonicalString());
});

var timingOutOpt = new Option<FileInfo?>("--out", "Write .puml here (default: <playable>.timing.puml)");
var timingThemeOpt = new Option<string?>(
    "--theme",
    "PlantUML theme (cyborg, crt-green, hacker, mono, …). Timing gallery themes work; use lowercase e.g. sunlust");
var timingCmd = new Command(
    "timing",
    "Unity .playable → PlantUML timing diagram (one concise lane per track, clip = state)")
{
    playableArg,
    timingOutOpt,
    timingThemeOpt,
};
timingCmd.SetHandler((InvocationContext ctx) =>
{
    var playable = ctx.ParseResult.GetValueForArgument(playableArg);
    var outFile = ctx.ParseResult.GetValueForOption(timingOutOpt);
    var theme = ctx.ParseResult.GetValueForOption(timingThemeOpt);
    try
    {
        var puml = PlayableTimingDiagram.GenerateFromFile(
            playable.FullName,
            new PlayableTimingDiagram.Options(Theme: theme));
        var dest = outFile?.FullName
                   ?? Path.ChangeExtension(playable.FullName, ".timing.puml");
        // if playable is outside writable tree, fall back to cwd
        try
        {
            File.WriteAllText(dest, puml);
        }
        catch (UnauthorizedAccessException)
        {
            dest = Path.GetFileName(dest);
            File.WriteAllText(dest, puml);
        }

        Console.WriteLine($"Wrote {Path.GetFullPath(dest)}");
        Console.WriteLine("---");
        Console.Write(puml);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        ctx.ExitCode = 1;
    }
});

var root = new RootCommand("Vex.TimelineSmith — offline tools")
{
    tracksCmd,
    run,
    timingCmd,
    runIr,
    validate,
    compile,
};

return await root.InvokeAsync(args);
