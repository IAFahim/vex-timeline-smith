# Timeline IR v1

See `samples/light_attack/light_attack.timeline.yaml`.

## Required fields

- `schema_id`: `vex.timeline-ir`
- `schema_version`: `1`
- `id`: document id
- `time.unit`: `ticks`
- `time.tick_hz`: &gt; 0
- `director.duration_ticks`, `wrap` (`none|hold|loop`)
- `tracks[]` with `id`, `clips[]` (`start_tick`, `end_tick`, `kind`, `payload`)
- `events[]` optional named projections

## Kinds

| kind | payload |
|------|---------|
| phase | `phase: string` |
| hitbox | `damage: int`, `hitbox: string` |
| signal | `fx: string` |

## Rules

- No overlapping clips on a track (`overlap: forbid` only in v1).
- Touching ends OK (`a.end == b.start`).
- Clips within `[0, duration_ticks)`.
