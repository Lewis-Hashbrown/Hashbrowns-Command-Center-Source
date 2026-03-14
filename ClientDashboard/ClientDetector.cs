using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientDashboard;

public class ClientDetector
{
    private static readonly string[] ToolWindowTitles =
    {
        "Script Manager",
        "Console",
        "Account Manager",
        "Client settings"
    };

    public List<IntPtr> FindClientWindows()
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;
            var title = NativeMethods.GetWindowTitle(hWnd);
            // Real client windows are titled like "DreamBot <version> ...".
            // Exclude app dialogs such as "Launch DreamBot".
            if (title.StartsWith("DreamBot ", StringComparison.OrdinalIgnoreCase) &&
                !title.StartsWith("Launch DreamBot", StringComparison.OrdinalIgnoreCase) &&
                !title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
            {
                windows.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public List<IntPtr> FindToolWindows()
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            var title = NativeMethods.GetWindowTitle(hWnd);
            if (ToolWindowTitles.Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase)))
                windows.Add(hWnd);

            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
