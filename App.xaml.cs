using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace MemoryBloom;

public partial class App : System.Windows.Application
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("MemoryBloom.App");
        _singleInstance = new Mutex(true, "Local\\MemoryBloom.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        var previewIndex = Array.FindIndex(e.Args, value => value.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        var previewFolder = previewIndex >= 0 && previewIndex + 1 < e.Args.Length ? e.Args[previewIndex + 1] : null;
        window.InitializeInBackground(e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase) || previewFolder is not null, previewFolder);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
