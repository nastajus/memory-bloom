using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
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
    private const double CompactCardWidth = 292;
    private const double CompactCardHeight = 116;
    private const double CompactOrbSize = 92;
    private enum ExpandedPage
    {
        Processes,
        History
    }

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
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint WmNcLButtonDown = 0x00A1;
    private static readonly TimeSpan CurrentAssociationLifetime = TimeSpan.FromMinutes(2);
    private static readonly IntPtr HtCaption = new(2);
    private static readonly ProcessRowWidthConverter RowWidthConverter = new();

    private sealed class ProcessRowWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var available = value is double width ? width : 0;
            var depth = parameter is int integer ? integer : 0;
            return Math.Max(320, available - 42 - depth * 18);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _firefoxTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _compactHoverLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly FirefoxCompanionServer _firefoxCompanion;
    private readonly MemoryHistoryStore _history = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly Dictionary<string, bool> _groupExpansionState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, bool> _processExpansionState = new();
    private readonly Dictionary<int, double> _previousCpuByPid = new();
    private readonly Dictionary<int, long> _previousWorkingSetByPid = new();
    private readonly Dictionary<string, TabProcessAssociation> _tabAssociations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, double>> _tabActivityEvidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _liveTabAccess = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _liveTabTitles = new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr, string> _liveTitleKeyByWindow = new();
    private readonly Dictionary<string, DateTime> _supersededLiveTitleKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> _latestFirefoxTabHostPids = new();
    private List<HistoryProcessSample> _latestHistoryProcesses = new();
    private List<SystemHistoryMarker> _historyMarkers = new();
    private List<ProcessGroup> _latestGroups = new();
    private FirefoxTabsSnapshot _firefoxSessionTabs = new();
    private FirefoxTabsSnapshot _firefoxCompanionTabs = new();
    private FirefoxTabsSnapshot _latestFirefoxTabs = new();
    private string _pendingCompanionTabKey = "";
    private DateTime _pendingCompanionProbeUntilUtc;
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
    private bool _historyContextRefreshRunning;
    private bool _previewMode;
    private DateTime _lastHistoryContextUtc;
    private ExpandedPage _expandedPage = ExpandedPage.Processes;
    private int _quietTicks;
    private int _tick;
    private bool _ready;
    private bool _surfacePointerDown;
    private bool _surfaceDragging;
    private bool _updatingProcessFontSize;
    private bool _orbExpanded;
    private bool _orbAnchoredLeft;
    private Point _pointerStartScreen;
    private bool _hasCompactAnchor;
    private bool _compactAnchoredRight;
    private double _compactLeft;
    private double _compactRight;
    private double _compactTop;
    private Rect _compactWorkArea;
    private bool UseOrbCompact => !_settings.CompactStyle.Equals("Card", StringComparison.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        _firefoxCompanion = new FirefoxCompanionServer(update => Dispatcher.BeginInvoke(() => ApplyFirefoxCompanionUpdate(update)));
        Opacity = 0;
        ThresholdSlider.Value = _settings.ShowAtPercent;
        ThresholdText.Text = $"{_settings.ShowAtPercent:0}%";
        _settings.ProcessFontSize = Math.Clamp(Math.Round(_settings.ProcessFontSize), 10, 18);
        if (!_settings.CompactStyle.Equals("Card", StringComparison.OrdinalIgnoreCase))
            _settings.CompactStyle = "Orb";
        if (!_settings.OrbLocationMigrated)
        {
            if (UseOrbCompact && double.IsFinite(_settings.Left))
                _settings.Left += (CompactCardWidth - CompactOrbSize) * _settings.Scale;
            _settings.OrbLocationMigrated = true;
            _settings.Save();
        }
        ProcessTree.FontSize = _settings.ProcessFontSize;
        ProcessFontSizeSlider.Value = _settings.ProcessFontSize;
        ProcessFontSizeText.Text = $"{_settings.ProcessFontSize:0} pt";
        UpdateCompactStyleButton();
        HistoryChart.ViewChanged += (_, _) => UpdateHistorySummary();
        _compactHoverLeaveTimer.Tick += (_, _) =>
        {
            _compactHoverLeaveTimer.Stop();
            if (IsMouseOver) return;
            SetOrbExpanded(false);
            Opacity = _restingOpacity;
        };

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
        MouseEnter += MainWindow_MouseEnter;
        MouseLeave += MainWindow_MouseLeave;
        Closed += (_, _) => Cleanup();
        Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideUntilPressure();
        };
        MouseWheel += MainWindow_MouseWheel;
    }

    public void InitializeInBackground(bool showNow, string? previewFolder = null, bool activateInitial = true)
    {
        _previewMode = previewFolder is not null;
        if (previewFolder is not null)
            Loaded += async (_, _) => await CapturePreviewsAsync(previewFolder);
        ShowActivated = activateInitial;
        SetMinimalSize();
        PositionAtEdgeIfNeeded();
        Show();
        if (showNow) ShowDetails(activateInitial);
        else Hide();
        ShowActivated = true;
        _ready = true;
        _timer.Start();
        _firefoxTimer.Start();
        _ = LoadWindowsHistoryAsync();
        _ = SampleAsync();
    }

    private async Task SampleAsync()
    {
        try
        {
            _reading = MemoryProbe.ReadMemory();
            UpdateMemoryVisuals(_reading);
            _history.Record(_reading, _latestHistoryProcesses, _settings.ShowAtPercent, _settings.HideBelowPercent, persist: !_previewMode);
            if (DateTime.UtcNow - _lastHistoryContextUtc >= TimeSpan.FromSeconds(30))
                _ = RefreshHistoryContextAsync();
            if (_detailed && _expandedPage == ExpandedPage.History) UpdateHistoryView();
            EvaluateVisibility(_reading.PressurePercent);
            _tick++;
            if (_detailed && _expandedPage == ExpandedPage.Processes && (_tick % 3 == 0 || ProcessTree.Items.Count == 0))
                await RefreshProcessesAsync();
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Memory reading unavailable";
            LoadingText.Text = ex.Message;
        }
    }

    private async Task RefreshHistoryContextAsync()
    {
        if (_historyContextRefreshRunning) return;
        _historyContextRefreshRunning = true;
        _lastHistoryContextUtc = DateTime.UtcNow;
        try
        {
            _latestHistoryProcesses = await Task.Run(() => MemoryProbe.ReadTopMemoryConsumers());
            if (_detailed && _expandedPage == ExpandedPage.History) UpdateHistoryView();
        }
        finally
        {
            _historyContextRefreshRunning = false;
        }
    }

    private async Task LoadWindowsHistoryAsync()
    {
        _historyMarkers = await Task.Run(() => WindowsHistoryProbe.ReadRecentMarkers());
        if (_detailed && _expandedPage == ExpandedPage.History) UpdateHistoryView();
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
        PressureTagline.Text = PressureTaglineFor(pressure);
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

    private string PressureTaglineFor(double pressure)
    {
        if (pressure < _settings.HideBelowPercent) return "Memory is comfortable";
        if (pressure < _settings.ShowAtPercent) return "Memory use is rising";
        if (pressure < 90) return "Memory is getting busy";
        return "Critical memory pressure";
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
            ObserveLiveFirefoxTab(foreground.IsForeground, foreground.Title, foreground.Window);
            var mergedTabs = MergeFirefoxTabs(CurrentFirefoxSnapshot());
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
                groups = OrderGroupsByMemory(groups).ToList();
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
        if (_firefoxBrowserPid != 0 && browserPid != _firefoxBrowserPid)
        {
            _previousCpuByPid.Clear();
            _previousWorkingSetByPid.Clear();
            _tabAssociations.Clear();
            _tabActivityEvidence.Clear();
            _liveTabAccess.Clear();
            _liveTabTitles.Clear();
            _liveTitleKeyByWindow.Clear();
            _supersededLiveTitleKeys.Clear();
            _lastForegroundFirefoxTitle = "";
            _firefoxCompanionTabs = new FirefoxTabsSnapshot();
            _pendingCompanionTabKey = "";
            _pendingCompanionProbeUntilUtc = default;
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
        var liveChanged = ObserveLiveFirefoxTab(foreground.IsForeground, foreground.Title, foreground.Window);
        var mergedTabs = MergeFirefoxTabs(CurrentFirefoxSnapshot());
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

    private (bool IsForeground, string Title, IntPtr Window) GetForegroundFirefoxTitle()
    {
        try
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var foregroundPid);
            using var foreground = Process.GetProcessById((int)foregroundPid);
            if (!foreground.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return (false, "", IntPtr.Zero);
            return (true, CleanFirefoxTitle(foreground.MainWindowTitle), window);
        }
        catch
        {
            return (false, "", IntPtr.Zero);
        }
    }

    private bool ObserveLiveFirefoxTab(bool firefoxIsForeground, string activeTitle, IntPtr window)
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
            if (window != IntPtr.Zero && _liveTitleKeyByWindow.TryGetValue(window, out var previousKey) && !previousKey.Equals(normalized, StringComparison.Ordinal))
            {
                _liveTabAccess.Remove(previousKey);
                _liveTabTitles.Remove(previousKey);
                _tabAssociations.Remove(previousKey);
                _tabActivityEvidence.Remove(previousKey);
                _supersededLiveTitleKeys[previousKey] = DateTime.UtcNow;
            }
            _supersededLiveTitleKeys.Remove(normalized);
            _liveTabAccess[normalized] = DateTime.UtcNow;
            _liveTabTitles[normalized] = activeTitle;
            if (window != IntPtr.Zero) _liveTitleKeyByWindow[window] = normalized;
        }
        _lastForegroundFirefoxTitle = normalized;
        return titleChanged;
    }

    private FirefoxTabsSnapshot CurrentFirefoxSnapshot() =>
        _firefoxCompanionTabs.IsCompanion && DateTime.UtcNow - _firefoxCompanionTabs.SourceTimeUtc < TimeSpan.FromSeconds(25)
            ? _firefoxCompanionTabs
            : _firefoxSessionTabs;

    private void ApplyFirefoxCompanionUpdate(FirefoxCompanionUpdate update)
    {
        if (_firefoxCompanionTabs.SessionId.Length > 0 && !_firefoxCompanionTabs.SessionId.Equals(update.Snapshot.SessionId, StringComparison.Ordinal))
        {
            _tabAssociations.Clear();
            _tabActivityEvidence.Clear();
        }
        _firefoxCompanionTabs = update.Snapshot;
        if (update.EventTabKey.Length > 0)
        {
            _pendingCompanionTabKey = update.EventTabKey;
            _pendingCompanionProbeUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        }
        if (_detailed && !_processRefreshRunning && _latestGroups.Count > 0)
        {
            _latestFirefoxTabs = MergeFirefoxTabs(CurrentFirefoxSnapshot());
            PopulateTree(_latestGroups, _latestFirefoxTabs);
        }
    }

    private FirefoxTabsSnapshot MergeFirefoxTabs(FirefoxTabsSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        foreach (var window in _liveTitleKeyByWindow.Keys.Where(window => !IsWindow(window)).ToList())
            _liveTitleKeyByWindow.Remove(window);
        foreach (var key in _liveTabAccess.Where(item => now - item.Value > TimeSpan.FromMinutes(10)).Select(item => item.Key).ToList())
        {
            _liveTabAccess.Remove(key);
            _liveTabTitles.Remove(key);
            foreach (var window in _liveTitleKeyByWindow.Where(item => item.Value.Equals(key, StringComparison.Ordinal)).Select(item => item.Key).ToList())
                _liveTitleKeyByWindow.Remove(window);
        }

        foreach (var key in _supersededLiveTitleKeys.Where(item => snapshot.SourceTimeUtc >= item.Value).Select(item => item.Key).ToList())
            _supersededLiveTitleKeys.Remove(key);
        var tabs = snapshot.Tabs.Where(tab => !_supersededLiveTitleKeys.ContainsKey(NormalizeTitle(tab.Title))).ToList();
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

        if (!snapshot.IsCompanion)
        {
            tabs = tabs
                .GroupBy(tab => NormalizeTitle(tab.Title), StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(tab => tab.IsLiveObservation)
                    .ThenByDescending(tab => tab.LastAccessedUtc)
                    .First())
                .ToList();
        }

        return new FirefoxTabsSnapshot
        {
            Tabs = tabs.OrderByDescending(tab => tab.LastAccessedUtc).ToList(),
            SourcePath = snapshot.SourcePath,
            SourceTimeUtc = snapshot.SourceTimeUtc,
            Error = snapshot.Error,
            IsCompanion = snapshot.IsCompanion,
            SessionId = snapshot.SessionId
        };
    }

    private bool UpdateFirefoxAssociations(
        IReadOnlyList<ProcessActivity> content,
        FirefoxTabsSnapshot tabsSnapshot,
        bool firefoxIsForeground,
        string activeTitle)
    {
        var now = DateTime.UtcNow;
        var companionProbe = _pendingCompanionTabKey.Length > 0 && now <= _pendingCompanionProbeUntilUtc
            ? tabsSnapshot.Tabs.FirstOrDefault(tab => TabKey(tab).Equals(_pendingCompanionTabKey, StringComparison.Ordinal))
            : null;
        var activeTab = companionProbe ?? tabsSnapshot.Tabs.FirstOrDefault(tab => tab.Active) ?? tabsSnapshot.Tabs.FirstOrDefault(tab => TitlesMatch(tab.Title, activeTitle));
        var renderChanged = false;

        if ((firefoxIsForeground || companionProbe is not null) && activeTab is not null && content.Count > 0)
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
        foreach (var group in OrderGroupsByMemory(groups))
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
                var tabHosts = OrderProcessesByMemory(group.Processes.Where(IsFirefoxTabHost)).ToList();
                var tabsByPid = firefoxTabs.Tabs
                    .Select(tab => (Tab: tab, Association: _tabAssociations.GetValueOrDefault(TabKey(tab))))
                    .Where(item => item.Association is not null && tabHosts.Any(process => process.Pid == item.Association.Pid))
                    .GroupBy(item => item.Association!.Pid)
                    .ToDictionary(grouped => grouped.Key, grouped => grouped.ToList());
                var observedLinks = tabsByPid.Sum(item => item.Value.Count(value => IsAssociationCurrent(value.Association!)));
                var historicalLinks = tabsByPid.Sum(item => item.Value.Count(value => !IsAssociationCurrent(value.Association!)));
                var contentSection = new TreeViewItem
                {
                    Header = MakeRow("Tab hosts", $"{tabHosts.Count} candidates · {observedLinks} current · {historicalLinks} historical · {(firefoxTabs.IsCompanion ? "companion live" : "recovery")}", FormatBytes(tabHosts.Sum(process => process.PrivateBytes)), true,
                        "These are Firefox processes capable of hosting site or built-in pages—not a count of open tabs. The optional companion makes the tab catalog authoritative and event-driven, but PID links remain inferred from short process-activity bursts. Historical links stay visible after current evidence expires.",
                        depth: 1),
                    Tag = new FirefoxContentSection("Tab hosts"),
                    IsExpanded = _firefoxContentExpanded
                };
                var unresolved = firefoxTabs.Tabs.Where(tab =>
                {
                    var association = _tabAssociations.GetValueOrDefault(TabKey(tab));
                    return association is null || tabHosts.All(process => process.Pid != association.Pid);
                }).ToList();
                contentSection.Items.Add(MakeUnresolvedTabsSection(group, firefoxTabs, unresolved));
                foreach (var process in tabHosts)
                {
                    tabsByPid.TryGetValue(process.Pid, out var associatedTabs);
                    contentSection.Items.Add(MakeProcessTreeItem(process, true, associatedTabs, firefoxTabs, rowDepth: 2));
                }

                var firefoxChildren = group.Processes.Except(tabHosts)
                    .Select(process => (Weight: process.PrivateBytes, Item: MakeProcessTreeItem(process, true, rowDepth: 1)))
                    .Append((Weight: tabHosts.Sum(process => process.PrivateBytes), Item: contentSection))
                    .OrderByDescending(item => item.Weight)
                    .ToList();
                foreach (var item in firefoxChildren) root.Items.Add(item.Item);
            }
            else
            {
                foreach (var process in OrderProcessesByMemory(group.Processes)) root.Items.Add(MakeProcessTreeItem(process, false, rowDepth: 1));
            }
            ProcessTree.Items.Add(root);
        }
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private static IOrderedEnumerable<ProcessSnapshot> OrderProcessesByMemory(IEnumerable<ProcessSnapshot> processes) =>
        processes.OrderByDescending(process => process.PrivateBytes).ThenBy(process => process.Pid);

    private static IOrderedEnumerable<ProcessGroup> OrderGroupsByMemory(IEnumerable<ProcessGroup> groups) =>
        groups.OrderByDescending(group => group.TotalPrivateBytes).ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

    private static bool IsAssociationCurrent(TabProcessAssociation association) =>
        DateTime.UtcNow - association.ObservedUtc <= CurrentAssociationLifetime;

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
        var currentTabCount = associatedTabs?.Count(item => item.Association is not null && IsAssociationCurrent(item.Association)) ?? 0;
        var historicalTabCount = associatedTabs?.Count - currentTabCount ?? 0;
        var containsCurrentWebPage = firefox && IsFirefoxTabHost(process) && currentTabCount > 0;
        if (firefox && IsFirefoxTabHost(process))
            descriptor += currentTabCount == 0 && historicalTabCount == 0
                ? " · no tab observed"
                : $" · {currentTabCount} current{(historicalTabCount > 0 ? $" · {historicalTabCount} historical" : "")}";
        var child = new TreeViewItem
        {
            Header = MakeRow(
                process.IsSynthetic ? process.DisplayName : containsCurrentWebPage ? $"● PID {process.Pid}" : $"PID {process.Pid}",
                descriptor,
                FormatBytes(process.PrivateBytes),
                firefox,
                process.IsSynthetic
                    ? process.Tooltip
                    : $"Private committed memory: {FormatBytes(process.PrivateBytes)}. Current physical working set: {FormatBytes(process.WorkingSetBytes)}. {process.Role}{(containsCurrentWebPage ? " A blue dot marks a host with one or more current inferred web-page links." : "")}",
                depth: rowDepth,
                activeHost: containsCurrentWebPage),
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
        var freshness = snapshot.IsCompanion
            ? $"companion live · {snapshot.Tabs.Count} open tab{(snapshot.Tabs.Count == 1 ? "" : "s")}"
            : snapshot.Error is not null
            ? "live observations only · session snapshot pending"
            : $"snapshot {FormatAge(sourceAge)} old{(activeTitleMatches ? "" : " · active title differs")}";
        var section = new TreeViewItem
        {
            Header = MakeRow("Unresolved tabs", freshness, $"{unresolved.Count}", true,
                "Tabs that have not yet produced a confident process-activity match, or whose last matched PID has disappeared. With the companion installed this list is the current Firefox tab catalog; without it, it comes from Firefox's delayed recovery snapshot.",
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
            var association = _tabAssociations.GetValueOrDefault(TabKey(tab));
            var associationAge = association is null ? TimeSpan.Zero : DateTime.UtcNow - association.ObservedUtc;
            var stale = association is not null && !IsAssociationCurrent(association);
            var subtitle = stale
                ? $"last PID {association!.Pid} evidence expired · seen {FormatAge(associationAge)} ago · last used {FormatAge(age)} ago"
                : $"Firefox snapshot · last used {FormatAge(age)} ago{(tab.Hidden ? " · hidden" : "")}";
            var tooltip = stale
                ? $"{tab.Title}\n{tab.Url}\nLast interacted: {tab.LastAccessedUtc.ToLocalTime():g}\nThe PID {association!.Pid} match expired because it has not been refreshed for {FormatAge(associationAge)}."
                : $"{tab.Title}\n{tab.Url}\nLast interacted: {tab.LastAccessedUtc.ToLocalTime():g}\nNo current PID association.";
            section.Items.Add(new TreeViewItem
            {
                Header = MakeTabRow(tab.Title, subtitle, tooltip, stale, depth: 3),
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
        var ageFraction = Math.Clamp(observationAge.TotalMilliseconds / CurrentAssociationLifetime.TotalMilliseconds, 0, 1);
        var currentConfidence = association.Confidence * (1 - ageFraction * 0.50);
        var current = IsAssociationCurrent(association);
        var warning = !current || observationAge > TimeSpan.FromMinutes(1) || (!tab.IsLiveObservation && sourceAge > TimeSpan.FromMinutes(2)) || currentConfidence < 0.60 || driftRecent;
        var status = !current
            ? $"historical PID evidence expired · observed {FormatAge(observationAge)} ago · last used {FormatAge(DateTime.UtcNow - tab.LastAccessedUtc)} ago"
            : driftRecent
            ? $"moved from PID {association.PreviousPid} · seen {FormatAge(observationAge)} ago"
            : $"{(snapshot.IsCompanion ? "companion tab · " : tab.IsLiveObservation ? "title seen live · " : "")}current confidence {currentConfidence:P0} · observed {FormatAge(observationAge)} ago · last used {FormatAge(DateTime.UtcNow - tab.LastAccessedUtc)} ago";
        return new TreeViewItem
        {
            Header = MakeTabRow(tab.Title, status,
                $"{tab.Title}\n{tab.Url}\n{(current ? "Inferred PID" : "Historical inferred PID")}: {association.Pid}\nConfidence at observation: {association.Confidence:P0}\n{(current ? $"Current age-adjusted confidence: {currentConfidence:P0}" : "Current placement: expired; retained as history")}\nObserved: {association.ObservedUtc.ToLocalTime():g}\nA link stops being current after {CurrentAssociationLifetime.TotalMinutes:0} minutes without fresh evidence, but remains visible as historical evidence while the process and tab still exist.\nTab source: {(snapshot.IsCompanion ? "Firefox companion" : "Firefox recovery snapshot")}", warning, depth: 3),
            Tag = tab
        };
    }

    private Grid MakeRow(string title, string subtitle, string memory, bool accent, string tooltip, bool warning = false, int depth = 0, bool activeHost = false)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2, 2, 3, 2) };
        grid.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = ProcessTree, Converter = RowWidthConverter, ConverterParameter = depth });
        grid.ToolTip = tooltip;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 76 });
        var titleColor = warning ? Color.FromRgb(230, 190, 92) : activeHost ? Color.FromRgb(92, 203, 255) : accent ? Color.FromRgb(117, 214, 178) : Color.FromRgb(240, 244, 245);
        var a = new TextBlock { Text = title, FontWeight = activeHost ? FontWeights.Bold : FontWeights.SemiBold, Foreground = new SolidColorBrush(titleColor), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
        var b = new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(168, 182, 186)), TextTrimming = TextTrimming.CharacterEllipsis };
        var c = new TextBlock { Text = memory, Foreground = Brushes.WhiteSmoke, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(a, 0); Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        grid.Children.Add(a); grid.Children.Add(b); grid.Children.Add(c);
        return grid;
    }

    private Grid MakeTabRow(string title, string status, string tooltip, bool warning, int depth)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(2, 3, 3, 3), ToolTip = tooltip };
        grid.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = ProcessTree, Converter = RowWidthConverter, ConverterParameter = depth });
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
            FontSize = Math.Max(9, _settings.ProcessFontSize - 2)
        };
        Grid.SetRow(statusBlock, 1);
        grid.Children.Add(titleBlock);
        grid.Children.Add(statusBlock);
        return grid;
    }

    private static string TabKey(FirefoxTabSnapshot tab) => string.IsNullOrWhiteSpace(tab.StableId) ? NormalizeTitle(tab.Title) : tab.StableId;
    private static string NormalizeTitle(string title)
    {
        var cleaned = CleanFirefoxTitle(title);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[—-]\s*ChatGPT\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return cleaned.ToUpperInvariant();
    }
    private static string CleanFirefoxTitle(string title)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(title ?? "", @"\s*[—-]\s*Mozilla Firefox\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return cleaned.Equals("Mozilla Firefox", StringComparison.OrdinalIgnoreCase) ? "New Tab" : cleaned;
    }
    private static bool TitlesMatch(string left, string right)
    {
        var normalizedLeft = NormalizeTitle(left);
        var normalizedRight = NormalizeTitle(right);
        return normalizedLeft.Length > 0 && normalizedRight.Length > 0 &&
            (normalizedLeft.Equals(normalizedRight, StringComparison.Ordinal) || normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal));
    }

    private void ShowDetails() => ShowDetails(activate: true);

    private void ShowDetails(bool activate)
    {
        var enteringFromCompact = !_detailed;
        if (enteringFromCompact)
        {
            _compactWorkArea = GetCurrentWorkArea();
            var compactWidth = ActualWidth > 0 ? ActualWidth : Width;
            _compactLeft = Left;
            _compactRight = Left + compactWidth;
            _compactTop = Top;
            _compactAnchoredRight = Left + compactWidth / 2 >= _compactWorkArea.Left + _compactWorkArea.Width / 2;
            _hasCompactAnchor = true;
        }

        _manualOpen = true;
        _detailed = true;
        _expandedPage = ExpandedPage.Processes;
        MinimalView.Visibility = Visibility.Collapsed;
        DetailedView.Visibility = Visibility.Visible;
        ApplyExpandedPage();
        Shell.LayoutTransform = Transform.Identity;
        Shell.CornerRadius = new CornerRadius(22);
        Shell.Padding = new Thickness(12);
        Topmost = false;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 760;
        MinHeight = 430;
        Width = Math.Max(780, Width);
        Height = Math.Max(600, Height);
        if (enteringFromCompact)
        {
            Left = _compactAnchoredRight
                ? Math.Max(_compactWorkArea.Left, _compactRight - Width)
                : Math.Min(_compactLeft, _compactWorkArea.Right - Width);
            Top = Math.Clamp(_compactTop, _compactWorkArea.Top, Math.Max(_compactWorkArea.Top, _compactWorkArea.Bottom - Height));
        }
        _restingOpacity = 0.98;
        Opacity = IsMouseOver ? 1 : _restingOpacity;
        ShowActivated = activate;
        Show();
        if (activate) Activate();
        ShowActivated = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyNativeWindowShape));
        _ = RefreshProcessesAsync();
    }

    private void ApplyExpandedPage()
    {
        var processes = _expandedPage == ExpandedPage.Processes;
        ProcessSummary.Visibility = processes ? Visibility.Visible : Visibility.Collapsed;
        ProcessTreeContainer.Visibility = processes ? Visibility.Visible : Visibility.Collapsed;
        ProcessFooter.Visibility = processes ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = processes ? Visibility.Collapsed : Visibility.Visible;
        FirefoxProcessesButton.Visibility = processes ? Visibility.Visible : Visibility.Collapsed;
        PageSwitchButton.Content = processes ? "History" : "Processes";
        PageSwitchButton.ToolTip = processes ? "Open the memory history timeline" : "Return to the process investigator";
        if (!processes) UpdateHistoryView();
    }

    private void ShowHistoryPage()
    {
        if (!_detailed) ShowDetails();
        _expandedPage = ExpandedPage.History;
        ApplyExpandedPage();
    }

    private void ShowProcessPage()
    {
        if (!_detailed) ShowDetails();
        _expandedPage = ExpandedPage.Processes;
        ApplyExpandedPage();
        _ = RefreshProcessesAsync();
    }

    private void UpdateHistoryView()
    {
        HistoryChart.SetData(_history.Points, _historyMarkers);
        UpdateHistorySummary();
    }

    private void UpdateHistorySummary()
    {
        if (HistoryChart is null) return;
        var start = HistoryChart.VisibleStartUtc;
        var end = HistoryChart.VisibleEndUtc;
        var visible = _history.Points.Where(point => point.TimestampUtc >= start && point.TimestampUtc <= end).ToList();
        var markers = _historyMarkers.Count(marker => marker.TimestampUtc >= start && marker.TimestampUtc <= end);
        HistoryCurrentText.Text = _reading is null ? "--%" : $"{_reading.PressurePercent:0}%";
        var measuredPeak = visible.Select(point => (double?)point.PressurePercent)
            .Concat(_historyMarkers.Where(marker => marker.TimestampUtc >= start && marker.TimestampUtc <= end).Select(marker => marker.PressurePercent))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty(double.NaN)
            .Max();
        HistoryPeakText.Text = double.IsNaN(measuredPeak) ? "--%" : $"{measuredPeak:0}%";
        HistorySpikeText.Text = (markers + visible.Count(point => point.Classification != "Normal")).ToString(CultureInfo.CurrentCulture);
        HistorySinceText.Text = _history.TrackingSinceUtc is DateTime since
            ? $"Since {since.ToLocalTime():g}"
            : "Starting now";
        var range = HistoryChart.VisibleRange;
        HistoryRangeText.Text = HistoryChart.IsFollowingLive
            ? $"Live · {FormatHistoryRange(range)}"
            : $"{start.ToLocalTime():g} – {end.ToLocalTime():g}";
    }

    private static string FormatHistoryRange(TimeSpan? range)
    {
        if (range is null) return "all retained data";
        if (range.Value.TotalDays >= 1) return $"{range.Value.TotalDays:0} day{(range.Value.TotalDays == 1 ? "" : "s")}";
        if (range.Value.TotalHours >= 1) return $"{range.Value.TotalHours:0} hour{(range.Value.TotalHours == 1 ? "" : "s")}";
        return $"{range.Value.TotalMinutes:0} minutes";
    }

    private void ShowMinimal(bool manual)
    {
        _detailed = false;
        _manualOpen = manual;
        DetailedView.Visibility = Visibility.Collapsed;
        MinimalView.Visibility = Visibility.Visible;
        Topmost = true;
        SetMinimalSize();
        if (_hasCompactAnchor)
        {
            Left = _compactAnchoredRight ? _compactRight - Width : _compactLeft;
            Left = Math.Clamp(Left, _compactWorkArea.Left, Math.Max(_compactWorkArea.Left, _compactWorkArea.Right - Width));
            Top = Math.Clamp(_compactTop, _compactWorkArea.Top, Math.Max(_compactWorkArea.Top, _compactWorkArea.Bottom - Height));
        }
        else
        {
            PositionAtEdgeIfNeeded();
        }
        Show();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyNativeWindowShape));
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
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 0;
        MinHeight = 0;
        _orbExpanded = false;
        ApplyCompactPresentation();
        Width = (UseOrbCompact ? CompactOrbSize : CompactCardWidth) * _settings.Scale;
        Height = (UseOrbCompact ? CompactOrbSize : CompactCardHeight) * _settings.Scale;
        Shell.LayoutTransform = new ScaleTransform(_settings.Scale, _settings.Scale);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyNativeWindowShape));
    }

    private void ApplyCompactPresentation()
    {
        if (UseOrbCompact)
        {
            Shell.Padding = new Thickness(11);
            if (!_orbExpanded)
            {
                CompactLeftColumn.Width = new GridLength(68);
                CompactCenterColumn.Width = new GridLength(0);
                Grid.SetColumn(MinimalGauge, 0);
            }
            else if (_orbAnchoredLeft)
            {
                CompactLeftColumn.Width = new GridLength(68);
                CompactCenterColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(MinimalGauge, 0);
            }
            else
            {
                CompactLeftColumn.Width = new GridLength(1, GridUnitType.Star);
                CompactCenterColumn.Width = new GridLength(68);
                Grid.SetColumn(MinimalGauge, 1);
            }
            CompactRightColumn.Width = new GridLength(0);
            Grid.SetColumn(MinimalTextPanel, _orbAnchoredLeft ? 1 : 0);
            MinimalTextPanel.Visibility = _orbExpanded ? Visibility.Visible : Visibility.Collapsed;
            MinimalZoomPanel.Visibility = Visibility.Collapsed;
            Shell.CornerRadius = new CornerRadius(CompactOrbSize / 2);
        }
        else
        {
            Shell.Padding = new Thickness(12);
            CompactLeftColumn.Width = new GridLength(76);
            CompactCenterColumn.Width = new GridLength(1, GridUnitType.Star);
            CompactRightColumn.Width = new GridLength(28);
            Grid.SetColumn(MinimalGauge, 0);
            Grid.SetColumn(MinimalTextPanel, 1);
            MinimalTextPanel.Visibility = Visibility.Visible;
            MinimalZoomPanel.Visibility = Visibility.Visible;
            Shell.CornerRadius = new CornerRadius(22);
        }
    }

    private void SetOrbExpanded(bool expanded, bool? anchoredLeftOverride = null)
    {
        if (_detailed || !UseOrbCompact || _orbExpanded == expanded) return;
        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        var currentLeft = Left;
        var right = Left + currentWidth;
        if (expanded)
        {
            var area = GetCurrentWorkArea();
            _orbAnchoredLeft = anchoredLeftOverride ?? (currentLeft + currentWidth / 2 < area.Left + area.Width / 2);
        }
        _orbExpanded = expanded;
        ApplyCompactPresentation();
        Width = (expanded ? CompactCardWidth : CompactOrbSize) * _settings.Scale;
        Height = CompactOrbSize * _settings.Scale;
        Left = _orbAnchoredLeft ? currentLeft : right - Width;
        ApplyNativeWindowShape();
    }

    private void MainWindow_MouseEnter(object sender, MouseEventArgs e)
    {
        _compactHoverLeaveTimer.Stop();
        Opacity = 1;
        SetOrbExpanded(true);
    }

    private void MainWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_detailed && UseOrbCompact)
            _compactHoverLeaveTimer.Start();
        else
            Opacity = _restingOpacity;
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

    private void UpdateCompactStyleButton()
    {
        CompactStyleButton.Content = UseOrbCompact ? "Circle · text on hover" : "Full card";
    }

    private void CompactStyle_Click(object sender, RoutedEventArgs e)
    {
        _settings.CompactStyle = UseOrbCompact ? "Card" : "Orb";
        _settings.Save();
        UpdateCompactStyleButton();
        if (!_detailed)
        {
            var right = Left + (ActualWidth > 0 ? ActualWidth : Width);
            SetMinimalSize();
            Left = right - Width;
            SaveLocationIfReady();
        }
        e.Handled = true;
    }

    private void PositionAtEdgeIfNeeded(bool force = false)
    {
        var area = GetCurrentWorkArea();
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

    private Rect GetCurrentWorkArea()
    {
        var center = new Point(
            double.IsFinite(Left) ? Left + Math.Max(1, ActualWidth > 0 ? ActualWidth : Width) / 2 : 0,
            double.IsFinite(Top) ? Top + Math.Max(1, ActualHeight > 0 ? ActualHeight : Height) / 2 : 0);
        var source = PresentationSource.FromVisual(this);
        var screenPoint = IsVisible
            ? PointToScreen(new Point(Math.Max(1, ActualWidth) / 2, Math.Max(1, ActualHeight) / 2))
            : (source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity).Transform(center);
        var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)));
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        return new Rect(topLeft, bottomRight);
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

        if (WindowState == WindowState.Maximized)
        {
            _ = SetWindowRgn(window, IntPtr.Zero, true);
            return;
        }
        if (!GetWindowRect(window, out var rectangle)) return;
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        var dpiScale = Math.Max(1, GetDpiForWindow(window)) / 96d;
        var compactRadius = UseOrbCompact ? CompactOrbSize / 2 : 22;
        var radius = (int)Math.Round((_detailed ? 22 : compactRadius * _settings.Scale) * dpiScale);
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
        const int wmSysCommand = 0x0112;
        const int wmNcLeftButtonDoubleClick = 0x00A3;
        const int scMaximize = 0xF030;
        if (!_detailed && ((message == wmSysCommand && (wParam.ToInt64() & 0xFFF0) == scMaximize) || message == wmNcLeftButtonDoubleClick))
        {
            handled = true;
            return IntPtr.Zero;
        }
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
        menu.Items.Add("Show history", null, (_, _) => Dispatcher.Invoke(ShowHistoryPage));
        menu.Items.Add("Show compact", null, (_, _) => Dispatcher.Invoke(() => ShowMinimal(manual: true)));
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
        _compactHoverLeaveTimer.Stop();
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is not null) UnregisterHotKey(source.Handle, HotkeyId);
        _tray.Visible = false;
        _tray.Dispose();
        _firefoxCompanion.Dispose();
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
            ShowDetails(activate: false);
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
            CaptureSurfaceMenuPreview(TitleBar, System.IO.Path.Combine(folder, "detailed-titlebar-context-menu.png"), overlayOnWindow: true);

            (previewProcess?.Items.OfType<TreeViewItem>().FirstOrDefault() ?? previewProcess)?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "firefox-processes.png"));

            (previewTab ?? previewTabsSection)?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "firefox-tabs.png"));
            CaptureLargeProcessTextPreview(System.IO.Path.Combine(folder, "detailed-large-text.png"));
            CaptureHistoryPreview(System.IO.Path.Combine(folder, "history.png"));

            ShowMinimal(manual: true);
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "minimal.png"));
            CaptureSurfaceMenuPreview(MinimalView, System.IO.Path.Combine(folder, "compact-context-menu.png"), overlayOnWindow: false);
            CaptureCompactVariants(folder);
            CapturePressureTierPreviews(folder);
        }
        finally
        {
            ExitApp();
        }
    }

    private void CaptureSurfaceMenuPreview(FrameworkElement surface, string path, bool overlayOnWindow)
    {
        var menu = surface.ContextMenu;
        if (menu is null) return;

        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();

        var windowWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var windowHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var menuWidth = Math.Max(1, (int)Math.Ceiling(menu.ActualWidth));
        var menuHeight = Math.Max(1, (int)Math.Ceiling(menu.ActualHeight));
        var windowBitmap = RenderWindowBitmap();
        var menuBitmap = new RenderTargetBitmap(menuWidth, menuHeight, 96, 96, PixelFormats.Pbgra32);
        menuBitmap.Render(menu);

        const int gap = 6;
        var outputWidth = Math.Max(windowWidth, menuWidth);
        var outputHeight = overlayOnWindow ? windowHeight : windowHeight + gap + menuHeight;
        var menuLeft = outputWidth - menuWidth - (overlayOnWindow ? 12 : 0);
        var surfaceBottom = surface.TranslatePoint(new Point(0, surface.ActualHeight), this).Y;
        var menuTop = overlayOnWindow ? Math.Min(windowHeight - menuHeight - 12, surfaceBottom + gap) : windowHeight + gap;
        var composition = new DrawingVisual();
        using (var context = composition.RenderOpen())
        {
            context.DrawImage(windowBitmap, new Rect(0, 0, windowWidth, windowHeight));
            context.DrawImage(menuBitmap, new Rect(menuLeft, menuTop, menuWidth, menuHeight));
        }

        var output = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(composition);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private void CapturePressureTierPreviews(string folder)
    {
        var originalShowAt = _settings.ShowAtPercent;
        var originalHideBelow = _settings.HideBelowPercent;
        var stages = new[]
        {
            (Name: "tier-comfortable-55", Percent: 55d),
            (Name: "tier-rising-74", Percent: 74d),
            (Name: "tier-busy-82", Percent: 82d),
            (Name: "tier-critical-92", Percent: 92d)
        };
        const ulong physicalTotal = 32UL * 1024 * 1024 * 1024;
        const ulong commitLimit = 48UL * 1024 * 1024 * 1024;
        try
        {
            _settings.ShowAtPercent = 78;
            _settings.HideBelowPercent = 72;
            foreach (var stage in stages)
            {
                var physicalUsed = (ulong)(physicalTotal * stage.Percent / 100d);
                var commitUsed = (ulong)(commitLimit * stage.Percent / 100d);
                UpdateMemoryVisuals(new MemoryReading(
                    stage.Percent,
                    physicalUsed,
                    physicalTotal,
                    stage.Percent,
                    commitUsed,
                    commitLimit,
                    0,
                    0,
                    0));
                Opacity = 1;
                Shell.Opacity = _restingOpacity;
                UpdateLayout();
                SaveVisual(System.IO.Path.Combine(folder, $"{stage.Name}.png"));
            }
        }
        finally
        {
            Shell.Opacity = 1;
            _settings.ShowAtPercent = originalShowAt;
            _settings.HideBelowPercent = originalHideBelow;
        }
    }

    private void CaptureCompactVariants(string folder)
    {
        var originalStyle = _settings.CompactStyle;
        try
        {
            _settings.CompactStyle = "Orb";
            SetMinimalSize();
            SetOrbExpanded(true, anchoredLeftOverride: false);
            Opacity = 1;
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "compact-hover.png"));
            SaveVisual(System.IO.Path.Combine(folder, "compact-expand-left.png"));
            SetOrbExpanded(false);
            SetOrbExpanded(true, anchoredLeftOverride: true);
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "compact-expand-right.png"));
            SetOrbExpanded(false);

            _settings.CompactStyle = "Card";
            SetMinimalSize();
            Opacity = 1;
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "compact-card.png"));
        }
        finally
        {
            _settings.CompactStyle = originalStyle;
            SetMinimalSize();
            if (_reading is not null) UpdateMemoryVisuals(_reading);
            UpdateLayout();
        }
    }

    private void CaptureLargeProcessTextPreview(string path)
    {
        var originalSize = _settings.ProcessFontSize;
        ApplyProcessFontSize(17, persist: false);
        UpdateLayout();
        SaveVisual(path);
        ApplyProcessFontSize(originalSize, persist: false);
        UpdateLayout();
    }

    private void CaptureHistoryPreview(string path)
    {
        var now = DateTime.UtcNow;
        const ulong physicalTotal = 32UL * 1024 * 1024 * 1024;
        const ulong commitLimit = 48UL * 1024 * 1024 * 1024;
        var processes = new List<HistoryProcessSample>
        {
            new("Firefox", 649852, 3_780_000_000),
            new("Visual Studio Code", 512440, 1_260_000_000),
            new("Desktop Window Manager", 2280, 610_000_000)
        };
        var points = new List<MemoryHistoryPoint>();
        for (var index = 0; index <= 360; index++)
        {
            var hours = index / 60d;
            var basePressure = 54 + 3 * Math.Sin(index / 5d);
            var firstSpike = 30 * Math.Exp(-Math.Pow((hours - 2.1) / 0.20, 2));
            var criticalSpike = 43 * Math.Exp(-Math.Pow((hours - 4.65) / 0.24, 2));
            var physical = Math.Clamp(basePressure + firstSpike + criticalSpike, 0, 96);
            var commit = Math.Clamp(physical + 3 + 2 * Math.Sin(index / 7d), 0, 98);
            var classification = index == 126 ? "Spike" : index == 279 ? "Critical" : index == 294 ? "Recovered" : "Normal";
            points.Add(new MemoryHistoryPoint
            {
                TimestampUtc = now - TimeSpan.FromHours(6) + TimeSpan.FromMinutes(index),
                PhysicalPercent = physical,
                CommitPercent = commit,
                PhysicalUsedBytes = (ulong)(physicalTotal * physical / 100),
                PhysicalTotalBytes = physicalTotal,
                CommitUsedBytes = (ulong)(commitLimit * commit / 100),
                CommitLimitBytes = commitLimit,
                Classification = classification,
                TopProcesses = processes
            });
        }
        var markers = new List<SystemHistoryMarker>
        {
            new(now - TimeSpan.FromHours(1.35), "Resource exhaustion", "Windows recorded low virtual memory or resource exhaustion", "Physical 94.2% · Commit 99.1% · recorded leaders: firefox.exe, Code.exe", 94.2, 99.1)
        };

        ShowHistoryPage();
        HistoryChart.SetRange(TimeSpan.FromHours(6));
        HistoryChart.SetData(points, markers);
        HistoryCurrentText.Text = $"{points[^1].PressurePercent:0}%";
        HistoryPeakText.Text = $"{points.Max(point => point.PressurePercent):0}%";
        HistorySpikeText.Text = "4";
        HistorySinceText.Text = "Since 6 hours ago";
        HistoryRangeText.Text = "Live · 6 hours";
        UpdateLayout();
        SaveVisual(path);
        HistoryChart.SetPreviewHover(0.77, 0.58);
        UpdateLayout();
        SaveVisual(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "history-hover.png"));
    }

    private void SaveVisual(string path)
    {
        var bitmap = RenderWindowBitmap();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private RenderTargetBitmap RenderWindowBitmap()
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var source = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        source.Render(this);
        var radius = _detailed ? 22 : UseOrbCompact ? Math.Min(width, height) / 2d : 22 * _settings.Scale;
        var clipped = new DrawingVisual();
        using (var context = clipped.RenderOpen())
        {
            context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), radius, radius));
            context.DrawImage(source, new Rect(0, 0, width, height));
            context.Pop();
        }
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(clipped);
        return result;
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
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();
    private void PageSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_expandedPage == ExpandedPage.Processes) ShowHistoryPage();
        else ShowProcessPage();
        e.Handled = true;
    }
    private void ShowMinimal_Click(object sender, RoutedEventArgs e) => ShowMinimal(manual: true);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideUntilPressure();
    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ChangeScale(e.Delta > 0 ? 0.10 : -0.10); }
    private void History1Hour_Click(object sender, RoutedEventArgs e) => HistoryChart.SetRange(TimeSpan.FromHours(1));
    private void History6Hours_Click(object sender, RoutedEventArgs e) => HistoryChart.SetRange(TimeSpan.FromHours(6));
    private void History24Hours_Click(object sender, RoutedEventArgs e) => HistoryChart.SetRange(TimeSpan.FromHours(24));
    private void History7Days_Click(object sender, RoutedEventArgs e) => HistoryChart.SetRange(TimeSpan.FromDays(7));
    private void HistoryAll_Click(object sender, RoutedEventArgs e) => HistoryChart.SetRange(null);
    private void HistoryLive_Click(object sender, RoutedEventArgs e) => HistoryChart.FollowLive();

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.ShowAtPercent = Math.Round(e.NewValue);
        _settings.HideBelowPercent = Math.Max(60, _settings.ShowAtPercent - 6);
        ThresholdText.Text = $"{_settings.ShowAtPercent:0}%";
        _settings.Save();
    }

    private void ProcessFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingProcessFontSize) return;
        ApplyProcessFontSize(e.NewValue, persist: true);
    }

    private void ApplyProcessFontSize(double value, bool persist)
    {
        var size = Math.Clamp(Math.Round(value), 10, 18);
        _settings.ProcessFontSize = size;
        ProcessTree.FontSize = size;
        ProcessFontSizeText.Text = $"{size:0} pt";
        if (Math.Abs(ProcessFontSizeSlider.Value - size) > 0.01)
        {
            _updatingProcessFontSize = true;
            ProcessFontSizeSlider.Value = size;
            _updatingProcessFontSize = false;
        }
        if (_latestGroups.Count > 0) PopulateTree(_latestGroups, _latestFirefoxTabs);
        if (persist) _settings.Save();
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
