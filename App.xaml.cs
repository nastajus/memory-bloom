using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace MemoryBloom;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\MemoryBloom.SingleInstance";
    private const string ShowExistingEventName = "Local\\MemoryBloom.ShowExisting";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private Mutex? _singleInstance;
    private EventWaitHandle? _showExistingEvent;
    private RegisteredWaitHandle? _showExistingRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("MemoryBloom.App");
        var previewIndex = Array.FindIndex(e.Args, value => value.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        var previewFolder = previewIndex >= 0 && previewIndex + 1 < e.Args.Length ? e.Args[previewIndex + 1] : null;
        if (previewFolder is null)
        {
            _showExistingEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowExistingEventName);
            _singleInstance = new Mutex(true, SingleInstanceMutexName, out var created);
            if (!created)
            {
                _showExistingEvent.Set();
                _showExistingEvent.Dispose();
                _showExistingEvent = null;
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (_showExistingEvent is not null)
        {
            _showExistingRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showExistingEvent,
                (_, timedOut) =>
                {
                    if (timedOut) return;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (MainWindow is MemoryBloom.MainWindow existingWindow)
                            existingWindow.ShowFromExternalLaunch();
                    });
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        var showPassive = e.Args.Contains("--show-passive", StringComparer.OrdinalIgnoreCase);
        window.InitializeInBackground(
            e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase) || showPassive || previewFolder is not null,
            previewFolder,
            activateInitial: !showPassive && previewFolder is null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showExistingRegistration?.Unregister(null);
        _showExistingEvent?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
