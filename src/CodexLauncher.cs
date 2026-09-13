using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Generic;

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
        LauncherLog.Write("INFO", "start", "exe=" + Assembly.GetExecutingAssembly().Location +
            " build=" + typeof(Program).Module.ModuleVersionId + " os=" + Environment.OSVersion +
            " x64=" + Environment.Is64BitProcess + " args=" + String.Join(" ", args));
        try {
            if (diagnose) return Diagnose();
            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (createShortcut) return RunBounded(() => CreateShortcut(executablePath, args[1]), 5000, "Shortcut creation");
            return Run(executablePath);
        }
        catch (Exception error) {
            LauncherLog.Write("ERROR", "fatal", error.ToString());
            LogShortcutError(error);
            if (!createShortcut && !diagnose)
                MessageBox(IntPtr.Zero, error.Message + "\n\nDetails: " + Path.Combine(DataDirectory, "shortcut-errors.log"), "Codex Launcher", 0x10);
            return 1;
        }
        finally { LauncherLog.Write("INFO", "exit", "Launcher finished"); }
    }

    private static int Run(string executablePath) {
        string installDirectory = Path.GetDirectoryName(executablePath);
        // Resolve the registered activation identity independently of taskbar grouping.
        string appId = AppDiscovery.Resolve(installDirectory);
        LauncherLog.Write("INFO", "discovery", "activationId=" + appId + " taskbarId=" + WindowTaskbar.TaskbarAppId(appId));

        int result = RunBounded(() => Activate(appId), 20000, "Application activation");
        if (result < 0) {
            // Refresh registration after a concurrent Store update and retry once.
            LogShortcutError(new COMException("Activation failed; retrying after package discovery.", result));
            appId = AppDiscovery.Resolve(installDirectory);
            result = RunBounded(() => Activate(appId), 20000, "Application activation retry");
        }
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        // Independent of taskbar success: cosmetic failures must not block activation.
        try { RunBounded(() => TrayIconOverride.Apply(appId, executablePath), 6000, "Tray icon"); }
        catch (Exception error) { LauncherLog.Write("WARN", "tray.failed", error.ToString()); }
        // The visible window belongs to the packaged app, not this executable.
        // Apply taskbar metadata independently of optional shortcut creation.
        try {
            RunBounded(() => WindowTaskbar.Apply(appId, executablePath),
                6000, "Taskbar icon");
        }
        catch (Exception error) {
            LogShortcutError(error);
            LauncherLog.Write("ERROR", "taskbar.failed", error.ToString());
            MessageBox(IntPtr.Zero, "应用已启动，但启动器未能确认任务栏图标属性写入成功。\n\n" +
                error.Message + "\n\n详细日志：" + LauncherLog.PathName,
                "Codex Launcher — 图标设置失败", 0x30);
            return 3;
        }
        return 0;
    }

    // A stuck Shell/COM call must not keep this launcher alive indefinitely.
    // COM objects are created, used and released on the same STA worker.
    internal static int RunBounded(Func<int> operation, int milliseconds, string stage) {
        LauncherLog.Write("INFO", "stage.begin", stage + " timeoutMs=" + milliseconds);
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        int result = 0;
        Exception failure = null;
        Thread worker = new Thread(() => {
            try { result = operation(); }
            catch (Exception error) { failure = error; }
        });
        worker.IsBackground = true;
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(milliseconds)) {
            LauncherLog.Write("ERROR", "stage.timeout", stage + " elapsedMs=" + elapsed.ElapsedMilliseconds);
            throw new TimeoutException(stage + " did not finish within " + milliseconds / 1000 +
                " seconds. Windows may still be processing the request. Try opening the app from Windows Search.");
        }
        LauncherLog.Write(failure == null ? "INFO" : "ERROR", "stage.end", stage +
            " elapsedMs=" + elapsed.ElapsedMilliseconds + " result=" + result + " error=" + failure);
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
            int foreground = CoAllowSetForegroundWindow(manager, IntPtr.Zero);
            uint processId;
            int result = manager.ActivateApplication(appId, null, ActivateOptions.NoErrorUI, out processId);
            LauncherLog.Write(result < 0 ? "ERROR" : "INFO", "activation", "appId=" + appId +
                " pid=" + processId + " hr=0x" + result.ToString("X8") + " foregroundHr=0x" + foreground.ToString("X8"));
            return result;
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
            report.AppendLine("Window snapshot (read-only):");
            report.AppendLine(WindowTaskbar.Inspect(selected, executablePath));
            report.AppendLine(TrayIconOverride.Inspect(selected));
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

