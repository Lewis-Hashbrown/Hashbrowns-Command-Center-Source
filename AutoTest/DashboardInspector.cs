using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoTest;

public static class DashboardInspector
{
    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWndParent, Win32.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public static void Inspect()
    {
        Console.WriteLine("=== Dashboard Inspector ===\n");

        // Find the dashboard window
        IntPtr dashHwnd = IntPtr.Zero;
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("OSRS Client Dashboard"))
            {
                dashHwnd = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (dashHwnd == IntPtr.Zero)
        {
            Console.WriteLine("Dashboard not found!");
            return;
        }

        Win32.GetWindowRect(dashHwnd, out var dashRect);
        Win32.GetClientRect(dashHwnd, out var dashClient);
        Console.WriteLine($"Dashboard: hwnd=0x{dashHwnd:X}");
        Console.WriteLine($"  Window: {dashRect.Width}x{dashRect.Height}");
        Console.WriteLine($"  Client: {dashClient.Width}x{dashClient.Height}");

        Console.WriteLine("\nAll child windows (depth 0-2):");
        int level0 = 0;
        EnumChildWindows(dashHwnd, (child, _) =>
        {
            level0++;
            var title = Win32.GetWindowTitle(child);
            Win32.GetWindowRect(child, out var cr);
            Win32.GetClientRect(child, out var ccr);
            long style = (long)Win32.GetWindowLongPtr(child, Win32.GWL_STYLE);
            bool isChild = (style & Win32.WS_CHILD) != 0;

            Win32.GetWindowThreadProcessId(child, out uint pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            string className = GetClassName(child);

            // Only print interesting children (non-empty title, or Java-owned, or sizable)
            if (!string.IsNullOrEmpty(title) || procName.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                cr.Width > 100 || className.Contains("SunAwt") || className.Contains("Panel"))
            {
                Console.WriteLine($"  [{level0}] hwnd=0x{child:X} class=\"{className}\" proc={procName}");
                Console.WriteLine($"       title=\"{title}\"");
                Console.WriteLine($"       rect={cr.Width}x{cr.Height} at ({cr.Left},{cr.Top}), client={ccr.Width}x{ccr.Height}");
                Console.WriteLine($"       WS_CHILD={isChild} style=0x{style:X8}");
            }
            return true;
        }, IntPtr.Zero);

        Console.WriteLine($"\nTotal direct children enumerated: {level0}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    static string GetClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
