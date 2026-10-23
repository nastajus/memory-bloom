using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;

namespace MemoryBloom;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xB100;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkM = 0x4D;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wordParameter, IntPtr longParameter);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint WmNcLButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new(2);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _firefoxTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon _tray;
    private readonly Dictionary<string, bool> _groupExpansionState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, bool> _processExpansionState = new();
    private readonly Dictionary<int, double> _previousCpuByPid = new();
    private readonly Dictionary<int, long> _previousWorkingSetByPid = new();
    private readonly Dictionary<string, TabProcessAssociation> _tabAssociations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, double>> _tabActivityEvidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _liveTabAccess = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _liveTabTitles = new(StringComparer.Ordinal);
    private readonly HashSet<int> _latestFirefoxTabHostPids = new();
    private List<ProcessGroup> _latestGroups = new();
    private FirefoxTabsSnapshot _firefoxSessionTabs = new();
    private FirefoxTabsSnapshot _latestFirefoxTabs = new();
    private string _lastForegroundFirefoxTitle = "";
    private int _firefoxBrowserPid;
    private double _restingOpacity = 0.98;
    private bool _virtualDesktopPinned;
    private bool _tabsSectionExpanded;
    private bool _firefoxContentExpanded;
    private MemoryReading? _reading;
    private ProcessSnapshot? _selectedProcess;
    private bool _detailed;
    private bool _manualOpen;
    private bool _exiting;
    private bool _processRefreshRunning;
    private int _quietTicks;
    private int _tick;
    private bool _ready;
    private bool _surfacePointerDown;
    private bool _surfaceDragging;
    private Point _pointerStartScreen;

    public MainWindow()
    {
        InitializeComponent();
        Opacity = 0;
        ThresholdSlider.Value = _settings.ShowAtPercent;
        ThresholdText.Text = $"{_settings.ShowAtPercent:0}%";

        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Memory Bloom — pressure monitor",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDetails);

        _timer.Tick += async (_, _) => await SampleAsync();
        _firefoxTimer.Tick += (_, _) => SampleFirefoxActivity();
        SourceInitialized += async (_, _) =>
        {
            RegisterShortcut();
            ApplyNativeWindowShape();
            await TryPinToAllDesktopsAsync();
        };
        SizeChanged += (_, _) => ApplyNativeWindowShape();
        StateChanged += (_, _) => ApplyNativeWindowShape();
        MouseEnter += (_, _) => Opacity = 1;
        MouseLeave += (_, _) => Opacity = _restingOpacity;
        Closed += (_, _) => Cleanup();
        Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideUntilPressure();
        };
        MouseWheel += MainWindow_MouseWheel;
    }

    public void InitializeInBackground(bool showNow, string? previewFolder = null)
    {
        if (previewFolder is not null)
            Loaded += async (_, _) => await CapturePreviewsAsync(previewFolder);
        Show();
        if (showNow) ShowDetails();
        else Hide();
        _ready = true;
        _timer.Start();
        _firefoxTimer.Start();
        _ = SampleAsync();
    }

    private async Task SampleAsync()
    {
        try
        {
            _reading = MemoryProbe.ReadMemory();
            UpdateMemoryVisuals(_reading);
            EvaluateVisibility(_reading.PressurePercent);
            _tick++;
            if (_detailed && (_tick % 3 == 0 || ProcessTree.Items.Count == 0))
                await RefreshProcessesAsync();
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Memory reading unavailable";
            LoadingText.Text = ex.Message;
        }
    }

    private void EvaluateVisibility(double pressure)
    {
        if (_manualOpen || _detailed) return;

        if (pressure >= _settings.ShowAtPercent)
        {
            _quietTicks = 0;
            if (!IsVisible)
            {
                SetMinimalSize();
                PositionAtEdgeIfNeeded();
                Show();
            }
        }
        else if (pressure < _settings.HideBelowPercent)
        {
            _quietTicks++;
            if (_quietTicks >= 5 && IsVisible) Hide();
        }
        else
        {
            _quietTicks = 0;
        }
    }

    private void UpdateMemoryVisuals(MemoryReading reading)
    {
        var pressure = Math.Clamp(reading.PressurePercent, 0, 100);
        var intensity = Math.Clamp((pressure - _settings.HideBelowPercent) / (100 - _settings.HideBelowPercent), 0, 1);
        var color = Blend(Color.FromRgb(93, 207, 166), Color.FromRgb(255, 104, 92), intensity);
        var brush = new SolidColorBrush(color);

        PressureArc.Data = MakeArc(pressure / 100d);
        PressureArc.Stroke = brush;
        Pulse.Fill = new SolidColorBrush(Color.FromArgb((byte)(44 + 115 * intensity), color.R, color.G, color.B));
        Pulse.Width = Pulse.Height = 30 + 12 * intensity;
        HeaderDot.Fill = brush;
        PercentText.Text = $"{pressure:0}";
        DetailPercent.Text = $"{pressure:0}%";
        SummaryText.Text = $"{FormatBytes(reading.PhysicalUsedBytes)} of {FormatBytes(reading.PhysicalTotalBytes)} in use";
        PhysicalText.Text = $"Physical   {reading.PhysicalUsedPercent:0}%   {FormatBytes(reading.PhysicalUsedBytes)} / {FormatBytes(reading.PhysicalTotalBytes)}";
        CommitText.Text = $"Committed   {reading.CommitPercent:0}%   {FormatBytes(reading.CommitUsedBytes)} / {FormatBytes(reading.CommitLimitBytes)}";

        _restingOpacity = _detailed ? 0.98 : 0.40 + intensity * 0.58;
        Opacity = IsMouseOver ? 1 : _restingOpacity;
        Shell.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(70 + 110 * intensity), color.R, color.G, color.B));
        _tray.Text = $"Memory Bloom — {pressure:0}% pressure";
    }

    private async Task RefreshProcessesAsync()
    {
        if (_processRefreshRunning) return;
        _processRefreshRunning = true;
        LoadingText.Text = "Refreshing…";
        LoadingText.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() => (Rows: MemoryProbe.ReadProcesses(), Tabs: MemoryProbe.ReadFirefoxTabs()));
            var rows = result.Rows;
            var firefoxMain = rows
                .Where(process => process.DisplayName.Equals("Firefox", StringComparison.OrdinalIgnoreCase) && !process.CommandLine.Contains("-contentproc", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(process => process.StartTimeUtc)
                .FirstOrDefault();
            ResetFirefoxObservationsForNewSession(firefoxMain?.Pid ?? 0);
            _firefoxSessionTabs = IgnorePreRestartFirefoxSnapshot(result.Tabs, firefoxMain?.StartTimeUtc ?? default);
            var firefoxTabHosts = rows
                .Where(process => process.DisplayName.Equals("Firefox", StringComparison.OrdinalIgnoreCase) && IsFirefoxTabHost(process))
                .ToList();
            _latestFirefoxTabHostPids.Clear();
            foreach (var process in firefoxTabHosts) _latestFirefoxTabHostPids.Add(process.Pid);
            var foreground = GetForegroundFirefoxTitle();
            ObserveLiveFirefoxTab(foreground.IsForeground, foreground.Title);
            var mergedTabs = MergeFirefoxTabs(_firefoxSessionTabs);
            UpdateFirefoxAssociations(
                firefoxTabHosts.Select(process => new ProcessActivity(process.Pid, process.CpuTotalMilliseconds, process.WorkingSetBytes)).ToList(),
                mergedTabs,
                foreground.IsForeground,
                foreground.Title);
            var groups = rows
                .Where(p => p.PrivateBytes > 0 && p.Pid != Environment.ProcessId)
                .GroupBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcessGroup { Name = g.Key, Processes = g.OrderByDescending(p => p.PrivateBytes).ToList() })
                .OrderByDescending(g => g.TotalPrivateBytes)
                .Take(30)
                .ToList();
            if (_reading is not null)
            {
                groups.Add(BuildSystemMemoryGroup(rows, _reading));
                groups = groups.OrderByDescending(group => group.TotalPrivateBytes).ToList();
            }
            _latestGroups = groups;
            _latestFirefoxTabs = mergedTabs;
            PopulateTree(_latestGroups, _latestFirefoxTabs);
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Could not read processes: {ex.Message}";
            LoadingText.Visibility = Visibility.Visible;
        }
        finally { _processRefreshRunning = false; }
    }

    private static ProcessGroup BuildSystemMemoryGroup(List<ProcessSnapshot> rows, MemoryReading reading)
    {
        var summedWorkingSets = (ulong)Math.Max(0, rows.Sum(process => (double)Math.Max(0, process.WorkingSetBytes)));
        var gap = reading.PhysicalUsedBytes > summedWorkingSets ? reading.PhysicalUsedBytes - summedWorkingSets : 0;
        var remaining = gap;

        ulong Take(ulong requested)
        {
            var taken = Math.Min(requested, remaining);
            remaining -= taken;
            return taken;
        }

        var cache = Take(reading.SystemCacheBytes);
        var nonPaged = Take(reading.KernelNonPagedBytes);
        var paged = Take(reading.KernelPagedBytes);
        var items = new List<ProcessSnapshot>
        {
            new() { Name = "System file cache", DisplayName = "System file cache", PrivateBytes = (long)cache, Role = "recent file data retained for fast reuse", IsSynthetic = true,
                Tooltip = "Windows uses otherwise available RAM to cache file data. It is generally reclaimable quickly when applications need memory." },
            new() { Name = "Kernel nonpaged pool", DisplayName = "Kernel nonpaged pool", PrivateBytes = (long)nonPaged, Role = "drivers and kernel memory that must remain in RAM", IsSynthetic = true,
                Tooltip = "Memory used by Windows and device drivers that cannot be paged to disk. Unusual growth can indicate a driver leak." },
            new() { Name = "Kernel paged pool", DisplayName = "Kernel paged pool", PrivateBytes = (long)paged, Role = "pageable Windows kernel and driver memory", IsSynthetic = true,
                Tooltip = "Kernel and driver allocations that Windows may move to the page file when necessary." },
            new() { Name = "Other shared / OS", DisplayName = "Other shared / OS", PrivateBytes = (long)remaining, Role = "shared pages, mapped files, page tables and accounting remainder", IsSynthetic = true,
                Tooltip = "The remaining physical-memory gap after process working sets, file cache, and kernel pools. It includes shared mappings, page tables, locked pages, and measurement timing differences." }
        };
        return new ProcessGroup { Name = "Windows, cache & shared", Processes = items, IsSystemEstimate = true };
    }

    private void ResetFirefoxObservationsForNewSession(int browserPid)
    {
        if (_firefoxBrowserPid != 0 && browserPid != 0 && browserPid != _firefoxBrowserPid)
        {
            _previousCpuByPid.Clear();
            _previousWorkingSetByPid.Clear();
            _tabAssociations.Clear();
            _tabActivityEvidence.Clear();
            _liveTabAccess.Clear();
            _liveTabTitles.Clear();
            _lastForegroundFirefoxTitle = "";
        }
        _firefoxBrowserPid = browserPid;
    }

    private static FirefoxTabsSnapshot IgnorePreRestartFirefoxSnapshot(FirefoxTabsSnapshot snapshot, DateTime browserStartUtc)
    {
        if (browserStartUtc == default || snapshot.SourceTimeUtc == default || snapshot.SourceTimeUtc >= browserStartUtc - TimeSpan.FromSeconds(2))
            return snapshot;

        return new FirefoxTabsSnapshot
        {
            SourcePath = snapshot.SourcePath,
            SourceTimeUtc = snapshot.SourceTimeUtc,
            Error = "Waiting for Firefox's current-session snapshot; older pre-restart titles were ignored."
        };
    }

    private static bool IsFirefoxTabHost(ProcessSnapshot process) =>
        process.Role.StartsWith("web content", StringComparison.OrdinalIgnoreCase) ||
        process.Role.StartsWith("built-in Firefox pages", StringComparison.OrdinalIgnoreCase);

    private void SampleFirefoxActivity()
    {
        if (!_detailed || _latestFirefoxTabHostPids.Count == 0) return;
        var foreground = GetForegroundFirefoxTitle();
        var liveChanged = ObserveLiveFirefoxTab(foreground.IsForeground, foreground.Title);
        var mergedTabs = MergeFirefoxTabs(_firefoxSessionTabs);
        var associationChanged = UpdateFirefoxAssociations(
            MemoryProbe.ReadProcessActivity(_latestFirefoxTabHostPids),
            mergedTabs,
            foreground.IsForeground,
            foreground.Title);
        if ((liveChanged || associationChanged) && !_processRefreshRunning && _latestGroups.Count > 0)
        {
            _latestFirefoxTabs = mergedTabs;
            PopulateTree(_latestGroups, _latestFirefoxTabs);
        }
    }

    private (bool IsForeground, string Title) GetForegroundFirefoxTitle()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
            using var foreground = Process.GetProcessById((int)foregroundPid);
            if (!foreground.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return (false, "");
            return (true, CleanFirefoxTitle(foreground.MainWindowTitle));
        }
        catch
        {
            return (false, "");
        }
    }

    private bool ObserveLiveFirefoxTab(bool firefoxIsForeground, string activeTitle)
    {
        if (!firefoxIsForeground || string.IsNullOrWhiteSpace(activeTitle))
        {
            var changed = _lastForegroundFirefoxTitle.Length > 0;
            _lastForegroundFirefoxTitle = "";
            return changed;
        }

        var normalized = NormalizeTitle(activeTitle);
        var titleChanged = !_lastForegroundFirefoxTitle.Equals(normalized, StringComparison.Ordinal);
        if (titleChanged)
        {
            _liveTabAccess[normalized] = DateTime.UtcNow;
            _liveTabTitles[normalized] = activeTitle;
        }
        _lastForegroundFirefoxTitle = normalized;
        return titleChanged;
    }

    private FirefoxTabsSnapshot MergeFirefoxTabs(FirefoxTabsSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        foreach (var key in _liveTabAccess.Where(item => now - item.Value > TimeSpan.FromHours(12)).Select(item => item.Key).ToList())
        {
            _liveTabAccess.Remove(key);
            _liveTabTitles.Remove(key);
        }

        var tabs = snapshot.Tabs.ToList();
        foreach (var observed in _liveTabAccess)
        {
            var index = tabs.FindIndex(tab => NormalizeTitle(tab.Title).Equals(observed.Key, StringComparison.Ordinal));
            if (index >= 0)
            {
                var tab = tabs[index];
                tabs[index] = tab with { LastAccessedUtc = tab.LastAccessedUtc > observed.Value ? tab.LastAccessedUtc : observed.Value, IsLiveObservation = true };
            }
            else
            {
                tabs.Add(new FirefoxTabSnapshot(_liveTabTitles.GetValueOrDefault(observed.Key, observed.Key), "", observed.Value, false, true));
            }
        }

        return new FirefoxTabsSnapshot
        {
            Tabs = tabs.OrderByDescending(tab => tab.LastAccessedUtc).ToList(),
            SourcePath = snapshot.SourcePath,
            SourceTimeUtc = snapshot.SourceTimeUtc,
            Error = snapshot.Error
        };
    }

    private bool UpdateFirefoxAssociations(
        IReadOnlyList<ProcessActivity> content,
        FirefoxTabsSnapshot tabsSnapshot,
        bool firefoxIsForeground,
        string activeTitle)
    {
        var now = DateTime.UtcNow;
        var activeTab = tabsSnapshot.Tabs.FirstOrDefault(tab => TitlesMatch(tab.Title, activeTitle));
        var renderChanged = false;

        if (firefoxIsForeground && activeTab is not null && content.Count > 0)
        {
            var scored = content.Select(process =>
            {
                var cpuDelta = _previousCpuByPid.TryGetValue(process.Pid, out var previousCpu)
                    ? Math.Max(0, process.CpuTotalMilliseconds - previousCpu) : 0;
                var workingSetDeltaMb = _previousWorkingSetByPid.TryGetValue(process.Pid, out var previousWorkingSet)
                    ? Math.Max(0, process.WorkingSetBytes - previousWorkingSet) / 1048576d : 0;
                return (Process: process, Score: cpuDelta + workingSetDeltaMb * 1.4);
            }).OrderByDescending(item => item.Score).ToList();

            var key = TabKey(activeTab);
            if (!_tabActivityEvidence.TryGetValue(key, out var evidence))
            {
                evidence = new Dictionary<int, double>();
                _tabActivityEvidence[key] = evidence;
            }
            foreach (var pid in evidence.Keys.ToList()) evidence[pid] *= 0.45;
            foreach (var item in scored) evidence[item.Process.Pid] = evidence.GetValueOrDefault(item.Process.Pid) + item.Score;
            foreach (var pid in evidence.Where(item => item.Value < 0.05 || content.All(process => process.Pid != item.Key)).Select(item => item.Key).ToList())
                evidence.Remove(pid);

            var rankedEvidence = evidence.OrderByDescending(item => item.Value).ToList();
            if (rankedEvidence.Count > 0)
            {
                var top = rankedEvidence[0];
                var secondScore = rankedEvidence.Count > 1 ? rankedEvidence[1].Value : 0;
                if (top.Value >= 2 && (secondScore == 0 || top.Value >= secondScore * 1.2 || top.Value - secondScore >= 3))
                {
                    var separation = 1 - Math.Min(1, secondScore / Math.Max(1, top.Value));
                    var confidence = Math.Clamp(0.52 + separation * 0.30 + Math.Min(0.14, top.Value / 350), 0.52, 0.96);
                    if (!_tabAssociations.TryGetValue(key, out var association))
                    {
                        association = new TabProcessAssociation { Key = key };
                        _tabAssociations[key] = association;
                        renderChanged = true;
                    }
                    if (association.Pid != 0 && association.Pid != top.Key)
                    {
                        association.PreviousPid = association.Pid;
                        association.DriftDetectedUtc = now;
                        renderChanged = true;
                    }
                    if (association.Pid != top.Key || Math.Abs(association.Confidence - confidence) >= 0.08 || now - association.ObservedUtc >= TimeSpan.FromSeconds(5))
                        renderChanged = true;
                    association.Pid = top.Key;
                    association.ObservedUtc = now;
                    association.Confidence = confidence;
                }
            }
        }

        foreach (var process in content)
        {
            _previousCpuByPid[process.Pid] = process.CpuTotalMilliseconds;
            _previousWorkingSetByPid[process.Pid] = process.WorkingSetBytes;
        }
        var livePids = content.Select(process => process.Pid).ToHashSet();
        foreach (var pid in _previousCpuByPid.Keys.Where(pid => !livePids.Contains(pid)).ToList())
        {
            _previousCpuByPid.Remove(pid);
            _previousWorkingSetByPid.Remove(pid);
        }
        var currentKeys = tabsSnapshot.Tabs.Select(TabKey).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _tabAssociations.Where(item => !currentKeys.Contains(item.Key) && now - item.Value.ObservedUtc > TimeSpan.FromHours(1)).Select(item => item.Key).ToList())
        {
            _tabAssociations.Remove(key);
            _tabActivityEvidence.Remove(key);
        }
        return renderChanged;
    }

    private void PopulateTree(List<ProcessGroup> groups, FirefoxTabsSnapshot firefoxTabs)
    {
        foreach (var item in ProcessTree.Items.OfType<TreeViewItem>().Where(item => item.Tag is ProcessGroup))
        {
            _groupExpansionState[((ProcessGroup)item.Tag).Name] = item.IsExpanded;
            foreach (var child in item.Items.OfType<TreeViewItem>())
            {
                if (child.Tag is FirefoxTabSection) _tabsSectionExpanded = child.IsExpanded;
                if (child.Tag is FirefoxContentSection)
                {
                    _firefoxContentExpanded = child.IsExpanded;
                    foreach (var grandchild in child.Items.OfType<TreeViewItem>())
                    {
                        if (grandchild.Tag is FirefoxTabSection) _tabsSectionExpanded = grandchild.IsExpanded;
                        if (grandchild.Tag is ProcessSnapshot process && !process.IsSynthetic) _processExpansionState[process.Pid] = grandchild.IsExpanded;
                    }
                }
            }
        }

        ProcessTree.Items.Clear();
        foreach (var group in groups)
        {
            var root = new TreeViewItem
            {
                Header = MakeRow(
                    group.Name,
                    group.IsSystemEstimate ? "estimated physical-memory gap" : $"{group.Processes.Count} process{(group.Processes.Count == 1 ? "" : "es")}",
                    group.IsSystemEstimate ? $"≈{FormatBytes(group.TotalPrivateBytes)}" : FormatBytes(group.TotalPrivateBytes),
                    group.IsFirefox,
                    group.IsSystemEstimate
                        ? "Estimated RAM not present in the summed process working sets below. Expand for cache, kernel pools, and the remaining shared/OS category. Process working sets can overlap, so this is a reconciliation estimate rather than exact ownership."
                        : "Total private committed memory for the processes in this app group.",
                    depth: 0),
                Tag = group,
                IsExpanded = _groupExpansionState.TryGetValue(group.Name, out var wasExpanded) ? wasExpanded : group.IsFirefox || group.IsSystemEstimate
            };
            if (group.IsFirefox)
            {
                var tabHosts = group.Processes.Where(IsFirefoxTabHost).ToList();
                foreach (var process in group.Processes.Except(tabHosts)) root.Items.Add(MakeProcessTreeItem(process, true, rowDepth: 1));
                var tabsByPid = firefoxTabs.Tabs
                    .Select(tab => (Tab: tab, Association: _tabAssociations.GetValueOrDefault(TabKey(tab))))
                    .Where(item => item.Association is not null && tabHosts.Any(process => process.Pid == item.Association.Pid))
                    .GroupBy(item => item.Association!.Pid)
                    .ToDictionary(grouped => grouped.Key, grouped => grouped.ToList());
                var observedLinks = tabsByPid.Sum(item => item.Value.Count);
                var contentSection = new TreeViewItem
                {
                    Header = MakeRow("Tab hosts", $"{tabHosts.Count} candidate processes · {observedLinks} observed link{(observedLinks == 1 ? "" : "s")}", FormatBytes(tabHosts.Sum(process => process.PrivateBytes)), true,
                        "These are Firefox processes capable of hosting site or built-in pages—not a count of open tabs. Firefox keeps a pool of content processes and may leave some without a currently observable tab. Links below are inferred from foreground-title changes and short activity bursts.",
                        depth: 1),
                    Tag = new FirefoxContentSection("Tab hosts"),
                    IsExpanded = _firefoxContentExpanded
                };
                foreach (var process in tabHosts)
                {
                    tabsByPid.TryGetValue(process.Pid, out var associatedTabs);
                    contentSection.Items.Add(MakeProcessTreeItem(process, true, associatedTabs, firefoxTabs, rowDepth: 2));
                }
                var unresolved = firefoxTabs.Tabs.Where(tab =>
                {
                    var association = _tabAssociations.GetValueOrDefault(TabKey(tab));
                    return association is null || tabHosts.All(process => process.Pid != association.Pid);
                }).ToList();
                contentSection.Items.Add(MakeUnresolvedTabsSection(group, firefoxTabs, unresolved));
                root.Items.Add(contentSection);
            }
            else
            {
                foreach (var process in group.Processes) root.Items.Add(MakeProcessTreeItem(process, false, rowDepth: 1));
            }
            ProcessTree.Items.Add(root);
        }
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private TreeViewItem MakeProcessTreeItem(
        ProcessSnapshot process,
        bool firefox,
        List<(FirefoxTabSnapshot Tab, TabProcessAssociation? Association)>? associatedTabs = null,
        FirefoxTabsSnapshot? snapshot = null,
        int rowDepth = 1)
    {
        var descriptor = process.Role;
        if (!string.IsNullOrWhiteSpace(process.WindowTitle) && process.Role == process.WindowTitle)
            descriptor = Trim(process.WindowTitle, 42);
        var tabCount = associatedTabs?.Count ?? 0;
        if (firefox && IsFirefoxTabHost(process))
            descriptor += tabCount == 0 ? " · no tab observed" : $" · {tabCount} observed tab link{(tabCount == 1 ? "" : "s")}";
        var child = new TreeViewItem
        {
            Header = MakeRow(
                process.IsSynthetic ? process.DisplayName : $"PID {process.Pid}",
                descriptor,
                FormatBytes(process.PrivateBytes),
                firefox,
                process.IsSynthetic
                    ? process.Tooltip
                    : $"Private committed memory: {FormatBytes(process.PrivateBytes)}. Current physical working set: {FormatBytes(process.WorkingSetBytes)}. {process.Role}",
                depth: rowDepth),
            Tag = process,
            ToolTip = process.Tooltip,
            IsExpanded = _processExpansionState.GetValueOrDefault(process.Pid)
        };
        if (!process.IsSynthetic) child.ContextMenu = MakeProcessMenu(process);
        if (firefox && IsFirefoxTabHost(process))
        {
            if (associatedTabs is null || associatedTabs.Count == 0)
            {
                child.Items.Add(new TreeViewItem
                {
                    Header = MakeRow("No tab observed for this process", "may be spare, pooled, or too quiet to distinguish", "", false,
                        "Firefox's process pool can contain content processes with no live tab. A process may also host an idle tab that produces no distinctive activity. Memory Bloom waits for a foreground activity burst instead of guessing.",
                        depth: rowDepth + 1),
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var item in associatedTabs.OrderByDescending(item => item.Tab.LastAccessedUtc))
                    child.Items.Add(MakeAssociatedTabItem(item.Tab, item.Association!, snapshot!));
            }
        }
        return child;
    }

    private TreeViewItem MakeUnresolvedTabsSection(ProcessGroup firefoxGroup, FirefoxTabsSnapshot snapshot, List<FirefoxTabSnapshot> unresolved)
    {
        var sourceAge = snapshot.SourceTimeUtc == default ? TimeSpan.Zero : DateTime.UtcNow - snapshot.SourceTimeUtc;
        var mainTitle = firefoxGroup.Processes
            .Where(process => !process.CommandLine.Contains("-contentproc", StringComparison.OrdinalIgnoreCase))
            .Select(process => System.Text.RegularExpressions.Regex.Replace(process.WindowTitle ?? "", @"\s*[—-]\s*Mozilla Firefox\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim())
            .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)) ?? "";
        var activeTitleMatches = string.IsNullOrWhiteSpace(mainTitle) || snapshot.Tabs.Any(tab =>
            tab.Title.Equals(mainTitle, StringComparison.OrdinalIgnoreCase) ||
            tab.Title.Contains(mainTitle, StringComparison.OrdinalIgnoreCase) ||
            mainTitle.Contains(tab.Title, StringComparison.OrdinalIgnoreCase));
        var freshness = snapshot.Error is not null
            ? "live observations only · session snapshot pending"
            : $"snapshot {FormatAge(sourceAge)} old{(activeTitleMatches ? "" : " · active title differs")}";
        var section = new TreeViewItem
        {
            Header = MakeRow("Unresolved tabs", freshness, $"{unresolved.Count}", true,
                "Tabs that have not yet produced a confident process-activity match, or whose last matched PID has disappeared. They remain unresolved rather than being assigned arbitrarily.",
                depth: 2),
            Tag = new FirefoxTabSection("Unresolved tabs"),
            IsExpanded = _tabsSectionExpanded
        };
        if (snapshot.Error is not null && unresolved.Count == 0)
        {
            section.Items.Add(new TreeViewItem { Header = MakeRow("Unavailable", snapshot.Error, "", false, snapshot.Error, depth: 3), IsEnabled = false });
            return section;
        }
        foreach (var tab in unresolved.Take(250))
        {
            var age = DateTime.UtcNow - tab.LastAccessedUtc;
            var subtitle = $"last used {FormatAge(age)} ago{(tab.Hidden ? " · hidden" : "")}";
            section.Items.Add(new TreeViewItem
            {
                Header = MakeTabRow(tab.Title, subtitle, $"{tab.Title}\n{tab.Url}\nLast interacted: {tab.LastAccessedUtc.ToLocalTime():g}\nNo current PID association.", false, depth: 3),
                Tag = tab
            });
        }
        if (unresolved.Count > 250)
            section.Items.Add(new TreeViewItem { Header = MakeTabRow("More tabs not shown", $"{unresolved.Count - 250} additional titles", "The display is capped at 250 titles to keep the process tree responsive.", false, depth: 3), IsEnabled = false });
        return section;
    }

    private TreeViewItem MakeAssociatedTabItem(FirefoxTabSnapshot tab, TabProcessAssociation association, FirefoxTabsSnapshot snapshot)
    {
        var observationAge = DateTime.UtcNow - association.ObservedUtc;
        var sourceAge = snapshot.SourceTimeUtc == default ? TimeSpan.MaxValue : DateTime.UtcNow - snapshot.SourceTimeUtc;
        var driftRecent = association.DriftDetectedUtc is not null && DateTime.UtcNow - association.DriftDetectedUtc < TimeSpan.FromMinutes(10);
        var warning = observationAge > TimeSpan.FromMinutes(5) || (!tab.IsLiveObservation && sourceAge > TimeSpan.FromMinutes(2)) || association.Confidence < 0.60 || driftRecent;
        var status = driftRecent
            ? $"moved from PID {association.PreviousPid} · seen {FormatAge(observationAge)} ago"
            : $"{(tab.IsLiveObservation ? "live · " : "")}match {association.Confidence:P0} · seen {FormatAge(observationAge)} ago · last used {FormatAge(DateTime.UtcNow - tab.LastAccessedUtc)} ago";
        return new TreeViewItem
        {
            Header = MakeTabRow(tab.Title, status,
                $"{tab.Title}\n{tab.Url}\nInferred PID: {association.Pid}\nConfidence: {association.Confidence:P0}\nObserved: {association.ObservedUtc.ToLocalTime():g}\nFirefox snapshot: {snapshot.SourceTimeUtc.ToLocalTime():g}", warning, depth: 3),
            Tag = tab
        };
    }

    private static Grid MakeRow(string title, string subtitle, string memory, bool accent, string tooltip, bool warning = false, int depth = 0)
    {
        var grid = new Grid { Width = 650, Margin = new Thickness(2, 2, 3, 2) };
        grid.ToolTip = tooltip;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
        var titleColor = warning ? Color.FromRgb(230, 190, 92) : accent ? Color.FromRgb(117, 214, 178) : Color.FromRgb(240, 244, 245);
        var a = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(titleColor), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
        var b = new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(168, 182, 186)), TextTrimming = TextTrimming.CharacterEllipsis };
        var c = new TextBlock { Text = memory, Foreground = Brushes.WhiteSmoke, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(a, 0); Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        grid.Children.Add(a); grid.Children.Add(b); grid.Children.Add(c);
        return grid;
    }

    private static Grid MakeTabRow(string title, string status, string tooltip, bool warning, int depth)
    {
        var grid = new Grid { Width = Math.Max(560, 680 - depth * 18), Margin = new Thickness(2, 3, 3, 3), ToolTip = tooltip };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titleColor = warning ? Color.FromRgb(230, 190, 92) : Color.FromRgb(117, 214, 178);
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(titleColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var statusBlock = new TextBlock
        {
            Text = status,
            Foreground = new SolidColorBrush(warning ? Color.FromRgb(218, 184, 105) : Color.FromRgb(168, 182, 186)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 1, 8, 0),
            FontSize = 11
        };
        Grid.SetRow(statusBlock, 1);
        grid.Children.Add(titleBlock);
        grid.Children.Add(statusBlock);
        return grid;
    }

    private static string TabKey(FirefoxTabSnapshot tab) => NormalizeTitle(tab.Title);
    private static string NormalizeTitle(string title) => CleanFirefoxTitle(title).Trim().ToUpperInvariant();
    private static string CleanFirefoxTitle(string title)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(title ?? "", @"\s*[—-]\s*Mozilla Firefox\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return cleaned.Equals("Mozilla Firefox", StringComparison.OrdinalIgnoreCase) ? "New Tab" : cleaned;
    }
    private static bool TitlesMatch(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        (left.Equals(right, StringComparison.OrdinalIgnoreCase) || left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase));

    private void ShowDetails()
    {
        _manualOpen = true;
        _detailed = true;
        MinimalView.Visibility = Visibility.Collapsed;
        DetailedView.Visibility = Visibility.Visible;
        Shell.LayoutTransform = Transform.Identity;
        Topmost = false;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 760;
        MinHeight = 430;
        Width = Math.Max(780, Width);
        Height = Math.Max(600, Height);
        _restingOpacity = 0.98;
        Opacity = IsMouseOver ? 1 : _restingOpacity;
        Show();
        Activate();
        _ = RefreshProcessesAsync();
    }

    private void ShowMinimal(bool manual)
    {
        _detailed = false;
        _manualOpen = manual;
        DetailedView.Visibility = Visibility.Collapsed;
        MinimalView.Visibility = Visibility.Visible;
        Topmost = true;
        SetMinimalSize();
        PositionAtEdgeIfNeeded();
        Show();
        if (_reading is not null) UpdateMemoryVisuals(_reading);
    }

    private void HideUntilPressure()
    {
        _manualOpen = false;
        _detailed = false;
        DetailedView.Visibility = Visibility.Collapsed;
        MinimalView.Visibility = Visibility.Visible;
        Hide();
    }

    private void SetMinimalSize()
    {
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 0;
        MinHeight = 0;
        Width = 292 * _settings.Scale;
        Height = 116 * _settings.Scale;
        Shell.LayoutTransform = new ScaleTransform(_settings.Scale, _settings.Scale);
    }

    private void ChangeScale(double delta)
    {
        _settings.Scale = Math.Clamp(_settings.Scale + delta, 0.80, 1.50);
        _settings.Save();
        if (!_detailed)
        {
            SetMinimalSize();
            PositionAtEdgeIfNeeded(force: true);
        }
    }

    private void PositionAtEdgeIfNeeded(bool force = false)
    {
        var area = SystemParameters.WorkArea;
        if (force || double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top))
        {
            Left = area.Right - Width - 18;
            Top = area.Bottom - Height - 18;
        }
        else
        {
            Left = Math.Clamp(_settings.Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(_settings.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
    }

    private ContextMenu MakeProcessMenu(ProcessSnapshot process)
    {
        var menu = new ContextMenu();
        var focus = new MenuItem { Header = "Bring to front", Tag = process };
        focus.Click += (_, _) => FocusProcess(process);
        var end = new MenuItem { Header = "End process…", Tag = process };
        end.Click += (_, _) => EndProcess(process);
        menu.Items.Add(focus);
        menu.Items.Add(new Separator());
        menu.Items.Add(end);
        return menu;
    }

    private void Shell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        var inClickableSurface = !_detailed || e.GetPosition(this).Y <= 50;
        if (!inClickableSurface) return;

        _surfacePointerDown = true;
        _surfaceDragging = false;
        _pointerStartScreen = PointToScreen(e.GetPosition(this));
        Mouse.Capture(Shell);
        e.Handled = true;
    }

    private void Shell_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_surfacePointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var current = PointToScreen(e.GetPosition(this));
        var deltaX = current.X - _pointerStartScreen.X;
        var deltaY = current.Y - _pointerStartScreen.Y;
        if (!_surfaceDragging && Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 5) return;

        _surfaceDragging = true;
        _surfacePointerDown = false;
        Mouse.Capture(null);
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WmNcLButtonDown, HtCaption, IntPtr.Zero);
        SaveLocationIfReady();
        e.Handled = true;
    }

    private void Shell_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_surfacePointerDown) return;
        var wasDragging = _surfaceDragging;
        _surfacePointerDown = false;
        _surfaceDragging = false;
        Mouse.Capture(null);

        if (wasDragging)
        {
            SaveLocationIfReady();
        }
        else if (_detailed)
        {
            ShowMinimal(manual: true);
        }
        else
        {
            ShowDetails();
        }
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? item) where T : DependencyObject
    {
        while (item is not null)
        {
            if (item is T found) return found;
            item = VisualTreeHelper.GetParent(item);
        }
        return null;
    }

    private void ProcessTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedProcess = (e.NewValue as TreeViewItem)?.Tag as ProcessSnapshot;
        var enabled = _selectedProcess is not null && !_selectedProcess.IsSynthetic;
        FocusButton.IsEnabled = enabled;
        EndButton.IsEnabled = enabled;
        SelectionText.Text = enabled ? $"{_selectedProcess!.DisplayName} · PID {_selectedProcess.Pid} · {_selectedProcess.Role}" : "Select an individual process";
    }

    private void Focus_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null) return;
        FocusProcess(_selectedProcess);
    }

    private void FocusProcess(ProcessSnapshot processInfo)
    {
        try
        {
            using var process = Process.GetProcessById(processInfo.Pid);
            if (process.MainWindowHandle == IntPtr.Zero)
            {
                MessageBox.Show("That process has no separate window to bring forward. Select its parent app or use the app's own process view.", "Memory Bloom", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowWindow(process.MainWindowHandle, 9);
            SetForegroundWindow(process.MainWindowHandle);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not focus process", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void End_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null) return;
        EndProcess(_selectedProcess);
    }

    private void EndProcess(ProcessSnapshot processInfo)
    {
        var answer = MessageBox.Show(
            $"End {processInfo.DisplayName} process {processInfo.Pid}?\n\nUnsaved work in that process can be lost.",
            "End process?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            using var process = Process.GetProcessById(processInfo.Pid);
            process.Kill(entireProcessTree: false);
            _ = RefreshProcessesAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not end process", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ProcessTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((ProcessTree.SelectedItem as TreeViewItem)?.Tag is not ProcessSnapshot process || process.IsSynthetic) return;
        FocusProcess(process);
        e.Handled = true;
    }

    private void ProcessTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindParent<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        item.IsSelected = true;
        item.Focus();
    }

    private void OpenFirefoxProcesses_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe", "", null) as string
                ?? Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe", "", null) as string
                ?? @"C:\Program Files\Mozilla Firefox\firefox.exe";
            Process.Start(new ProcessStartInfo(path, "about:processes") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Firefox could not be opened.\n\n{ex.Message}", "Memory Bloom", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void RegisterShortcut()
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(WindowHook);
        RegisterHotKey(source.Handle, HotkeyId, ModControl | ModAlt, VkM);
    }

    private void ApplyNativeWindowShape()
    {
        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;

        const int dwmWindowCornerPreference = 33;
        const int roundCorners = 2;
        var preference = roundCorners;
        _ = DwmSetWindowAttribute(window, dwmWindowCornerPreference, ref preference, sizeof(int));

        if (WindowState == WindowState.Maximized)
        {
            _ = SetWindowRgn(window, IntPtr.Zero, true);
            return;
        }
        if (!GetWindowRect(window, out var rectangle)) return;
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        var radius = _detailed ? 22 : (int)Math.Round(22 * _settings.Scale);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }

    private async Task TryPinToAllDesktopsAsync()
    {
        if (_virtualDesktopPinned) return;
        var handle = new WindowInteropHelper(this).Handle;
        for (var attempt = 0; attempt < 3 && !_virtualDesktopPinned; attempt++)
        {
            if (attempt > 0) await Task.Delay(350);
            _virtualDesktopPinned = VirtualDesktopPinning.TryPinWindow(handle, out _);
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            ShowDetails();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show details", null, (_, _) => Dispatcher.Invoke(ShowDetails));
        menu.Items.Add("Show minimal", null, (_, _) => Dispatcher.Invoke(() => ShowMinimal(manual: true)));
        menu.Items.Add("Hide until pressure rises", null, (_, _) => Dispatcher.Invoke(HideUntilPressure));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        return menu;
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/MemoryBloom.ico", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource is not null)
            {
                using var loaded = new Drawing.Icon(resource.Stream);
                return (Drawing.Icon)loaded.Clone();
            }
        }
        catch
        {
        }
        return Drawing.SystemIcons.Application;
    }

    private void Cleanup()
    {
        _timer.Stop();
        _firefoxTimer.Stop();
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is not null) UnregisterHotKey(source.Handle, HotkeyId);
        _tray.Visible = false;
        _tray.Dispose();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private async Task CapturePreviewsAsync(string folder)
    {
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            _reading = MemoryProbe.ReadMemory();
            UpdateMemoryVisuals(_reading);
            WindowState = WindowState.Normal;
            Width = 780;
            Height = 600;
            ShowDetails();
            while (_processRefreshRunning)
                await Task.Delay(75);
            if (ProcessTree.Items.Count == 0)
                await RefreshProcessesAsync();
            TreeViewItem? previewProcess = null;
            TreeViewItem? previewTabsSection = null;
            TreeViewItem? previewTab = null;
            var firefoxRoot = ProcessTree.Items.OfType<TreeViewItem>().FirstOrDefault(item => (item.Tag as ProcessGroup)?.IsFirefox == true);
            var contentSection = firefoxRoot?.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is FirefoxContentSection);
            if (contentSection is not null)
            {
                contentSection.IsExpanded = true;
                previewProcess = contentSection.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is ProcessSnapshot);
                if (previewProcess is not null) previewProcess.IsExpanded = true;
                var tabsSection = contentSection.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is FirefoxTabSection);
                if (tabsSection is not null)
                {
                    tabsSection.IsExpanded = true;
                    previewTabsSection = tabsSection;
                    previewTab = tabsSection.Items.OfType<TreeViewItem>().FirstOrDefault();
                }
            }
            UpdateLayout();
            firefoxRoot?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "detailed.png"));

            (previewProcess?.Items.OfType<TreeViewItem>().FirstOrDefault() ?? previewProcess)?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "firefox-processes.png"));

            (previewTab ?? previewTabsSection)?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "firefox-tabs.png"));

            ShowMinimal(manual: true);
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "minimal.png"));
        }
        finally
        {
            ExitApp();
        }
    }

    private void SaveVisual(string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private void SaveLocationIfReady()
    {
        if (!_ready || WindowState != WindowState.Normal || !IsVisible) return;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { ChangeScale(0.10); e.Handled = true; }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { ChangeScale(-0.10); e.Handled = true; }
    private void ShowMinimal_Click(object sender, RoutedEventArgs e) => ShowMinimal(manual: true);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideUntilPressure();
    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ChangeScale(e.Delta > 0 ? 0.10 : -0.10); }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.ShowAtPercent = Math.Round(e.NewValue);
        _settings.HideBelowPercent = Math.Max(60, _settings.ShowAtPercent - 6);
        ThresholdText.Text = $"{_settings.ShowAtPercent:0}%";
        _settings.Save();
    }

    private static Geometry MakeArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.001, 0.999);
        const double center = 34;
        const double radius = 29;
        var endAngle = -90 + 360 * fraction;
        var radians = endAngle * Math.PI / 180;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static string FormatBytes(ulong bytes) => FormatBytes((long)Math.Min(bytes, long.MaxValue));
    private static string FormatBytes(long bytes)
    {
        var gib = bytes / 1073741824d;
        return gib >= 0.1 ? $"{gib:0.0} GB" : $"{bytes / 1048576d:0} MB";
    }
    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalSeconds < 45) return "just now";
        if (age.TotalMinutes < 60) return $"{age.TotalMinutes:0}m";
        if (age.TotalHours < 48) return $"{age.TotalHours:0}h";
        if (age.TotalDays < 60) return $"{age.TotalDays:0}d";
        return $"{age.TotalDays / 30:0}mo";
    }
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