internal static class LauncherLog {
    private static readonly object Gate = new object();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    internal static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLauncher", "logs",
        "launch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".log");
    private static bool initialized, warned;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    internal static void Write(string level, string stage, string detail) {
        lock (Gate) {
            try {
                if (!initialized) {
                    string directory = Path.GetDirectoryName(PathName);
                    Directory.CreateDirectory(directory);
                    string[] files = Directory.GetFiles(directory, "launch-*.log");
                    Array.Sort(files, StringComparer.Ordinal);
                    // Each run owns a separate file; never rotate another active run's file.
                    for (int i = 0; i < files.Length - 19; i++) {
                        try {
                            if (File.GetLastWriteTimeUtc(files[i]) < DateTime.UtcNow.AddMinutes(-5)) File.Delete(files[i]);
                        }
                        catch { /* Retention failure must not discard the current run. */ }
                    }
                    initialized = true;
                }
                File.AppendAllText(PathName, DateTime.UtcNow.ToString("o") + " +" + Clock.ElapsedMilliseconds +
                    "ms [" + level + "] " + stage + " " + detail.Replace("\r", "\\r").Replace("\n", "\\n") + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception error) {
                if (!warned) {
                    warned = true;
                    MessageBox(IntPtr.Zero, "无法写入启动诊断日志：" + PathName + "\n" + error.Message,
                        "Codex Launcher — 日志不可用", 0x30);
                }
            }
        }
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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("kernel32.dll", SetLastError = true)]
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

    internal static string ProcessAppId(uint processId) {
        IntPtr process = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, processId);
        if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess pid=" + processId);
        try {
            uint length = 0;
            int result = GetApplicationUserModelId(process, ref length, null);
            if (result == 15703) return null; // APPMODEL_ERROR_NO_APPLICATION
            if (result != 122 || length == 0) throw new System.ComponentModel.Win32Exception(result, "GetApplicationUserModelId size pid=" + processId);
            StringBuilder text = new StringBuilder(checked((int)length));
            result = GetApplicationUserModelId(process, ref length, text);
            if (result != 0) throw new System.ComponentModel.Win32Exception(result, "GetApplicationUserModelId pid=" + processId);
            return text.ToString();
        }
        finally { CloseHandle(process); }
    }

