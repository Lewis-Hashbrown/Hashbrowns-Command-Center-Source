using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoTest;

public static class DeepScanner
{
    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWndParent, Win32.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public static void ScanDeep()
    {
        Console.WriteLine("=== Deep Window Scanner (including hidden/non-visible) ===\n");

        // Scan ALL top-level windows owned by java processes
        var javaProcs = Process.GetProcessesByName("java");
        var javawProcs = Process.GetProcessesByName("javaw");

        Console.WriteLine($"Java processes: java={javaProcs.Length}, javaw={javawProcs.Length}");
        foreach (var p in javaProcs)
            Console.WriteLine($"  java.exe PID={p.Id}, Memory={p.WorkingSet64 / 1024 / 1024}MB");
        foreach (var p in javawProcs)
            Console.WriteLine($"  javaw.exe PID={p.Id}, Memory={p.WorkingSet64 / 1024 / 1024}MB");

        Console.WriteLine();
        Console.WriteLine("ALL windows (including hidden) from Java processes:");

        int total = 0;
        Win32.EnumWindows((hWnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }

            if (!procName.Contains("java", StringComparison.OrdinalIgnoreCase))
                return true;

            var title = Win32.GetWindowTitle(hWnd);
            bool visible = Win32.IsWindowVisible(hWnd);
            Win32.GetWindowRect(hWnd, out var wr);
            long style = (long)Win32.GetWindowLongPtr(hWnd, Win32.GWL_STYLE);

            Console.WriteLine($"  hwnd=0x{hWnd:X} visible={visible} title=\"{title}\"");
            Console.WriteLine($"    Size: {wr.Width}x{wr.Height} at ({wr.Left},{wr.Top})");
            Console.WriteLine($"    Style: 0x{style:X8}");

            // Count child windows
            int childCount = 0;
            EnumChildWindows(hWnd, (child, _) =>
            {
                childCount++;
                if (childCount <= 5)
                {
                    var childTitle = Win32.GetWindowTitle(child);
                    Win32.GetWindowRect(child, out var cwr);
                    Console.WriteLine($"      child hwnd=0x{child:X} title=\"{childTitle}\" size={cwr.Width}x{cwr.Height}");
                }
                return true;
            }, IntPtr.Zero);
            if (childCount > 5)
                Console.WriteLine($"      ... and {childCount - 5} more children");
            Console.WriteLine($"    Children: {childCount}");
            Console.WriteLine();
            total++;
            return true;
        }, IntPtr.Zero);

        Console.WriteLine($"Total Java windows: {total}");

        // Also check ALL windows for anything DreamBot related
        Console.WriteLine("\n--- All windows with 'dream' or 'bot' (case insensitive) ---");
        Win32.EnumWindows((hWnd, _) =>
        {
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("dream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("bot", StringComparison.OrdinalIgnoreCase))
            {
                bool visible = Win32.IsWindowVisible(hWnd);
                Win32.GetWindowRect(hWnd, out var wr);
                Console.WriteLine($"  hwnd=0x{hWnd:X} visible={visible} title=\"{title}\" size={wr.Width}x{wr.Height}");
            }
            return true;
        }, IntPtr.Zero);
    }
}
