# Memory Bloom progress record

This is the human-readable design trail. Every feature or design prompt should add a dated iteration, save new screenshots under `docs/screenshots/<version>/`, update **Current state** and **Next state**, and end with a Git commit.

## Current state — 0.3.0

- Conditional pressure widget with proportional opacity/color intensity.
- Minimal and detailed modes with click-versus-drag handling.
- Stable collapsible process hierarchy and process actions.
- Firefox process roles plus a separate local-session tab-title snapshot.
- Estimated Windows/cache/kernel/shared-memory reconciliation group.
- Dark process context menus and scrollbar.

Screenshots:

- [Detailed 0.3.0](docs/screenshots/v0.3.0/detailed.png)
- [Minimal 0.3.0](docs/screenshots/v0.3.0/minimal.png)

## Next state — 0.4.0

- Place inferred tab associations beneath individual Firefox web-content processes.
- Keep unresolved titles explicit instead of forcing an assignment.
- Add confidence, observation age, drift detection, and yellow stale/uncertain styling.
- Improve deep-row title/age indentation and memory-column alignment.
- Save a fresh before/after visual record and executable snapshot.

## Iterations

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

