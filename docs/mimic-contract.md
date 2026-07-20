# Mimic contract (Phase 1+)

## Time

- Canonical unit: **integer ticks**.
- One Advance = `TimeTicks += 1` when `Mode == Playing`.
- Half-open windows: clip active iff `start <= t < end`.
- `Play()` is zero-advance: establishes membership at current time and emits Enter edges for covering windows.

## Director modes

| Mode | Time advances? | Membership |
|------|----------------|------------|
| Stopped | No | none |
| Playing | Yes | windows at `t` |
| Paused | No | windows frozen at `t` |
| HoldComplete | No | if `sample_last_frame`, **display** eval at `duration-1` (no re-Enter edges after real Exits at `duration`) |

Wrap (transport transitions):

- **hold** → at `t >= duration`, `HoldComplete` + fire IR events mapped to director signal `hold_reached` (e.g. sample name `ATTACK_DONE`, not a fixed literal `hold_reached`)
- **none** → `Stopped` + IR events mapped to `range_stopped`
- **loop** → `t = 0`, re-enter windows at 0, IR events mapped to `looped`

Director signals are **projections**: only names listed in IR `events[]` with `when.director: …` appear in the named-event stream.

## Edges

- Enter: clip becomes active this frame.
- Exit: clip becomes inactive this frame.
- Same-tick abutting: Exit then Enter (sort: Exit &lt; Enter, then **ordinal track name**).
- Multi-track same tick: ordered by track name (not file index). Live edges and `Boundaries` dump use the same key.
- Hold + `sample_last_frame`: Exits at `duration` for half-open end; membership display holds last frame **without** synthetic re-Enter.

## Named events

Mapped from IR when matching clip edge or director signal fires (clip edges after ordered edge list; director signals after wrap transition).

## Seek / hard reset

- `Seek(t)`: jump clock (clamped), Diff membership Exit/Enter, no wrap fire.
- `HardReset()`: Stopped at initial time, force Exit all.

## Oracle

`Oracle.Run` == `TimelineAgent.RunScript` for Phase 1 (shared semantics).  
`Oracle.ActiveSetAt` is independent pure membership.
