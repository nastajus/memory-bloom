using System;
using System.Runtime.InteropServices;

namespace MemoryBloom;

// Windows exposes pinning through the shell's internal virtual-desktop service.
// The call is best-effort because Microsoft does not publish this interface as a stable desktop API.
internal static class VirtualDesktopPinning
{
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid PinnedAppsService = new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");

    public static bool TryPinWindow(IntPtr window, out string? error)
    {
        error = null;
        object? shellObject = null;
        object? viewsObject = null;
        object? pinnedObject = null;
        IApplicationView? view = null;
        try
        {
            var shellType = Type.GetTypeFromCLSID(ImmersiveShellClass, throwOnError: true)!;
            shellObject = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows shell service was unavailable.");
            var shell = (IServiceProvider)shellObject;

            var viewsService = typeof(IApplicationViewCollection).GUID;
            viewsObject = shell.QueryService(ref viewsService, ref viewsService);
            var views = (IApplicationViewCollection)viewsObject;
            var result = views.GetViewForHwnd(window, out view);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            if (view is null) throw new InvalidOperationException("Windows did not register the Memory Bloom window for virtual desktops.");

            var pinnedInterface = typeof(IVirtualDesktopPinnedApps).GUID;
            var pinnedService = PinnedAppsService;
            pinnedObject = shell.QueryService(ref pinnedService, ref pinnedInterface);
            var pinned = (IVirtualDesktopPinnedApps)pinnedObject;
            if (!pinned.IsViewPinned(view)) pinned.PinView(view);
            return pinned.IsViewPinned(view);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            Release(view);
            Release(pinnedObject);
            Release(viewsObject);
            Release(shellObject);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid interfaceId);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
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

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513")]
    private interface IApplicationView
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
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
