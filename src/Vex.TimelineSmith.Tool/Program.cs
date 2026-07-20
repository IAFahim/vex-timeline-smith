using System.CommandLine;
using System.CommandLine.Invocation;
using Vex.TimelineSmith.Compiler;
using Vex.TimelineSmith.Ir;
using Vex.TimelineSmith.Runtime;

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

var run = new Command("run", "Play IR to end and print canonical trace")
{
    irArg,
};
run.SetHandler((InvocationContext ctx) =>
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

var root = new RootCommand("Vex.TimelineSmith — offline Timeline IR tools")
{
    validate,
    compile,
    run,
};

return await root.InvokeAsync(args);
