using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey {
    public Guid FormatId;
    public uint PropertyId;
}

// PROPVARIANT must include the full union (including CA* on 64-bit Windows).
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant {
    public ushort Type;
    public ushort Reserved1, Reserved2, Reserved3;
    public IntPtr Value;
    public IntPtr Padding;
}

// Windows-defined COM interface ID; independent of the installed application's ID.
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore {
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[Flags]
internal enum ActivateOptions {
    NoErrorUI = 2
}

// Fixed Windows activation interface ID. The target AppUserModelID is discovered at runtime.
[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager {
    [PreserveSig]
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        ActivateOptions options,
        out uint processId);
    [PreserveSig] int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    [PreserveSig] int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

internal static class Program {
    private static string DataDirectory {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLauncher"); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
    // Windows property key naming the AppUserModelID field, not the field's value.
    private static PropertyKey AppIdKey = new PropertyKey {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHGetPropertyStoreFromParsingName(
        string path, IntPtr bindContext, uint flags, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CoCreateInstance(ref Guid classId, IntPtr outer, uint context,
        ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IApplicationActivationManager manager);

    [DllImport("ole32.dll")]
    private static extern int CoAllowSetForegroundWindow(
        [MarshalAs(UnmanagedType.IUnknown)] object manager, IntPtr reserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

    private static IPropertyStore OpenProperties(string path, bool writable) {
        Guid iid = typeof(IPropertyStore).GUID;
        IPropertyStore store;
        SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, writable ? 2u : 0u, ref iid, out store);
        return store;
    }

    private static string ReadAppId(string path) {
        IPropertyStore store = OpenProperties(path, false);
        PropVariant value = new PropVariant();
        try {
            store.GetValue(ref AppIdKey, out value);
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally {
            PropVariantClear(ref value);
            Marshal.FinalReleaseComObject(store);
        }
    }

    private static void WriteAppId(string path, string appId) {
        IPropertyStore store = OpenProperties(path, true);
        PropVariant value = new PropVariant();
        try {
            value.Type = 31; // VT_LPWSTR
            value.Value = Marshal.StringToCoTaskMemUni(appId);
            store.SetValue(ref AppIdKey, ref value);
            store.Commit();
        }
        finally {
            PropVariantClear(ref value);
            Marshal.FinalReleaseComObject(store);
        }
    }

    private static bool ShortcutIsCurrent(string shortcutPath, string executablePath, string appId) {
        if (!File.Exists(shortcutPath)) return false;
        dynamic shell = null;
        dynamic shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            shortcut = shell.CreateShortcut(shortcutPath);
            return String.Equals((string)shortcut.TargetPath, executablePath, StringComparison.OrdinalIgnoreCase) &&
                String.Equals((string)shortcut.IconLocation, executablePath + ",0", StringComparison.OrdinalIgnoreCase) &&
                String.Equals((string)shortcut.WorkingDirectory, Path.GetDirectoryName(executablePath), StringComparison.OrdinalIgnoreCase) &&
                String.IsNullOrEmpty((string)shortcut.Arguments) && ReadAppId(shortcutPath) == appId;
        }
        finally {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void UpdateShortcut(string shortcutPath, string executablePath, string appId) {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        dynamic shell = null;
        dynamic shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
            shortcut.Arguments = "";
            shortcut.IconLocation = executablePath + ",0";
            shortcut.Description = "Open Codex";
            shortcut.Save();
        }
        finally {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
        // The launcher exits after activating a different process. Windows needs
        // the window's taskbar identity on the shortcut to associate it with that window.
        WriteAppId(shortcutPath, appId);
        if (!ShortcutIsCurrent(shortcutPath, executablePath, appId))
            throw new IOException("Shortcut verification failed: " + shortcutPath);
        SHChangeNotify(0x00002000, 0x0005, shortcutPath, IntPtr.Zero); // UPDATEITEM, PATHW
    }

    [STAThread]
    private static int Main(string[] args) {
        bool createShortcut = args.Length == 2 && args[0] == "--create-shortcut" && (args[1] == "desktop" || args[1] == "start-menu");
        bool diagnose = args.Length == 1 && args[0] == "--diagnose";
        if (args.Length != 0 && !createShortcut && !diagnose) return 2;
        try {
            if (diagnose) return Diagnose();
            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (createShortcut) return RunBounded(() => CreateShortcut(executablePath, args[1]), 5000, "Shortcut creation");
            return Run(executablePath);
        }
        catch (Exception error) {
            LogShortcutError(error);
            if (!createShortcut && !diagnose)
                MessageBox(IntPtr.Zero, error.Message + "\n\nDetails: " + Path.Combine(DataDirectory, "shortcut-errors.log"), "Codex Launcher", 0x10);
            return 1;
        }
    }

    private static int Run(string executablePath) {
        string installDirectory = Path.GetDirectoryName(executablePath);
        // Resolve the registered activation identity independently of taskbar grouping.
        string appId = AppDiscovery.Resolve(installDirectory);

        int result = RunBounded(() => Activate(appId), 20000, "Application activation");
        if (result < 0) {
            // Refresh registration after a concurrent Store update and retry once.
            LogShortcutError(new COMException("Activation failed; retrying after package discovery.", result));
            appId = AppDiscovery.Resolve(installDirectory);
            result = RunBounded(() => Activate(appId), 20000, "Application activation retry");
        }
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        // The visible window belongs to the packaged app, not this executable.
        // Apply taskbar metadata independently of optional shortcut creation.
        try {
            RunBounded(() => WindowTaskbar.Apply(appId, executablePath),
                6000, "Taskbar icon");
        }
        catch (Exception error) { LogShortcutError(error); }
        return 0;
    }

    // A stuck Shell/COM call must not keep this launcher alive indefinitely.
    // COM objects are created, used and released on the same STA worker.
    internal static int RunBounded(Func<int> operation, int milliseconds, string stage) {
        int result = 0;
        Exception failure = null;
        Thread worker = new Thread(() => {
            try { result = operation(); }
            catch (Exception error) { failure = error; }
        });
        worker.IsBackground = true;
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(milliseconds))
            throw new TimeoutException(stage + " did not finish within " + milliseconds / 1000 +
                " seconds. Windows may still be processing the request. Try opening the app from Windows Search.");
        if (failure != null) throw new InvalidOperationException(stage + " failed: " + failure.Message, failure);
        return result;
    }

    private static int Activate(string appId) {
        Guid classId = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
        Guid interfaceId = typeof(IApplicationActivationManager).GUID;
        IApplicationActivationManager manager;
        // Microsoft requires an out-of-process manager for short-lived launchers,
        // so activation arguments survive after this executable exits.
        CoCreateInstance(ref classId, IntPtr.Zero, 4 /* CLSCTX_LOCAL_SERVER */, ref interfaceId, out manager);
        try {
            CoAllowSetForegroundWindow(manager, IntPtr.Zero);
            uint processId;
            return manager.ActivateApplication(appId, null, ActivateOptions.NoErrorUI, out processId);
        }
        finally { Marshal.FinalReleaseComObject(manager); }
    }

    private static string[] ShortcutPaths() {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string start = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return new string[] {
            String.IsNullOrEmpty(desktop) ? null : Path.Combine(desktop, "Codex.lnk"),
            String.IsNullOrEmpty(start) ? null : Path.Combine(start, "Programs", "Codex.lnk")
        };
    }

    private static int CreateShortcut(string executablePath, string location) {
        string appId = AppDiscovery.Resolve(Path.GetDirectoryName(executablePath));
        string path = ShortcutPaths()[location == "desktop" ? 0 : 1];
        if (path == null) throw new IOException("The selected Windows folder is unavailable.");
        CreateShortcutAt(path, executablePath, WindowTaskbar.TaskbarAppId(appId));
        return 0;
    }

    internal static void CreateShortcutAt(string path, string executablePath, string appId) {
        if (File.Exists(path)) throw new IOException("A shortcut already exists: " + path + ". Delete it first if you want to create a new one.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = Path.Combine(Path.GetDirectoryName(path), "Codex-" + Guid.NewGuid().ToString("N") + ".lnk");
        try {
            UpdateShortcut(temporary, executablePath, appId);
            // Publish without overwriting an existing shortcut, including concurrent creation.
            File.Move(temporary, path);
            SHChangeNotify(0x00002000, 0x0005, path, IntPtr.Zero);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static int Diagnose() {
        string executablePath = Assembly.GetExecutingAssembly().Location;
        StringBuilder report = new StringBuilder();
        report.AppendLine("Codex Launcher diagnostics " + DateTime.UtcNow.ToString("o"));
        report.AppendLine("Executable: " + executablePath);
        report.AppendLine("64-bit process: " + Environment.Is64BitProcess);
        report.AppendLine("Icon: embedded in executable");
        int result = 0;
        try {
            string directory = Path.GetDirectoryName(executablePath);
            string requested = AppDiscovery.ReadOverride(directory);
            string family = requested == null ? AppDiscovery.DefaultFamily : requested.Split('!')[0];
            report.AppendLine("Override: " + (requested ?? "(none)"));
            string[] ids = AppDiscovery.QueryApplicationIds(family);
            report.AppendLine("Registered application IDs: " + String.Join(", ", ids));
            string selected = AppDiscovery.SelectApplication(ids, family, requested);
            report.AppendLine("Selected AppUserModelID: " + selected);
            report.AppendLine("Taskbar AppUserModelID: " + WindowTaskbar.TaskbarAppId(selected));
        }
        catch (Exception error) { report.AppendLine("Discovery ERROR: " + error.Message); result = 1; }
        foreach (string path in ShortcutPaths()) {
            report.AppendLine("Shortcut: " + (path ?? "(folder unavailable)"));
            try { report.AppendLine("  AppUserModelID: " + (File.Exists(path) ? ReadAppId(path) ?? "(unset)" : "(missing)")); }
            catch (Exception error) { report.AppendLine("  ERROR: " + error.Message); result = 1; }
        }
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, "diagnostics.txt"), report.ToString(), Encoding.UTF8);
        return result;
    }

    private static void LogShortcutError(Exception error) {
        try {
            Directory.CreateDirectory(DataDirectory);
            string path = Path.Combine(DataDirectory, "shortcut-errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) {
                File.Copy(path, path + ".previous", true);
                File.WriteAllText(path, "");
            }
            File.AppendAllText(path,
                DateTime.UtcNow.ToString("o") + " " + error.ToString() + Environment.NewLine);
        }
        catch { }
    }
}

internal static class WindowTaskbar {
    internal static string TaskbarAppId(string activationId) {
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
            return "CodexLauncher." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(activationId))).Replace("-", "");
    }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    private delegate bool EnumResourceCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder value);
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    internal static string IconResource(string executable) {
        IntPtr module = LoadLibraryEx(executable, IntPtr.Zero, 2 /* LOAD_LIBRARY_AS_DATAFILE */);
        if (module == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            int resourceId = 0;
            EnumResourceNames(module, new IntPtr(14 /* RT_GROUP_ICON */), (handle, type, name, parameter) => {
                long value = name.ToInt64();
                if (value > 0 && value <= UInt16.MaxValue) resourceId = (int)value;
                return resourceId == 0;
            }, IntPtr.Zero);
            if (resourceId == 0) throw new IOException("No numeric icon resource was found in " + executable);
            // RelaunchIconResource uses negative resource IDs, unlike .lnk icon indexes.
            return executable + ",-" + resourceId;
        }
        finally { FreeLibrary(module); }
    }

    private static string ProcessAppId(uint processId) {
        IntPtr process = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, processId);
        if (process == IntPtr.Zero) return null;
        try {
            uint length = 0;
            if (GetApplicationUserModelId(process, ref length, null) != 122 || length == 0) return null;
            StringBuilder text = new StringBuilder(checked((int)length));
            return GetApplicationUserModelId(process, ref length, text) == 0 ? text.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    internal static int Apply(string appId, string executable) {
        string resource = IconResource(executable);
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        do {
            int count = 0;
            Exception failure = null;
            EnumWindows((window, parameter) => {
                // Only visible, unowned windows of the exact registered application.
                // Activation can return a transient PID when a single-instance app
                // forwards the launch, so discover the actual window owner instead.
                if (!IsWindowVisible(window) || GetWindow(window, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                try {
                    if (!String.Equals(ProcessAppId(processId), appId, StringComparison.Ordinal)) return true;
                    SetWindowProperties(window, TaskbarAppId(appId), executable, resource); count++;
                }
                catch (Exception error) { failure = error; }
                return true;
            }, IntPtr.Zero);
            if (failure != null) throw failure;
            if (count > 0) return count;
            Thread.Sleep(100);
        } while (timer.ElapsedMilliseconds < 4000);
        throw new TimeoutException("No visible window for " + appId + "; application activation succeeded but taskbar metadata was not applied.");
    }

    internal static void SetWindowProperties(IntPtr window, string appId, string executable, string iconResource) {
        Guid iid = typeof(IPropertyStore).GUID;
        IPropertyStore store;
        SHGetPropertyStoreForWindow(window, ref iid, out store);
        try {
            // Set relaunch information before the explicit identity, as required by Shell.
            SetString(store, 2, "\"" + executable + "\"");
            SetString(store, 3, iconResource);
            SetString(store, 4, "Codex");
            SetString(store, 5, appId);
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }

    private static void SetString(IPropertyStore store, uint property, string text) {
        PropertyKey key = new PropertyKey {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = property
        };
        PropVariant value = new PropVariant();
        PropVariant actual = new PropVariant();
        try {
            value.Type = 31;
            value.Value = Marshal.StringToCoTaskMemUni(text);
            store.SetValue(ref key, ref value);
            store.GetValue(ref key, out actual);
            if (actual.Type != 31 || Marshal.PtrToStringUni(actual.Value) != text)
                throw new IOException("Window taskbar property verification failed: " + property);
        }
        finally { PropVariantClear(ref actual); PropVariantClear(ref value); }
    }
}
