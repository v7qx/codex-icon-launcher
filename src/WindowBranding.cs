using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowBranding {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter,
        IntPtr value, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendTextTimeout(IntPtr window, uint message, IntPtr parameter,
        string text, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    internal static string CodexTitle(string title) {
        if (title == "ChatGPT") return "Codex";
        // Preserve conversation/document names; never replace arbitrary occurrences.
        foreach (string separator in new string[] { " — ", " – ", " - ", " | " }) {
            string suffix = separator + "ChatGPT";
            if (title.EndsWith(suffix, StringComparison.Ordinal))
                return title.Substring(0, title.Length - "ChatGPT".Length) + "Codex";
        }
        return title;
    }

    private static string Title(IntPtr window) {
        // Cross-process GetWindowText reads the native caption, not WM_GETTEXT.
        StringBuilder text = new StringBuilder(4096);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    internal static void ApplyTitle(IntPtr window) {
        try {
            string before = Title(window);
            // A possibly truncated caption must not be written back.
            if (before.Length >= 4095) return;
            string after = CodexTitle(before);
            if (before == after) return;
            IntPtr result;
            if (SendTextTimeout(window, 0x000C /* WM_SETTEXT */, IntPtr.Zero, after,
                0x22 /* SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT */, 500, out result) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WM_SETTEXT failed or timed out");
            if (result == IntPtr.Zero || Title(window) != after)
                throw new InvalidOperationException("Window title verification failed");
            LauncherLog.Write("INFO", "window.title-applied", "hwnd=0x" + window.ToInt64().ToString("X") +
                " title=" + after + "; one-shot; native caption verified");
        }
        catch (Exception error) {
            // Independent of taskbar properties; cosmetic failure remains nonfatal.
            LauncherLog.Write("WARN", "window.title-failed", "hwnd=0x" + window.ToInt64().ToString("X") + " " + error.Message);
        }
    }

    private static string IconState(IntPtr window, int kind) {
        IntPtr icon;
        if (SendMessageTimeout(window, 0x007F /* WM_GETICON */, new IntPtr(kind), IntPtr.Zero,
            0x22, 500, out icon) == IntPtr.Zero)
            return "message failed/timeout (error=" + Marshal.GetLastWin32Error() + ")";
        if (icon == IntPtr.Zero) return "0 (unset; class/system fallback possible)";
        // A nonzero HICON can be stale after its owner exits. Test a local copy;
        // never destroy or take ownership of the target application's handle.
        IntPtr copy = CopyIcon(icon);
        bool valid = copy != IntPtr.Zero;
        if (valid) DestroyIcon(copy);
        return "0x" + icon.ToInt64().ToString("X") + " copyable=" + valid;
    }

    internal static string Inspect(IntPtr window) {
        StringBuilder name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return "  ClassName=" + name + " WindowText=[" + Title(window).Replace("\r", "\\r").Replace("\n", "\\n") + "]" +
            Environment.NewLine + "  WM_GETICON BIG=" + IconState(window, 1) +
            " SMALL=" + IconState(window, 0) + " SMALL2=" + IconState(window, 2) +
            Environment.NewLine + "  Window icon override: disabled (launcher-owned HICON does not survive launcher exit)";
    }
}
