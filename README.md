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
- Use **Firefox tabs ↗** to open Firefox's `about:processes`, where Firefox can associate memory and CPU with specific tabs.
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

Expand Firefox → **Web content** → **Known tabs** for titles and approximate time since each tab was last accessed. Memory Bloom reads Firefox's local recovery snapshot without installing an extension. The subsection reports the snapshot age and flags when its titles do not include Firefox's currently active title.

Firefox intentionally does not put the actual tab URL/title on every Windows child process. `about:processes` is therefore the accurate built-in per-tab view and is opened directly from the app. Titles remain separate from PIDs because Firefox may combine or move tabs between content processes. A future companion extension would make title and activity updates event-driven and enable tab actions, but it still would not create a guaranteed one-to-one PID mapping.
