using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientDashboard;

public class LauncherAutomation
{
    private const uint BM_CLICK = 0x00F5;

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, NativeMethods.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public List<IntPtr> FindLauncherWindows()
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            var title = NativeMethods.GetWindowTitle(hWnd);
            if (title.Contains("DreamBot", StringComparison.OrdinalIgnoreCase) &&
                title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
            {
                windows.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>
    /// Attempts a non-invasive launch click via child button message only.
    /// Avoids global cursor movement/input hijacking.
    /// </summary>
    public bool TryClickLaunch(IntPtr launcherHwnd)
    {
        return TryClickChildLaunchButton(launcherHwnd);
    }

    private static bool TryClickChildLaunchButton(IntPtr launcherHwnd)
    {
        IntPtr launchButton = IntPtr.Zero;
        EnumChildWindows(launcherHwnd, (child, _) =>
        {
            var text = NativeMethods.GetWindowTitle(child);
            if (text.Contains("Launch", StringComparison.OrdinalIgnoreCase))
            {
                launchButton = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (launchButton == IntPtr.Zero)
            return false;

        PostMessage(launchButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return true;
    }
}
