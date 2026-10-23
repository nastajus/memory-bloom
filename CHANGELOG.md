# Memory Bloom changelog

Concise user-visible release notes. The engineering audit is kept separately in [`docs/HISTORY.md`](docs/HISTORY.md).

Dates for early releases are reconstructed from the conversation and build artifacts because Git tracking began after initial development.

## 0.11.0 — 2026-10-23

- Added the shared dark **Exit Memory Bloom** menu to the detailed title bar as well as compact mode.
- Made **Windows, cache & shared** re-sort dynamically among root application groups by its current aggregate memory estimate.
- Documented temporary Firefox companion loading, unlisted Mozilla signing, expected effort, and review-delay tradeoffs.

## 0.10.0 — 2026-10-23

- Added a dark right-click menu to the compact view with **Exit Memory Bloom**.
- Clarified the documentation structure: README for the product overview, CHANGELOG for release outcomes, and HISTORY for the linked engineering audit.
- Removed development wording and timezone suffixes from public-facing headings.

## 0.9.0 — 2026-10-23

- Added comfortable, rising, busy, and critical pressure language with matching transparent tier captures.
- Sorted **Tab hosts** among Firefox child processes by memory and placed **Unresolved tabs** first for diagnosis.
- Added a blue `●` cue to Firefox host PIDs with current inferred page links.

## 0.8.0 — 2026-10-23

- Added the optional local Firefox companion for stable tab IDs, titles, URLs, last-access times, and lifecycle events.
- Increased activity sampling to 250 ms and retained expired PID links as clearly labeled historical evidence.
- Deduplicated fallback recovery/live rows while preserving genuinely separate same-title companion tabs.

## 0.7.0 — 2026-10-23

- Made compact-to-detailed expansion grow inward from the nearest monitor edge.
- Unified native and panel corner geometry and added passive development restarts that restore prior focus.
- Expired current Firefox PID placement after two minutes and prevented same-window title changes from accumulating as synthetic tabs.
- Sorted app child processes by memory.

## 0.6.0 — 2026-10-23

- Reworded Firefox content processes as candidate hosts rather than claiming each contains a tab.
- Cleared stale Firefox session evidence after restart and included built-in page processes in sampling.
- Made the window fully opaque on hover, widened tooltips, and removed opaque square corners.

## 0.5.0 — 2026-10-23

- Restored standard Windows dragging, resizing, Snap feedback, Alt+Tab presence, and virtual-desktop pinning.
- Added the Memory Bloom icon, desktop-shortcut installer, and faster foreground Firefox activity sampling.
- Restored hierarchical indentation for memory values.

## 0.4.0 — 2026-10-23

- Nested known Firefox titles beneath inferred content processes and kept unmatched titles in **Unresolved tabs**.
- Added confidence, observation age, drift warnings, and persistent collapsed-tree state.
- Aligned memory values and introduced versioned screenshots, releases, and Git history.

## 0.3.0 — 2026-10-23

- Added Firefox recovery-snapshot titles, last-access ages, and process-role descriptions.
- Added estimated Windows/cache/kernel/shared-memory reconciliation with explanatory tooltips.
- Added dark context menus and scrollbar plus a discreet refresh indicator.

## 0.2.0 — 2026-10-23

- Distinguished clicks from native dragging and made title-bar clicks switch modes.
- Added process double-click focus and right-click focus/end actions.
- Preserved collapsed Firefox trees across refreshes.

## 0.1.0 — 2026-10-23

- Created the conditional Windows memory-pressure companion with proportional color, glow, gauge, and opacity.
- Added compact and detailed modes, compact zoom, process investigation, tray controls, and `Ctrl+Alt+M`.
- Added sign-in startup and a direct Firefox `about:processes` shortcut.
