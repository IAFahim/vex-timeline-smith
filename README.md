# Vex.TimelineSmith

Offline **Timeline IR → pure C# agents** for headless simulation.

StateSmith is **not** the timeline engine. It is an optional **gameplay edge consumer** via `EventInbox`.

## Quick start

```bash
dotnet test
dotnet run --project src/Vex.TimelineSmith.Tool -- validate samples/light_attack/light_attack.timeline.yaml
dotnet run --project src/Vex.TimelineSmith.Tool -- run samples/light_attack/light_attack.timeline.yaml
```

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
- `Vex.TimelineSmith.Tool` — CLI (`validate` / `compile` / `run`)

See `docs/mimic-contract.md`.
