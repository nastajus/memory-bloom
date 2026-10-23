# Memory Bloom progress record

This is the human-readable design trail. Every feature or design prompt should add a dated iteration, save new screenshots under `docs/screenshots/<version>/`, update **Current state** and **Next state**, and end with a Git commit.

## Current state — 0.6.0

- Conditional pressure widget with proportional opacity/color intensity; hovering anywhere over the window temporarily makes it fully opaque.
- Minimal and detailed modes with click-versus-drag handling, native Windows move/resize styles, and standard edge Snap/maximize previews.
- Branded executable/window/tray icon, Alt+Tab presence, desktop shortcut, and best-effort Windows shell pinning across virtual desktops.
- Stable collapsible process hierarchy and process actions.
- Firefox's active title is observed live and a 500 ms lightweight sampler accumulates/decays process activity evidence, reducing typical active-tab matching from tens of seconds to the first distinguishable burst.
- Firefox rows now distinguish candidate tab-host processes from observed tab links. Pooled processes are no longer described as proof of one or more tabs, and built-in Firefox page processes participate in matching.
- Firefox titles are placed beneath inferred tab-host processes when foreground activity separates one candidate; unmatched titles remain in an explicit unresolved section.
- Restart detection clears old activity evidence, and a recovery snapshot older than the current Firefox session is suppressed instead of resurfacing closed pre-restart titles.
- Per-tab confidence, observation age, recent process drift, and stale/uncertain yellow warnings make the probabilistic mapping visible rather than pretending it is exact.
- Firefox process rows retain their own expanded/collapsed state, and tab titles use a full-width first line with an indented age/status line.
- Estimated Windows/cache/kernel/shared-memory reconciliation group.
- Memory figures again step right with parent/child hierarchy while remaining clear of the scrollbar.
- Dark process context menus and scrollbar, with wider wrapping tooltips that reveal the complete explanation.
- Native rounded window regions remove the opaque square corner pixels while retaining Windows resize/Snap behavior.

Screenshots:

- [Overall accounting 0.6.0](docs/screenshots/v0.6.0/detailed.png)
- [Firefox candidate hosts 0.6.0](docs/screenshots/v0.6.0/firefox-processes.png)
- [Firefox observed/unresolved titles 0.6.0](docs/screenshots/v0.6.0/firefox-tabs.png)
- [Minimal 0.6.0](docs/screenshots/v0.6.0/minimal.png)

## Next state — 0.7.0

- Observe the 500 ms evidence sampler during normal browsing and tune its separation threshold from real ambiguous/quiet-tab cases.
- Consider an optional signed Firefox extension/native-messaging companion for stable tab IDs, event-driven background titles, and tab actions; it still would not expose an authoritative Firefox content-process PID.
- Add direct tab activation/closing only if the optional companion is introduced.
- Consider persisting recent inferred associations across app restarts with a short expiry.

## Iterations

### 0.6.0 — Honest Firefox host status and visual polish

Last state: Firefox's pooled `tab` processes were described as “one or more tabs,” stale titles could remain after a browser restart, tooltips could run out of their narrow surface, opacity did not react to hover, and the rounded inner panel exposed opaque square window corners.

Reported context: this user-supplied 0.5.0 screenshot was taken after Firefox had been restarted with a much smaller live session. It records the misleading five-process/zero-match wording and the lingering unresolved snapshot that motivated this pass; it is diagnostic evidence, not a release beauty shot.

![Reported Firefox restart state before 0.6.0](docs/screenshots/v0.6.0/reported-before-firefox-restart.png)

Result: candidate Firefox tab-host processes are now explicitly separated from observed tab links, built-in Firefox pages can be sampled, restart/session timestamps discard genuinely pre-restart data, and idle/spare candidates say “no tab observed” instead of claiming ownership. Hover raises the window to full opacity, wrapped tooltips show their full text, and a native rounded region removes the square outer corners without giving up normal Windows Snap.

Next state: evaluate the new wording and matching during ordinary browsing, then decide whether an optional extension is worth adding for event-driven tab identity and actions.

### 0.5.0 — Native Windows integration and faster Firefox evidence

Last state: custom coordinate dragging bypassed Windows Snap, the app was absent from Alt+Tab and only lived on one virtual desktop, active Firefox inference sampled only during the six-second heavy refresh, and all memory values ended on a single aligned edge.

Result: the Windows non-client move loop now supplies standard Snap/maximize feedback; native resize/maximize styles are verified; the branded app is visible in Alt+Tab, pinned across virtual desktops, and installable from a desktop shortcut. Live Firefox titles appear without waiting for the recovery file, and a 500 ms rolling activity sampler can associate an active title after the first clear burst. Hierarchical memory indentation is restored.

Next state: measure ambiguous/static Firefox pages in daily use and decide whether an optional signed companion is worth maintaining for stable tab IDs and direct tab controls.

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

## Visual archive

Every generated checkpoint below is retained and linked here. Version 0.2.0 predates the automated capture workflow and has a [reconstructed functional record](docs/history/v0.2.0.md) rather than screenshots.

- 0.1.0: [detailed](docs/screenshots/v0.1.0/detailed.png), [minimal](docs/screenshots/v0.1.0/minimal.png)
- 0.3.0: [detailed](docs/screenshots/v0.3.0/detailed.png), [minimal](docs/screenshots/v0.3.0/minimal.png)
- 0.4.0: [detailed](docs/screenshots/v0.4.0/detailed.png), [Firefox processes](docs/screenshots/v0.4.0/firefox-processes.png), [Firefox tabs](docs/screenshots/v0.4.0/firefox-tabs.png), [minimal](docs/screenshots/v0.4.0/minimal.png)
- 0.5.0: [detailed](docs/screenshots/v0.5.0/detailed.png), [Firefox processes](docs/screenshots/v0.5.0/firefox-processes.png), [Firefox tabs](docs/screenshots/v0.5.0/firefox-tabs.png), [minimal](docs/screenshots/v0.5.0/minimal.png), [icon](docs/screenshots/v0.5.0/icon.png)
- 0.6.0: [reported context](docs/screenshots/v0.6.0/reported-before-firefox-restart.png), [detailed](docs/screenshots/v0.6.0/detailed.png), [Firefox candidate hosts](docs/screenshots/v0.6.0/firefox-processes.png), [Firefox tabs](docs/screenshots/v0.6.0/firefox-tabs.png), [minimal](docs/screenshots/v0.6.0/minimal.png)

