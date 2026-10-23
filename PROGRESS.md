# Memory Bloom progress record

This is the human-readable design trail. Every feature or design prompt should add a dated iteration, save new screenshots under `docs/screenshots/<version>/`, update **Current state** and **Next state**, and end with a Git commit.

## Current state — 0.4.0

- Conditional pressure widget with proportional opacity/color intensity.
- Minimal and detailed modes with click-versus-drag handling.
- Stable collapsible process hierarchy and process actions.
- Firefox titles are placed beneath inferred web-content processes when a foreground activity sample is confident enough; unmatched titles remain in an explicit unresolved section.
- Per-tab confidence, observation age, recent process drift, and stale/uncertain yellow warnings make the probabilistic mapping visible rather than pretending it is exact.
- Firefox process rows retain their own expanded/collapsed state, and tab titles use a full-width first line with an indented age/status line.
- Estimated Windows/cache/kernel/shared-memory reconciliation group.
- Hierarchy-aware row widths keep memory figures aligned at a common right edge and clear of the scrollbar.
- Dark process context menus and scrollbar.

Screenshots:

- [Overall accounting 0.4.0](docs/screenshots/v0.4.0/detailed.png)
- [Firefox processes 0.4.0](docs/screenshots/v0.4.0/firefox-processes.png)
- [Firefox tab titles 0.4.0](docs/screenshots/v0.4.0/firefox-tabs.png)
- [Minimal 0.4.0](docs/screenshots/v0.4.0/minimal.png)

## Next state — 0.5.0

- Observe real browsing over a longer session and tune the inference threshold from captured confidence/drift behavior.
- Consider an optional Firefox extension/native-messaging companion for stable tab IDs and live title events; it still would not expose an authoritative Firefox content-process PID.
- Add direct tab activation/closing only if the optional companion is introduced.
- Consider persisting recent inferred associations across app restarts with a short expiry.

## Iterations

### 0.4.0 — Per-process Firefox tabs and visual history

Last state: Firefox tab titles were segregated in one section, nested row widths drifted toward the scrollbar, and no durable screenshot/commit workflow existed.

Result: confident title matches now appear beneath their inferred Firefox web-content PID, unresolved titles remain honest and visible, changed/stale mappings are yellow, title metadata is split onto an indented second line, memory values share a clean right edge, and each feature prompt now produces screenshots, progress notes, a release snapshot, and a Git commit.

Next state: validate inference during normal browsing and decide whether the extra certainty and tab controls of an optional companion extension are worth the installation cost.

### 0.3.0 — Firefox metadata and system-memory accounting

Last state: process roles existed, but Firefox titles were only available through `about:processes`; system memory outside app rows was unexplained.

Result: local Firefox recovery data supplies title/age snapshots; non-process RAM is grouped into an explained estimate; refresh and dark-theme details were refined.

Next state: associate titles with processes probabilistically without representing the result as authoritative.

### 0.2.0 — Process interaction pass

Last state: the compact view opened details, but title-bar dragging and clicking were not differentiated; refresh forced Firefox open.

Result: click toggles, movement drags, double-click focuses, context actions were added, and expansion state became persistent.

Next state: improve Firefox tab-level context and account for system memory.

### 0.1.0 — Initial application

Last state: earlier standalone memory dashboards and scripts existed, but no conditional translucent companion.

Result: Memory Bloom introduced threshold-driven visibility, intensity graphics/transparency, compact scaling, process investigation, tray access, and startup.

Screenshots:

- [Detailed 0.1.0](docs/screenshots/v0.1.0/detailed.png)
- [Minimal 0.1.0](docs/screenshots/v0.1.0/minimal.png)

Next state: improve direct manipulation and Firefox process descriptions.

