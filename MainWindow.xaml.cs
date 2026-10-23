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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Forms.NotifyIcon _tray;
    private readonly Dictionary<string, bool> _groupExpansionState = new(StringComparer.OrdinalIgnoreCase);
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
    private Point _windowStart;

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
        SourceInitialized += (_, _) => RegisterShortcut();
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

        if (!_detailed)
            Opacity = 0.40 + intensity * 0.58;
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
            PopulateTree(groups, result.Tabs);
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
                        if (grandchild.Tag is FirefoxTabSection) _tabsSectionExpanded = grandchild.IsExpanded;
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
                        : "Total private committed memory for the processes in this app group."),
                Tag = group,
                IsExpanded = _groupExpansionState.TryGetValue(group.Name, out var wasExpanded) ? wasExpanded : group.IsFirefox || group.IsSystemEstimate
            };
            if (group.IsFirefox)
            {
                var webContent = group.Processes.Where(process => process.Role.StartsWith("web content", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var process in group.Processes.Except(webContent)) root.Items.Add(MakeProcessTreeItem(process, true));
                var contentSection = new TreeViewItem
                {
                    Header = MakeRow("Web content", $"{webContent.Count} Firefox content processes", FormatBytes(webContent.Sum(process => process.PrivateBytes)), true,
                        "Firefox may host multiple tabs in one content process and may move a tab after navigation or isolation changes. Expand for the separate title snapshot and the current Windows content PIDs."),
                    Tag = new FirefoxContentSection("Web content"),
                    IsExpanded = _firefoxContentExpanded
                };
                contentSection.Items.Add(MakeFirefoxTabsSection(group, firefoxTabs));
                foreach (var process in webContent) contentSection.Items.Add(MakeProcessTreeItem(process, true));
                root.Items.Add(contentSection);
            }
            else
            {
                foreach (var process in group.Processes) root.Items.Add(MakeProcessTreeItem(process, false));
            }
            ProcessTree.Items.Add(root);
        }
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private TreeViewItem MakeProcessTreeItem(ProcessSnapshot process, bool firefox)
    {
        var descriptor = process.Role;
        if (!string.IsNullOrWhiteSpace(process.WindowTitle) && process.Role == process.WindowTitle)
            descriptor = Trim(process.WindowTitle, 42);
        var child = new TreeViewItem
        {
            Header = MakeRow(
                process.IsSynthetic ? process.DisplayName : $"PID {process.Pid}",
                descriptor,
                FormatBytes(process.PrivateBytes),
                firefox,
                process.IsSynthetic
                    ? process.Tooltip
                    : $"Private committed memory: {FormatBytes(process.PrivateBytes)}. Current physical working set: {FormatBytes(process.WorkingSetBytes)}. {process.Role}"),
            Tag = process,
            ToolTip = process.Tooltip
        };
        if (!process.IsSynthetic) child.ContextMenu = MakeProcessMenu(process);
        return child;
    }

    private TreeViewItem MakeFirefoxTabsSection(ProcessGroup firefoxGroup, FirefoxTabsSnapshot snapshot)
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
            ? "unavailable"
            : $"snapshot {FormatAge(sourceAge)} old{(activeTitleMatches ? "" : " · active title differs")}";
        var section = new TreeViewItem
        {
            Header = MakeRow("Known tabs", freshness, $"{snapshot.Tabs.Count}", true,
                "Tab titles and last-interaction times read from Firefox's local recovery snapshot. They are intentionally separate because Firefox does not expose a stable tab-to-Windows-PID mapping. A differing active title or an old snapshot is flagged here."),
            Tag = new FirefoxTabSection("Known tabs"),
            IsExpanded = _tabsSectionExpanded
        };
        if (snapshot.Error is not null)
        {
            section.Items.Add(new TreeViewItem { Header = MakeRow("Unavailable", snapshot.Error, "", false, snapshot.Error), IsEnabled = false });
            return section;
        }
        foreach (var tab in snapshot.Tabs.Take(250))
        {
            var age = DateTime.UtcNow - tab.LastAccessedUtc;
            var subtitle = $"last used {FormatAge(age)} ago{(tab.Hidden ? " · hidden" : "")}";
            section.Items.Add(new TreeViewItem
            {
                Header = MakeRow(Trim(tab.Title, 36), subtitle, "", true, $"{tab.Title}\n{tab.Url}\nLast interacted: {tab.LastAccessedUtc.ToLocalTime():g}"),
                Tag = tab
            });
        }
        if (snapshot.Tabs.Count > 250)
            section.Items.Add(new TreeViewItem { Header = MakeRow("More tabs not shown", $"{snapshot.Tabs.Count - 250} additional titles", "", false, "The display is capped at 250 titles to keep the process tree responsive."), IsEnabled = false });
        return section;
    }

    private static Grid MakeRow(string title, string subtitle, string memory, bool accent, string tooltip)
    {
        var grid = new Grid { MinWidth = 565, Margin = new Thickness(2, 2, 3, 2) };
        grid.ToolTip = tooltip;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        var a = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = accent ? new SolidColorBrush(Color.FromRgb(117, 214, 178)) : Brushes.WhiteSmoke };
        var b = new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(168, 182, 186)), TextTrimming = TextTrimming.CharacterEllipsis };
        var c = new TextBlock { Text = memory, Foreground = Brushes.WhiteSmoke, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(a, 0); Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        grid.Children.Add(a); grid.Children.Add(b); grid.Children.Add(c);
        return grid;
    }

    private void ShowDetails()
    {
        _manualOpen = true;
        _detailed = true;
        MinimalView.Visibility = Visibility.Collapsed;
        DetailedView.Visibility = Visibility.Visible;
        Shell.LayoutTransform = Transform.Identity;
        Topmost = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 650;
        MinHeight = 430;
        Width = Math.Max(780, Width);
        Height = Math.Max(600, Height);
        Opacity = 0.98;
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
        ResizeMode = ResizeMode.NoResize;
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
        _windowStart = new Point(Left, Top);
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
        Left = _windowStart.X + deltaX;
        Top = _windowStart.Y + deltaY;
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
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            using var outer = new Drawing.Pen(Drawing.Color.FromArgb(110, 117, 214, 178), 4);
            using var inner = new Drawing.SolidBrush(Drawing.Color.FromArgb(235, 36, 50, 53));
            graphics.FillEllipse(inner, 3, 3, 26, 26);
            graphics.DrawArc(outer, 5, 5, 22, 22, -90, 285);
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 117, 214, 178));
            graphics.FillEllipse(dot, 12, 12, 8, 8);
        }
        var handle = bitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private void Cleanup()
    {
        _timer.Stop();
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
            ShowDetails();
            while (_processRefreshRunning)
                await Task.Delay(75);
            if (ProcessTree.Items.Count == 0)
                await RefreshProcessesAsync();
            TreeViewItem? previewTabsSection = null;
            var firefoxRoot = ProcessTree.Items.OfType<TreeViewItem>().FirstOrDefault(item => (item.Tag as ProcessGroup)?.IsFirefox == true);
            var contentSection = firefoxRoot?.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is FirefoxContentSection);
            if (contentSection is not null)
            {
                contentSection.IsExpanded = true;
                var tabsSection = contentSection.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is FirefoxTabSection);
                if (tabsSection is not null)
                {
                    tabsSection.IsExpanded = true;
                    previewTabsSection = tabsSection;
                }
            }
            UpdateLayout();
            previewTabsSection?.BringIntoView();
            UpdateLayout();
            SaveVisual(System.IO.Path.Combine(folder, "detailed.png"));

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
