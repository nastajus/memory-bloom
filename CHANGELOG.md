# Memory Bloom changelog

Dates are reconstructed from the conversation and build artifacts where available. Early version boundaries are approximate because Git tracking was requested after development had already begun.

## 0.6.0 — 2026-10-23 13:38 America/Toronto

- Reworded Firefox `tab` processes as pooled candidate hosts; they no longer imply that each PID contains one or more live tabs.
- Displayed observed tab-link counts separately and changed empty candidates to **No tab observed**, covering spare, pooled, and indistinguishably quiet processes without guessing.
- Guarded the sampler's zero-evidence path so a completely quiet foreground page remains unresolved without faulting the refresh loop.
- Included Firefox built-in-page processes in the activity sampler so New Tab/settings pages are no longer excluded by role.
- Cleared accumulated title/PID evidence when Firefox's browser PID changes and ignored recovery snapshots that genuinely predate the current browser session.
- Normalized a bare Firefox window title to **New Tab** for live observation.
- Made the whole window fully opaque while hovered, restoring its pressure-based resting opacity when the pointer leaves.
- Expanded and wrapped dark tooltips so long memory and Firefox explanations remain fully readable.
- Applied a native rounded window region so transparent desktop shows through the four corners while retaining native resizing and Snap.
- Added a preview-only single-instance exception so visual regression captures can run beside the installed monitor.
- Backfilled one linked visual archive covering every retained historical screenshot.

The following user-supplied screenshot is preserved as diagnostic context: it shows the 0.5.0 state shortly after a Firefox restart, where five pooled content processes were misleadingly described as holding tabs while no title links had been inferred.

![Reported Firefox restart state before 0.6.0](docs/screenshots/v0.6.0/reported-before-firefox-restart.png)

## 0.5.0 — 2026-10-23 10:44 America/Toronto

- Replaced coordinate-based dragging with Windows' native non-client move loop and restored standard edge Snap/maximize previews.
- Enabled native resize/maximize window styles and made Memory Bloom visible in Alt+Tab.
- Added a generated Memory Bloom application/window/tray icon and embedded multi-resolution `.ico` asset.
- Added a desktop-shortcut installer and integrated it with sign-in setup.
- Pinned the Memory Bloom window across virtual desktops through the Windows shell's own pin-view service, with safe best-effort failure handling.
- Added immediate live foreground-title observation for Firefox rather than waiting for its recovery snapshot.
- Added a detailed-view-only 500 ms Firefox sampler with decaying multi-sample CPU/working-set evidence for much faster active-tab inference.
- Made title-only live observations merge into later recovery entries and retain last-interacted timing.
- Restored stepped/jagged memory-value indentation for parent and child rows.
- Rebuilt the GitHub README as a screenshot-led project landing page.

## 0.4.0 — 2026-10-23 10:21 America/Toronto

- Nested known Firefox titles beneath the inferred web-content process instead of presenting all known tabs as a separate peer list.
- Added conservative foreground activity inference using title matching plus short CPU and working-set deltas.
- Kept tabs without a defensible match in an explicit **Unresolved tabs** subsection.
- Added association confidence and observation age, recent PID drift detection, and yellow styling for stale, uncertain, or recently moved mappings.
- Preserved the expanded/collapsed state of each Firefox content-process row across refreshes.
- Gave web titles a full-width line and moved last-accessed/match details to an indented second line.
- Aligned group, process, and Windows/cache/shared memory values to one right edge while keeping them clear of the scrollbar.
- Added versioned screenshots, executable snapshots, a reconstructed Git history, and a project-local per-prompt documentation/autocommit workflow.

## 0.3.0 — 2026-10-23 10:04 America/Toronto

- Added Firefox tab titles and approximate last-accessed ages from Firefox's local recovery snapshot—no extension required.
- Added snapshot-age reporting and active-title mismatch detection.
- Grouped Firefox roles into main browser, web content, graphics, networking, media decoding, extensions, internal pages, and utilities.
- Added the estimated **Windows, cache & shared** physical-memory gap with file-cache, kernel-pool, and shared/OS subitems and explanatory tooltips.
- Made process refresh status discreet and non-blocking.
- Added dark context menus and a visible dark scrollbar.
- Made right-click select its process row before showing actions.
- Preserved collapsed/expanded state across refreshes.

## 0.2.0 — 2026-10-23 09:50 America/Toronto

- Distinguished click from drag on the compact surface and detailed title bar.
- Made a stationary title-bar click toggle minimal/detailed modes.
- Added process double-click to bring its window forward.
- Added right-click **Bring to front** and **End process…** actions.
- Improved Firefox process-role descriptions and surfaced the active Firefox title.
- Fixed Firefox reopening itself after the user collapsed its tree.

## 0.1.0 — 2026-10-23 09:41 America/Toronto

- Created the native WPF memory-pressure utility.
- Added automatic threshold appearance and hysteresis-based hiding.
- Linked color, glow, gauge fill, and whole-window opacity to pressure intensity.
- Added minimal and detailed modes, compact zoom, grouped processes, focus/kill actions, tray controls, and `Ctrl+Alt+M`.
- Added current-user sign-in startup and the direct Firefox `about:processes` shortcut.

