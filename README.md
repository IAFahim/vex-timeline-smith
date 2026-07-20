# Vex.TimelineSmith

Offline **Timeline IR → pure C# agents** for headless simulation, plus **Unity `.playable`** inspect / run-at-frame / PlantUML timing with lossless reverse.

StateSmith is **not** the timeline engine. It is an optional **gameplay edge consumer** via `EventInbox`.

**Requires:** .NET 10 SDK

## Install

### Libraries (NuGet)

```bash
dotnet add package Vex.TimelineSmith.UnityYaml   # .playable → timing puml, run-at-frame
dotnet add package Vex.TimelineSmith.Ir          # Timeline IR load/validate
dotnet add package Vex.TimelineSmith.Runtime     # headless agents
dotnet add package Vex.TimelineSmith.Compiler    # IR → stubs/tables
```

### CLI (dotnet tool)

```bash
dotnet tool install -g vex-timeline-smith
vex-tls --help
vex-tls tracks path/to.playable
vex-tls run path/to.playable --at 60
vex-tls timing path/to.playable --out out.timing.puml
vex-tls from-timing out.timing.puml --out recovered.playable
```

Local / CI feed (before nuget.org publish):

```bash
dotnet pack -c Release
dotnet tool install -g vex-timeline-smith --add-source ./artifacts/nuget --version 0.1.0
```

## Quick start (from source)

```bash
dotnet test
dotnet run --project src/Vex.TimelineSmith.Tool -- validate samples/light_attack/light_attack.timeline.yaml
dotnet run --project src/Vex.TimelineSmith.Tool -- run tests/Vex.TimelineSmith.Tests/Fixtures/Playables/05_tmpTimeline.playable --at 120
dotnet run --project src/Vex.TimelineSmith.Tool -- timing tests/Vex.TimelineSmith.Tests/Fixtures/Playables/01_CanSubTimeline.playable --out /tmp/can.timing.puml
```

## Packages

| Package | What |
|---------|------|
| `Vex.TimelineSmith.Ir` | IR load/validate, lossy timing import |
| `Vex.TimelineSmith.Runtime` | Clock, agent, oracle, inbox, batch, traces (AOT-friendly) |
| `Vex.TimelineSmith.Compiler` | Emit stubs / tables dump |
| `Vex.TimelineSmith.UnityYaml` | Unity `.playable` inspect, run-at-frame, PlantUML timing + lossless embed reverse |
| `vex-timeline-smith` (tool) | CLI: `vex-tls` |

## Phases

| Phase | What |
|-------|------|
| 1 | IR load, half-open membership Diff, ordered edges, hold sample, oracle, CLI |
| 2 | Wrap matrix (hold/none/loop), `Seek` / `HardReset`, named-event inbox |
| 3 | Lossy timing skeleton → IR (`LossyTimingImport`; Unity live export optional) |
| 4 | `BatchAgents` multi-agent tick / shared tables |
| 5 | Deferred — ECS re-embed |

## Layout

- `Vex.TimelineSmith.Ir` — load/validate IR + lossy importer
- `Vex.TimelineSmith.Runtime` — clock, agent, oracle, inbox, batch, traces
- `Vex.TimelineSmith.Compiler` — emit stubs/tables dump
- `Vex.TimelineSmith.UnityYaml` — Unity playable + PlantUML timing
- `Vex.TimelineSmith.Tool` — CLI (`tracks` / `run` / `timing` / `from-timing` / `validate` / `compile` / `run-ir`)

See `docs/mimic-contract.md` and `docs/plantuml-timing.md`.

## Pack / publish (maintainers)

Local pack:

```bash
./pack.sh
ls artifacts/nuget/
```

**nuget.org** uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (no API key secret):

1. Policy on nuget.org → workflow `publish-nuget.yml` in `IAFahim/vex-timeline-smith` (already set).
2. Bump `Version` in `Directory.Build.props` if needed.
3. Publish:

```bash
git tag v0.1.0
git push origin v0.1.0
# or: Actions → publish-nuget → Run workflow
```

Workflow: `.github/workflows/publish-nuget.yml` (OIDC → `NuGet/login@v1` → push).
