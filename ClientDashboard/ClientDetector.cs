using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientDashboard;

public class ClientDetector
{
    private static readonly Regex DreamBotClientTitleRegex = new(
        @"^DreamBot\s4\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            if (IsDreamBotClientTitle(title))
            {
                windows.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IsDreamBotClientTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        if (title.StartsWith("Launch DreamBot", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
            return false;

        // Match titles beginning with "DreamBot 4" (major version 4).
        return DreamBotClientTitleRegex.IsMatch(title);
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
