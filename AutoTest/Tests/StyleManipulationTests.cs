using System;

namespace AutoTest.Tests;

public static class StyleManipulationTests
{
    public static void Run(TestRunner runner, IntPtr hwnd)
    {
        runner.Run("Style: Can read window style", () =>
        {
            long style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            Assert.IsTrue(style != 0, "Style is zero");
        });

        runner.Run("Style: Can strip caption and thickframe", () =>
        {
            long style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            long original = style;

            style &= ~(Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_BORDER |
                        Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);

            long newStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            Assert.IsTrue((newStyle & Win32.WS_THICKFRAME) == 0, "WS_THICKFRAME not stripped");

            // Restore
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)original);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
        });

        runner.Run("Style: Can set WS_CHILD and restore", () =>
        {
            long style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            long original = style;

            style &= ~Win32.WS_POPUP;
            style |= Win32.WS_CHILD;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);

            long newStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            Assert.IsTrue((newStyle & Win32.WS_CHILD) != 0, "WS_CHILD not set");

            // Restore immediately
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)original);
        });
    }
}
