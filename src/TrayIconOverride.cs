using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Only modifies the existing official item. Never ADD/DELETE or change callbacks.
internal static class TrayIconOverride {
    // Observed in 26.908.4834.0; restricted to the production package and its host.
    private static readonly Guid ProductionGuid = new Guid("e5768d8b-6936-4f45-b1ad-4c5fb414cb35");
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Identifier {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Guid;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    private static IntPtr FindHost(string appId, out uint processId) {
        IntPtr host = IntPtr.Zero;
        uint owner = 0;
        EnumWindows((window, parameter) => {
            StringBuilder name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "OwlElectron_NotifyIconHostWindow") return true;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            try {
                if (WindowTaskbar.ProcessAppId(pid) != appId) return true;
                host = window; owner = pid; return false;
            }
            catch { return true; }
        }, IntPtr.Zero);
        processId = owner;
        return host;
    }

    internal static string Inspect(string appId) {
        uint pid;
        IntPtr host = FindHost(appId, out pid);
        Identifier id = new Identifier { Size = (uint)Marshal.SizeOf(typeof(Identifier)), Guid = ProductionGuid };
        Rect rect;
        int hr = Shell_NotifyIconGetRect(ref id, out rect);
        return "Tray (read-only): knownProductionGuid=" + ProductionGuid + " host=0x" + host.ToInt64().ToString("X") +
            " pid=" + pid + " getRectHr=0x" + hr.ToString("X8") + " rect=" +
            rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom + "; visualStatus=UNVERIFIED";
    }

    internal static int Apply(string appId, string executable) {
        if (!appId.StartsWith(AppDiscovery.DefaultFamily + "!", StringComparison.Ordinal)) {
            LauncherLog.Write("INFO", "tray.skipped", "No known tray GUID for this package");
            return 0;
        }
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try {
            if (ExtractIconEx(executable, 0, out large, out small, 1) == 0 ||
                (large == IntPtr.Zero && small == IntPtr.Zero))
                throw new InvalidOperationException("Cannot extract the embedded Codex icon");
            Stopwatch clock = Stopwatch.StartNew();
            // A cold start may create its tray after activation returns. Stop at the
            // first successful modification; no helper, listener or subsequent refresh.
            do {
                uint pid;
                IntPtr host = FindHost(appId, out pid);
                if (host != IntPtr.Zero) {
                    Identifier id = new Identifier {
                        Size = (uint)Marshal.SizeOf(typeof(Identifier)), Guid = ProductionGuid
                    };
                    Rect rect;
                    if (Shell_NotifyIconGetRect(ref id, out rect) >= 0) {
                        NotifyData data = new NotifyData {
                            Size = (uint)Marshal.SizeOf(typeof(NotifyData)), Window = host,
                            Guid = ProductionGuid, Flags = 0x26 /* NIF_GUID | NIF_ICON | NIF_TIP */,
                            Tip = "Codex",
                            Icon = large != IntPtr.Zero ? large : small
                        };
                        if (Shell_NotifyIcon(1 /* NIM_MODIFY */, ref data)) {
                            LauncherLog.Write("INFO", "tray.applied", "pid=" + pid +
                                " elapsedMs=" + clock.ElapsedMilliseconds +
                                "; tooltip=Codex; one-shot; visualStatus=UNVERIFIED");
                            return 1;
                        }
                    }
                }
                if (clock.ElapsedMilliseconds >= 4000) break;
                Thread.Sleep(100);
            } while (true);
            LauncherLog.Write("WARN", "tray.skipped", "No tray modification succeeded within 4 seconds");
            return 0;
        }
        finally {
            if (small != IntPtr.Zero) DestroyIcon(small);
            if (large != IntPtr.Zero) DestroyIcon(large);
        }
    }
}
