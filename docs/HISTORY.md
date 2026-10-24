# Memory Bloom engineering history

This is the navigation point for the engineering audit. Each linked version record is authoritative for that checkpoint and includes its request, reported bugs, last/resulting/next states, implementation challenges, decisions, limitations, release artifacts, and every retained screenshot. The public product overview remains in [`README.md`](../README.md); concise release outcomes remain in [`CHANGELOG.md`](../CHANGELOG.md).

Screenshot files stay centralized under [`docs/screenshots/<version>/`](screenshots/) so each image has one canonical path. The corresponding version record explains why each image was retained.

## Version records

| Checkpoint | Focus | Exhaustive record |
|---|---|---|
| 0.13.0 | Persistent pressure timeline and mode-aware Windows Snap | [v0.13.0](history/v0.13.0.md) |
| 0.12.0 | Persistent process typography and responsive row flow | [v0.12.0](history/v0.12.0.md) |
| 0.11.0 | Companion deployment guidance and consistent root interactions | [v0.11.0](history/v0.11.0.md) |
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

## Current state — 0.13.0

- Memory Bloom appears only when configured memory pressure warrants attention, with proportional compact opacity and intensity; hover temporarily makes it fully opaque.
- Compact mode remains natively draggable but deliberately does not resize, maximize, or Snap. Expanded Process and History screens retain standard Windows resize/Snap behavior, Alt+Tab presence, inward monitor-edge expansion, and best-effort visibility across virtual desktops.
- Compact mode and the detailed title bar share a dark right-click **Exit Memory Bloom** action; detailed process rows retain focus and end-process actions.
- Process groups—including the changing **Windows, cache & shared** estimate—children, and the Firefox Tab hosts aggregate sort by current memory while preserving collapsible state.
- Windows/cache/kernel/shared memory explains the gap between process totals and physical use.
- Firefox titles come from a live foreground observation, delayed local recovery data, or the optional companion. PID placement remains measured inference with confidence, age, expiry, historical warnings, and an explicit unresolved state.
- The Tab hosts diagnostic currently places Unresolved first and marks hosts with current inferred pages using a blue `●`.
- The README explains why the Firefox companion is necessary for prompt full-tab awareness and compares per-restart temporary loading with persistent unlisted Mozilla signing.
- Detailed mode includes a persistent 10–18 pt process-text slider. Root rows, nested PIDs, tab titles, secondary metadata, indentation, row height, and the right-aligned memory column reflow with font and viewport changes.
- The expanded title bar switches directly between Processes and History. History measures pressure every two seconds while Bloom runs, persists ten-second samples and thirty-second top-process context, flags exceptional changes, and retains 30 days with older-data compaction.
- Historical Windows resource-exhaustion, unexpected restart/shutdown, application-crash, and error-report evidence is imported as separately styled event markers. Resource-exhaustion events include actual physical/commit levels and recorded leading processes when Windows retained them; gaps remain gaps.
- The timeline supports presets, mouse-wheel zoom, drag panning, exact hover inspection, and a Live reset.

See the [0.13.0 record](history/v0.13.0.md) for this prompt's full change and visual evidence, or use the table above for earlier iterations.

## Next state — 0.14.0

- Correlate a selected Windows crash or resource-exhaustion marker with the nearest Bloom samples in a dedicated incident summary.
- Consider per-application historical series and export only after real usage shows which incident questions the aggregate timeline cannot answer.
- Validate retention, spike sensitivity, and event density during normal sign-in operation before adding more event sources.
