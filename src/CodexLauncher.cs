using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

[Flags]
internal enum ActivateOptions {
    None = 0
}

[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager {
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        ActivateOptions options,
        out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager { }

internal static class Program {
    private static void UpdateShortcut(string shortcutPath, string executablePath, string iconPath) {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
        shortcut.Arguments = "";
        shortcut.IconLocation = iconPath + ",0";
        shortcut.Description = "Open Codex";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    [STAThread]
    private static int Main() {
        string executablePath = Assembly.GetExecutingAssembly().Location;
        string installDirectory = Path.GetDirectoryName(executablePath);
        string externalIcon = Path.Combine(installDirectory, "Codex.ico");
        string selectedIcon = File.Exists(externalIcon) ? externalIcon : executablePath;
        string desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Codex.lnk");
        string startShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Codex.lnk");
        string stateFile = Path.Combine(installDirectory, "Codex.shortcut-state");
        FileInfo iconInfo = new FileInfo(selectedIcon);
        string desiredState = executablePath + "\r\n" + selectedIcon + "\r\n" +
            iconInfo.Length + ":" + iconInfo.LastWriteTimeUtc.Ticks;

        try {
            bool shortcutsAreCurrent =
                File.Exists(desktopShortcut) &&
                File.Exists(startShortcut) &&
                File.Exists(stateFile) &&
                File.ReadAllText(stateFile) == desiredState;

            if (!shortcutsAreCurrent) {
                UpdateShortcut(desktopShortcut, executablePath, selectedIcon);
                UpdateShortcut(startShortcut, executablePath, selectedIcon);
                File.WriteAllText(stateFile, desiredState);
            }
        }
        catch {
            // Shortcut maintenance must never prevent the app from launching.
        }

        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        uint processId;
        int result = manager.ActivateApplication(
            "OpenAI.Codex_2p2nqsd0c76g0!App", null, ActivateOptions.None, out processId);
        Marshal.FinalReleaseComObject(manager);
        return result;
    }
}
