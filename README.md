# Memory Bloom

A quiet Windows memory-pressure companion. It runs in the notification area, stays invisible while memory is comfortable, and blooms into a translucent signal when pressure becomes important.

## Everyday use

- The compact view appears at 78% pressure by default and fades away below 72%.
- Its color, glow, and window opacity strengthen as pressure rises.
- Click the compact view—or the detailed title bar—to toggle modes. Drag either surface to move the window.
- Select an individual process to bring its main window forward or end it (with confirmation).
- Double-click a process to bring its window forward; right-click it for process actions.
- Right-clicking selects that process first. Context menus and scrollbars use the app's dark theme.
- Expand **Windows, cache & shared** for the estimated physical-memory gap outside summed process working sets, with explanations for file cache, kernel pools, and shared/OS memory.
- Expand **Firefox → Web content** to inspect each Firefox content process. Titles with a sufficiently strong activity match appear beneath the inferred PID; other titles remain in **Unresolved tabs** rather than being guessed.
- Yellow tab text means the match is stale, low-confidence, or was recently observed moving to another PID. Hover for the URL, confidence, and observation timestamps.
- Use **Firefox tabs ↗** to open Firefox's authoritative `about:processes` view.
- Use **+ / −** or Ctrl + mouse wheel to change the compact scale.
- Double-click the tray icon or press **Ctrl+Alt+M** to show the app at any time.
- Right-click the tray icon for Details, Hide, or Exit.

## Build and startup

```powershell
.\build.ps1
.\install-startup.ps1 -LaunchNow
```

Startup is installed only for the current Windows user and needs no administrator rights. Settings are saved under `%LOCALAPPDATA%\MemoryBloom`.

## Firefox limits

Windows can report Firefox's process tree and each process's memory, and Memory Bloom labels browser, tab-content, GPU, socket, media-decoder, extension, built-in-page, and utility roles where Firefox exposes them. The main browser row also shows Firefox's active window/tab title.

Expand Firefox → **Web content** for titles and approximate time since each tab was last accessed. Memory Bloom reads Firefox's local recovery snapshot without installing an extension. While Firefox is foreground, it conservatively combines the active window title with short CPU and working-set changes to infer a likely content PID. Each content-process row is independently collapsible and stays that way across refreshes.

Firefox intentionally does not put the actual tab URL/title on every Windows child process. `about:processes` is therefore the accurate built-in per-tab view and is opened directly from the app. A match can become outdated when Firefox combines or moves tabs between content processes; Memory Bloom marks old/uncertain/recently changed observations yellow and cannot detect a silent move until later activity supplies another sample. An optional future companion extension would provide stable tab IDs, event-driven title/activity updates, and tab actions, but Firefox's supported extension API still would not provide an authoritative content-process PID.
