using System;
using System.Diagnostics;

namespace AutoTest;

public static class AllWindowScanner
{
    public static void ScanJavaWindows()
    {
        Console.WriteLine("=== All Visible Windows (Java processes + title search) ===\n");

        // First, find all Java process IDs
        var javaProcs = Process.GetProcessesByName("java");
        var javawProcs = Process.GetProcessesByName("javaw");
        Console.WriteLine($"  java.exe processes: {javaProcs.Length}");
        Console.WriteLine($"  javaw.exe processes: {javawProcs.Length}");
        foreach (var p in javaProcs)
            Console.WriteLine($"    java.exe PID={p.Id}, MainWindow=\"{p.MainWindowTitle}\", hwnd=0x{p.MainWindowHandle:X}");
        foreach (var p in javawProcs)
            Console.WriteLine($"    javaw.exe PID={p.Id}, MainWindow=\"{p.MainWindowTitle}\", hwnd=0x{p.MainWindowHandle:X}");

        Console.WriteLine();
        Console.WriteLine("All visible windows with titles:");
        int count = 0;
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title)) return true;

            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            Win32.GetWindowRect(hWnd, out var wr);

            // Show all windows with their owning process
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            if (procName.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("dream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("osrs", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("runescape", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("client", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("launch", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("bot", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  hwnd=0x{hWnd:X} pid={pid} proc={procName}");
                Console.WriteLine($"    Title: \"{title}\"");
                Console.WriteLine($"    Size: {wr.Width}x{wr.Height} at ({wr.Left},{wr.Top})");
                count++;
            }
            return true;
        }, IntPtr.Zero);

        Console.WriteLine($"\nFound {count} matching windows");
    }
}
