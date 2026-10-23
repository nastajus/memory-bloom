# Memory Bloom engineering history

This is the navigation point for the engineering audit. Each linked version record is authoritative for that checkpoint and includes its request, reported bugs, last/resulting/next states, implementation challenges, decisions, limitations, release artifacts, and every retained screenshot. The public product overview remains in [`README.md`](../README.md); concise release outcomes remain in [`CHANGELOG.md`](../CHANGELOG.md).

Screenshot files stay centralized under [`docs/screenshots/<version>/`](screenshots/) so each image has one canonical path. The corresponding version record explains why each image was retained.

## Version records

| Checkpoint | Focus | Exhaustive record |
|---|---|---|
| 0.10.0 | Compact exit action and documentation separation | [v0.10.0](history/v0.10.0.md) |
| 0.9.0 | Pressure tiers and diagnostic-first Firefox ordering | [v0.9.0](history/v0.9.0.md) |
| 0.8.0 | Firefox companion and durable historical evidence | [v0.8.0](history/v0.8.0.md) |
| 0.7.0 | Edge-aware expansion and expiring evidence | [v0.7.0](history/v0.7.0.md) |
| 0.6.0 | Honest Firefox host language and visual polish | [v0.6.0](history/v0.6.0.md) |
| 0.5.0 | Native Windows behavior, branding, and faster evidence | [v0.5.0](history/v0.5.0.md) |
| 0.4.0 | Per-process Firefox inference and visual audit workflow | [v0.4.0](history/v0.4.0.md) |
| 0.3.0 | Firefox metadata and system-memory accounting | [v0.3.0](history/v0.3.0.md) |
| 0.2.0 | Click/drag/process interaction and stable trees | [v0.2.0](history/v0.2.0.md) |
| 0.1.0 | Initial conditional pressure companion | [v0.1.0](history/v0.1.0.md) |

## Current state — 0.10.0

- Memory Bloom appears only when configured memory pressure warrants attention, with proportional compact opacity and intensity; hover temporarily makes it fully opaque.
- Compact and detailed modes support native Windows move, resize, Snap, Alt+Tab, inward monitor-edge expansion, scaling, and best-effort visibility across virtual desktops.
- The compact view now has a dark right-click **Exit Memory Bloom** action; detailed process rows retain focus and end-process actions.
- Process groups, children, and the Firefox Tab hosts aggregate sort by memory while preserving collapsible state.
- Windows/cache/kernel/shared memory explains the gap between process totals and physical use.
- Firefox titles come from a live foreground observation, delayed local recovery data, or the optional companion. PID placement remains measured inference with confidence, age, expiry, historical warnings, and an explicit unresolved state.
- The Tab hosts diagnostic currently places Unresolved first and marks hosts with current inferred pages using a blue `●`.

See the [0.10.0 record](history/v0.10.0.md) for this prompt's full change and visual evidence, or use the table above for earlier iterations.

## Next state — 0.11.0

- Load and validate the optional companion in Firefox against ordinary foreground and background tab activity before pursuing Mozilla signing.
- Tune event-to-process correlation with real reload/navigation cases and return Unresolved tabs to the bottom when it becomes exceptional.
- Add direct tab activation or closing only after the live companion catalog has been validated.
