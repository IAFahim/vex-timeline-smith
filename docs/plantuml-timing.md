# Unity Timeline → PlantUML timing diagram

PlantUML source studied: `/home/i/GitHub/plantuml` (`src/main/java/.../timingdiagram`, [docs](https://plantuml.com/timing-diagram)).

## Mapping (Unity-ish, **frames by default**)

Unity stores seconds; we convert with `m_Framerate` (usually 60):  
`frame = floor(seconds * fps)`.

| Unity | PlantUML timing |
|-------|-----------------|
| Track row | `concise "Track Name" as Code` |
| Clip on track | state name on that player |
| No clip | `{hidden}` |
| Clip start/end | `@109` / `@169` (**frames**, not seconds) |

## CLI

```bash
dotnet run --project src/Vex.TimelineSmith.Tool -- timing path/to.playable
dotnet run --project src/Vex.TimelineSmith.Tool -- timing path/to.playable --out out.timing.puml
```

Render (needs Java + plantuml.jar):

```bash
java -jar plantuml.jar -tpng out.timing.puml
```
