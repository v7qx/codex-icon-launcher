using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

internal static class AppDiscovery {
    internal const string DefaultFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    private const int InsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count,
        IntPtr names, ref uint bufferLength, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int OpenPackageInfoByFullName(string name, uint reserved, out IntPtr reference);
    [DllImport("kernel32.dll")]
    private static extern int GetPackageApplicationIds(IntPtr reference, ref uint bufferLength, IntPtr buffer, out uint count);
    [DllImport("kernel32.dll")]
    private static extern int ClosePackageInfo(IntPtr reference);

    internal static string ReadOverride(string directory) {
        string path = Path.Combine(directory, "CodexLauncher.appid");
        if (!File.Exists(path)) return null;
        string value = File.ReadAllText(path).Trim();
        if (value.Length > 128 || !Regex.IsMatch(value, @"\A[A-Za-z0-9.-]+_[A-Za-z0-9]{13}![A-Za-z0-9.]+\z"))
            throw new InvalidDataException("CodexLauncher.appid must contain one packaged AppUserModelID (PackageFamily!ApplicationId).");
        return value;
    }

    internal static string Resolve(string directory) {
        string requested = ReadOverride(directory);
        string family = requested == null ? DefaultFamily : requested.Split('!')[0];
        // Packages can be replaced between enumeration and opening their metadata.
        for (int attempt = 0; ; attempt++) {
            try { return SelectApplication(QueryApplicationIds(family), family, requested); }
            catch (Win32Exception) { if (attempt == 2) throw; }
        }
    }

    internal static string SelectApplication(string[] ids, string family, string requested) {
        SortedSet<string> candidates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
            if (id != null && id.StartsWith(family + "!", StringComparison.Ordinal)) candidates.Add(id);
        if (requested != null) {
            if (candidates.Contains(requested)) return requested;
            throw new InvalidOperationException("The configured application is not registered for the current Windows user: " + requested);
        }
        string preferred = family + "!App";
        if (candidates.Contains(preferred)) return preferred;
        if (candidates.Count == 1) { foreach (string id in candidates) return id; }
        if (candidates.Count == 0)
            throw new InvalidOperationException("No launchable application was found for " + family +
                ". Install or finish updating the official app for this Windows user, then try again.");
        throw new InvalidOperationException("Multiple application entries were found: " + String.Join(", ", candidates) +
            ". Put the intended AppUserModelID in CodexLauncher.appid beside Codex.exe.");
    }

    internal static string[] QueryApplicationIds(string family) {
        List<string> ids = new List<string>();
        foreach (string package in QueryPackages(family)) {
            IntPtr reference;
            Check(OpenPackageInfoByFullName(package, 0, out reference));
            try { ids.AddRange(QueryIds(reference)); }
            finally { ClosePackageInfo(reference); }
        }
        return ids.ToArray();
    }

    private static string[] QueryPackages(string family) {
        for (int attempt = 0; attempt < 3; attempt++) {
            uint count = 0, length = 0;
            int result = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
            if (result == 0 && count == 0) return new string[0];
            if (result != InsufficientBuffer) Check(result);
            IntPtr names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try {
                result = GetPackagesByPackageFamily(family, ref count, names, ref length, buffer);
                if (result == InsufficientBuffer) continue;
                Check(result);
                return ReadPointers(names, count);
            }
            finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(names); }
        }
        throw new Win32Exception(InsufficientBuffer, "Package registration changed repeatedly; try again after the update finishes.");
    }

    private static string[] QueryIds(IntPtr reference) {
        uint length = 0, count;
        int result = GetPackageApplicationIds(reference, ref length, IntPtr.Zero, out count);
        if (result == 0 && count == 0) return new string[0];
        if (result != InsufficientBuffer) Check(result);
        for (int attempt = 0; attempt < 3; attempt++) {
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
            try {
                result = GetPackageApplicationIds(reference, ref length, buffer, out count);
                if (result == InsufficientBuffer) continue;
                Check(result);
                return ReadPointers(buffer, count);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new Win32Exception(InsufficientBuffer);
    }

    private static string[] ReadPointers(IntPtr buffer, uint count) {
        string[] result = new string[checked((int)count)];
        for (int i = 0; i < result.Length; i++)
            result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, checked(i * IntPtr.Size)));
        return result;
    }

    private static void Check(int result) { if (result != 0) throw new Win32Exception(result); }
}
