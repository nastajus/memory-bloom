param([switch]$UnpinForRecoveryTest)

$ErrorActionPreference = 'Stop'

$process = Get-Process -Name MemoryBloom -ErrorAction Stop |
    Where-Object { $_.Threads.Count -gt 0 } |
    Select-Object -First 1
if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
    throw 'Memory Bloom has no visible native window to query.'
}

if (-not ('MemoryBloom.VirtualDesktopPinProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace MemoryBloom;

public static class VirtualDesktopPinProbe
{
    private const string AppUserModelId = "MemoryBloom.App";
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid PinnedAppsService = new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");

    public static string Read(IntPtr window)
    {
        object shellObject = null;
        object viewsObject = null;
        object pinnedObject = null;
        IApplicationView view = null;
        try
        {
            var shellType = Type.GetTypeFromCLSID(ImmersiveShellClass, throwOnError: true);
            shellObject = Activator.CreateInstance(shellType);
            var shell = (IServiceProvider)shellObject;

            var viewsService = typeof(IApplicationViewCollection).GUID;
            viewsObject = shell.QueryService(ref viewsService, ref viewsService);
            var views = (IApplicationViewCollection)viewsObject;
            var result = views.GetViewForHwnd(window, out view);
            if (result < 0) Marshal.ThrowExceptionForHR(result);

            var pinnedInterface = typeof(IVirtualDesktopPinnedApps).GUID;
            var pinnedService = PinnedAppsService;
            pinnedObject = shell.QueryService(ref pinnedService, ref pinnedInterface);
            var pinned = (IVirtualDesktopPinnedApps)pinnedObject;
            return $"ViewPinned={pinned.IsViewPinned(view)} AppPinned={pinned.IsAppIdPinned(AppUserModelId)}";
        }
        finally
        {
            Release(view);
            Release(pinnedObject);
            Release(viewsObject);
            Release(shellObject);
        }
    }

    public static string UnpinForTest(IntPtr window)
    {
        object shellObject = null;
        object viewsObject = null;
        object pinnedObject = null;
        IApplicationView view = null;
        try
        {
            var shellType = Type.GetTypeFromCLSID(ImmersiveShellClass, throwOnError: true);
            shellObject = Activator.CreateInstance(shellType);
            var shell = (IServiceProvider)shellObject;
            var viewsService = typeof(IApplicationViewCollection).GUID;
            viewsObject = shell.QueryService(ref viewsService, ref viewsService);
            var views = (IApplicationViewCollection)viewsObject;
            var result = views.GetViewForHwnd(window, out view);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            var pinnedInterface = typeof(IVirtualDesktopPinnedApps).GUID;
            var pinnedService = PinnedAppsService;
            pinnedObject = shell.QueryService(ref pinnedService, ref pinnedInterface);
            var pinned = (IVirtualDesktopPinnedApps)pinnedObject;
            if (pinned.IsViewPinned(view)) pinned.UnpinView(view);
            if (pinned.IsAppIdPinned(AppUserModelId)) pinned.UnpinAppID(AppUserModelId);
            return $"ViewPinned={pinned.IsViewPinned(view)} AppPinned={pinned.IsAppIdPinned(AppUserModelId)}";
        }
        finally
        {
            Release(view);
            Release(pinnedObject);
            Release(viewsObject);
            Release(shellObject);
        }
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid interfaceId);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
    private interface IApplicationViewCollection
    {
        int GetViews(out object array);
        int GetViewsByZOrder(out object array);
        int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out object array);
        [PreserveSig] int GetViewForHwnd(IntPtr window, out IApplicationView view);
        int GetViewForApplication(object application, out IApplicationView view);
        int GetViewForAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IApplicationView view);
        int GetViewInFocus(out IntPtr view);
        int Unknown1(out IntPtr view);
        void RefreshCollection();
        int RegisterForApplicationViewChanges(object listener, out int cookie);
        int UnregisterForApplicationViewChanges(int cookie);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513")]
    private interface IApplicationView { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
    private interface IVirtualDesktopPinnedApps
    {
        [return: MarshalAs(UnmanagedType.Bool)] bool IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [return: MarshalAs(UnmanagedType.Bool)] bool IsViewPinned(IApplicationView view);
        void PinView(IApplicationView view);
        void UnpinView(IApplicationView view);
    }
}
'@
}

$state = if ($UnpinForRecoveryTest) {
    [MemoryBloom.VirtualDesktopPinProbe]::UnpinForTest($process.MainWindowHandle)
} else {
    [MemoryBloom.VirtualDesktopPinProbe]::Read($process.MainWindowHandle)
}
Write-Output "PID=$($process.Id) Handle=$($process.MainWindowHandle) $state"
