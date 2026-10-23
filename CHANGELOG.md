# Memory Bloom changelog

Dates are reconstructed from the conversation and build artifacts where available. Early version boundaries are approximate because Git tracking was requested after development had already begun.

## 0.9.0 — 2026-10-23

- Added four semantic pressure taglines—comfortable, rising, busy, and critical—while retaining continuous color, pulse, arc, and opacity interpolation.
- Added deterministic compact preview captures at 55%, 74%, 82%, and 92% pressure. The PNG alpha values preserve each tier's true resting transparency.
- Backfilled the tier captures into the 0.1.0 visual history, where pressure-proportional presentation originated, while documenting that the images were reconstructed with the current renderer.
- Added one staged low-pressure image beside the existing fully opaque 92% compact image on the public README.
- Sorted the aggregate **Tab hosts** section among Firefox's other child PIDs by its summed private memory; PIDs inside the section remain memory-descending.
- Moved **Unresolved tabs** to the top of the Tab hosts section as a temporary diagnostic-first layout.
- Added a blue `●` symbol and stronger title color to tab-host PIDs with one or more current inferred web-page links.

## Documentation audit split — 2026-10-23 14:26 America/Toronto

- Reordered the GitHub landing page to introduce the compact experience before the expanded investigator.
- Reduced the landing page to two distinct product images and removed repetitive Firefox-state screenshots from the portfolio narrative.
- Separated the concise public `README.md` from the exhaustive engineering audit in `docs/HISTORY.md`.
- Backfilled dedicated audit records for versions 0.1.0 through 0.8.0 with requests, bugs, challenges, decisions, commits, releases, limitations, and every retained screenshot link.
- Kept screenshot assets centralized under `docs/screenshots/<version>/` so documentation shares canonical images without duplicated files.

## 0.8.0 — 2026-10-23 14:17 America/Toronto

- Added the optional **Memory Bloom Companion** Firefox extension, a local native-messaging bridge, and a Windows host installer. The companion supplies stable tab IDs plus the current title, URL, window, last-access time, active/hidden/discarded state, and event timing without making network requests.
- Used Firefox activation, loading/reload, title, URL, move, create, close, replace, discard, and window-focus events to trigger prompt tab-catalog updates and a four-second PID-correlation window—even for a background reload.
- Preserved the honest boundary: Firefox's Tabs API does not expose OS content-process PIDs, so tab identity is authoritative with the companion while PID ownership remains confidence-scored inference.
- Increased the detailed Firefox sampler from 500 ms to 250 ms to reduce missed short process bursts.
- Retained expired tab/PID links under their last live process as explicitly yellow **historical** evidence instead of making them disappear into Unresolved after two minutes.
- Kept the two-minute current-evidence horizon so a stale placement cannot masquerade as current; historical rows remain while both the open tab and candidate PID exist.
- Deduplicated recovery/live rows with the same normalized title when no stable extension tab ID is available, fixing the duplicate 41% row shown in the supplied report. The companion preserves genuinely separate same-title tabs because their IDs differ.
- Changed the Tab hosts summary to distinguish current links, historical links, and whether its tab catalog is from the live companion or Firefox's delayed recovery file.

The first supplied screenshot is the Firefox tab dropdown ground truth. The second shows Bloom seeing only two duplicate links among thirteen candidates after the user refreshed many tabs. Together they document why recovery-file titles and foreground-window sampling alone cannot provide consistent awareness.

![Reported Firefox open-tab ground truth before 0.8.0](docs/screenshots/v0.8.0/reported-firefox-open-tabs.png)

![Reported missed and duplicated links before 0.8.0](docs/screenshots/v0.8.0/reported-missed-and-duplicate-links.png)

## 0.7.0 — 2026-10-23 14:00 America/Toronto

- Added a passive `--show-passive` startup path and `tools/restart-app.ps1`, which can deploy a replacement executable without taking lasting focus or leaving Memory Bloom flashing in the taskbar.
- Made compact-to-detailed expansion monitor-aware: compact views on the right half grow leftward, views on the left half grow rightward, and collapsing restores the original compact anchor.
- Standardized the user-facing name of the smaller mode as **Compact**.
- Removed the independent DWM corner preference and DPI-scaled the native window region to the same 22-DIP radius as the panel, eliminating the visible double curve.
- Tracked live Firefox titles per top-level window rather than retaining every title change for 12 hours.
- Suppressed a superseded live title until a newer Firefox recovery snapshot can establish whether the event was same-tab navigation or a real tab switch.
- Normalized optional `— ChatGPT` suffixes so Firefox and live-title variants of the same page merge.
- Aged displayed association confidence and expired PID placement after two minutes without fresh evidence; expired matches move to **Unresolved tabs** with the former PID and age stated explicitly.
- Fixed Firefox restart clearing when the sampler observes an interval with no main browser process.
- Enforced private-memory descending order for child process rows and Firefox tab hosts, providing the same default for future browser-specific sections.

The first supplied image documents the competing corner radii; the second documents historical ChatGPT titles and stale high-confidence PID placement that were incorrectly presented as current.

![Reported double-corner geometry before 0.7.0](docs/screenshots/v0.7.0/reported-before-double-corners.png)

![Reported stale and duplicated Firefox links before 0.7.0](docs/screenshots/v0.7.0/reported-before-stale-tab-links.png)

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

