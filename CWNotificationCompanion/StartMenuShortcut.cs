using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace CWNotificationCompanion;

/// <summary>
/// Creates the Start Menu shortcut that carries the app's AppUserModelID.
/// Windows will not display a toast from an unpackaged desktop app unless a
/// shortcut with a matching AUMID exists in the Start Menu — see
/// https://learn.microsoft.com/windows/win32/shell/enable-desktop-toast-with-appusermodelid
/// </summary>
internal static class StartMenuShortcut
{
    // PKEY_AppUserModel_ID — the shortcut property that holds the AUMID.
    private static PropertyKey AppUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    private const ushort VT_LPWSTR = 31;

    /// <summary>
    /// Ensures a Start Menu shortcut exists whose target is the current exe and
    /// whose AppUserModelID matches <paramref name="appId"/>. Recreated only when
    /// missing or pointing at a stale target, so it is cheap to call at startup.
    /// </summary>
    public static void EnsureExists(string appId, string shortcutName)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Error("StartMenuShortcut: could not resolve current exe path.");
                return;
            }

            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var shortcutPath = Path.Combine(startMenu, shortcutName + ".lnk");

            if (File.Exists(shortcutPath) && TargetMatches(shortcutPath, exePath))
                return;

            Create(shortcutPath, exePath, appId);
            Logger.Info($"StartMenuShortcut: created/updated '{shortcutPath}' (AUMID={appId}).");
        }
        catch (Exception ex)
        {
            Logger.Error("StartMenuShortcut: failed to create shortcut", ex);
        }
    }

    private static bool TargetMatches(string shortcutPath, string exePath)
    {
        try
        {
            var link = (IShellLinkW)new CShellLink();
            ((ComTypes.IPersistFile)link).Load(shortcutPath, 0);
            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return string.Equals(sb.ToString(), exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // unreadable → recreate
        }
    }

    private static void Create(string shortcutPath, string exePath, string appId)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(exePath);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
        link.SetIconLocation(exePath, 0);

        var store = (IPropertyStore)link;
        var value = Marshal.StringToCoTaskMemUni(appId);
        var pv = new PropVariant { vt = VT_LPWSTR, pointerValue = value };
        try
        {
            store.SetValue(ref AppUserModelIdKey, ref pv);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }

        ((ComTypes.IPersistFile)link).Save(shortcutPath, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport,
     Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport,
     Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }
}
