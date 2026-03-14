using System;
using System.Diagnostics;

namespace AutoTest.Tests;

public static class WindowDetectionTests
{
    public static void Run(TestRunner runner, IntPtr hwnd)
    {
        runner.Run("Detect: Window handle is valid", () =>
        {
            Assert.IsTrue(Win32.IsWindow(hwnd), "Window handle is invalid");
            Assert.IsTrue(Win32.IsWindowVisible(hwnd), "Window is not visible");
        });

        runner.Run("Detect: Can read window title", () =>
        {
            var title = Win32.GetWindowTitle(hwnd);
            Assert.IsTrue(title.Length > 0, "Window title is empty");
        });

        runner.Run("Detect: EnumWindows finds target", () =>
        {
            bool found = false;
            Win32.EnumWindows((hWnd, _) =>
            {
                if (hWnd == hwnd) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            Assert.IsTrue(found, "EnumWindows did not find target handle");
        });

        runner.Run("Detect: Can get window rect", () =>
        {
            Win32.GetWindowRect(hwnd, out var rect);
            Assert.IsTrue(rect.Width > 0, $"Window width is {rect.Width}");
            Assert.IsTrue(rect.Height > 0, $"Window height is {rect.Height}");
        });

        runner.Run("Detect: Can get client rect", () =>
        {
            Win32.GetClientRect(hwnd, out var rect);
            Assert.IsTrue(rect.Width > 0, $"Client width is {rect.Width}");
            Assert.IsTrue(rect.Height > 0, $"Client height is {rect.Height}");
        });
    }
}