    internal static int Apply(string appId, string executable) {
        string resource = IconResource(executable);
        string identity = TaskbarAppId(appId);
        LauncherLog.Write("INFO", "taskbar.begin", "icon=" + resource +
            " windowWaitMs=4000; apply immediately when found; property verification only");
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<uint, string> processStates = new Dictionary<uint, string>();
        do {
            int count = 0;
            Exception failure = null;
            bool enumerated = EnumWindows((window, parameter) => {
                // Activation can return a transient PID. Match the actual window owner.
                if (!IsWindowVisible(window) || GetWindow(window, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                string processIdentity;
                try { processIdentity = ProcessAppId(processId); }
                catch (Exception error) {
                    RecordProcess(processStates, processId, "query-error=" + error.Message);
                    return true;
                }
                RecordProcess(processStates, processId, processIdentity ?? "(no packaged application identity)");
                if (!String.Equals(processIdentity, appId, StringComparison.Ordinal)) return true;
                string context = "hwnd=0x" + window.ToInt64().ToString("X") + " pid=" + processId;
                LauncherLog.Write("INFO", "window.discovered", context + " elapsedMs=" + timer.ElapsedMilliseconds);
                WindowBranding.ApplyTitle(window);
                try {
                    string mismatch = PropertyMismatch(window, identity, executable, resource);
                    if (mismatch != null) {
                        LauncherLog.Write("INFO", "properties.before", context + " " + mismatch);
                        SetWindowProperties(window, identity, executable, resource);
                        string remaining = PropertyMismatch(window, identity, executable, resource);
                        if (remaining != null) throw new IOException(remaining);
                        LauncherLog.Write("INFO", "properties.applied", context + " verified after COM reopen");
                    }
                    else LauncherLog.Write("INFO", "properties.already-match", context);
                    count++;
                }
                catch (Exception error) {
                    failure = error;
                    LauncherLog.Write("ERROR", "properties.failed", context + " " + error);
                }
                return true;
            }, IntPtr.Zero);
            if (!enumerated) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed");
            if (failure != null) throw failure;
            if (count > 0) {
                LauncherLog.Write("INFO", "taskbar.properties-verified", "windows=" + count +
                    " elapsedMs=" + timer.ElapsedMilliseconds + "; visualStatus=UNVERIFIED; no further monitoring or rewriting");
                return count;
            }
            if (timer.ElapsedMilliseconds >= 4000)
                throw new TimeoutException("No visible window for " + appId + "; activation succeeded but taskbar properties could not be applied within 4 seconds.");
            Thread.Sleep(100);
        } while (true);
    }
    private static void RecordProcess(Dictionary<uint, string> states, uint pid, string state) {
        string previous;
        if (!states.TryGetValue(pid, out previous) || previous != state) {
            states[pid] = state;
            LauncherLog.Write("INFO", "process.identity", "pid=" + pid + " " + state);
        }
    }

    internal static string Inspect(string appId, string executable) {
        StringBuilder report = new StringBuilder();
        string resource = IconResource(executable);
        int matches = 0;
        bool enumerated = EnumWindows((window, parameter) => {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            try {
                if (ProcessAppId(pid) != appId) return true;
                matches++;
                string mismatch = PropertyMismatch(window, TaskbarAppId(appId), executable, resource);
                report.AppendLine("hwnd=0x" + window.ToInt64().ToString("X") + " pid=" + pid + " " +
                    (mismatch ?? "properties match; visualStatus=UNVERIFIED"));
                report.AppendLine(WindowBranding.Inspect(window));
            }
            catch (Exception error) { report.AppendLine("pid=" + pid + " inspection error: " + error.Message); }
            return true;
        }, IntPtr.Zero);
        if (!enumerated) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed");
        report.AppendLine("Matching visible unowned windows: " + matches);
        return report.ToString();
    }

    internal static string PropertyMismatch(IntPtr window, string appId, string executable, string iconResource) {
        Guid iid = typeof(IPropertyStore).GUID;
        IPropertyStore store;
        SHGetPropertyStoreForWindow(window, ref iid, out store);
        try {
            string[] expected = { "\"" + executable + "\"", iconResource, "Codex", appId };
            StringBuilder differences = new StringBuilder();
            for (uint property = 2; property <= 5; property++) {
                PropertyKey key = new PropertyKey {
                    FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = property
                };
                PropVariant value = new PropVariant();
                try {
                    store.GetValue(ref key, out value);
                    string actual = value.Type == 31 ? Marshal.PtrToStringUni(value.Value) : "(type=" + value.Type + ")";
                    if (value.Type != 31 || actual != expected[property - 2])
                        differences.Append("property=" + property + " actual=[" + actual + "] expected=[" + expected[property - 2] + "]; ");
                }
                finally { PropVariantClear(ref value); }
            }
            return differences.Length == 0 ? null : differences.ToString();
        }
        finally { Marshal.FinalReleaseComObject(store); }
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
