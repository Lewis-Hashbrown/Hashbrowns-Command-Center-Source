using System;
using System.Collections.Generic;

namespace AutoTest;

public static class WindowScanner
{
    public static void ScanAll()
    {
        Console.WriteLine("=== Window Scanner ===\n");
        Console.WriteLine("All visible windows with 'DreamBot' or 'OSRS' or 'Client' in title:\n");

        int count = 0;
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("DreamBot", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("OSRS", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Client Dashboard", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("RuneScape", StringComparison.OrdinalIgnoreCase))
            {
                Win32.GetWindowRect(hWnd, out var wr);
                Win32.GetClientRect(hWnd, out var cr);
                long style = (long)Win32.GetWindowLongPtr(hWnd, Win32.GWL_STYLE);
                bool isChild = (style & Win32.WS_CHILD) != 0;
                bool hasCaption = (style & Win32.WS_CAPTION) != 0;
                bool hasThickFrame = (style & Win32.WS_THICKFRAME) != 0;

                Console.WriteLine($"  hwnd=0x{hWnd:X}");
                Console.WriteLine($"    Title: \"{title}\"");
                Console.WriteLine($"    Window: {wr.Width}x{wr.Height} at ({wr.Left},{wr.Top})");
                Console.WriteLine($"    Client: {cr.Width}x{cr.Height}");
                Console.WriteLine($"    Style: WS_CHILD={isChild}, WS_CAPTION={hasCaption}, WS_THICKFRAME={hasThickFrame}");
                Console.WriteLine();
                count++;
            }
            return true;
        }, IntPtr.Zero);

        if (count == 0)
            Console.WriteLine("  (none found)");
        Console.WriteLine($"Total: {count} windows");
    }
}
