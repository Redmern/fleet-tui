using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// Gives Windows Terminal windows and a shortcut a shared AppUserModelID, so the taskbar groups
// those windows under the pinned shortcut (and its icon) instead of under Windows Terminal.
public static class FleetTaskbar
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Vt; public ushort R1, R2, R3; public IntPtr P; public IntPtr P2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    static readonly Guid AppUserModelFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    const uint AppUserModelIdProperty = 5;

    public static List<long> TerminalWindows()
    {
        var result = new List<long>();
        EnumWindows((hwnd, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "CASCADIA_HOSTING_WINDOW_CLASS" && IsWindowVisible(hwnd))
                result.Add(hwnd.ToInt64());
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static void SetWindowAppId(long hwnd, string appId)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(new IntPtr(hwnd), ref iid, out var store));
        try { SetString(store, appId); }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static void SetShortcutAppId(string lnkPath, string appId)
    {
        var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try
        {
            ((IPersistFile)link).Load(lnkPath, 2); // STGM_READWRITE
            SetString((IPropertyStore)link, appId);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    static void SetString(IPropertyStore store, string value)
    {
        var key = new PropertyKey { FormatId = AppUserModelFormat, PropertyId = AppUserModelIdProperty };
        var pv = new PropVariant { Vt = 31, P = Marshal.StringToCoTaskMemUni(value) }; // VT_LPWSTR
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref pv));
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.FreeCoTaskMem(pv.P); }
    }
}
