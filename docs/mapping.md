# Mapping: Timeline concepts → offline

| Timeline / BL | Offline |
|---------------|---------|
| PlayableDirector clock | `TimelineAgent.TimeTicks` + mode |
| Clip active range | half-open membership on tables |
| Concurrent tracks | multi-track active set + edges |
| StateSmith | optional gameplay consumer of named events (not track sequencer) |
| Canopy | unrelated app HFSM |

Phase 1 does **not** generate StateSmith for tracks. Edges are pure C#.
