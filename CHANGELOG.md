# Memory Bloom changelog

Dates are reconstructed from the conversation and build artifacts where available. Early version boundaries are approximate because Git tracking was requested after development had already begun.

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

