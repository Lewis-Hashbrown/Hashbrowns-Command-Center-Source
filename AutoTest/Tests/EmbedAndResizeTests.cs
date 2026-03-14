using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoTest.Tests;

public static class EmbedAndResizeTests
{
    public static void Run(TestRunner runner, IntPtr hwnd)
    {
        runner.Run("Embed: Reparent into WinForms panel", () =>
        {
            using var form = new Form { Width = 800, Height = 600, Text = "AutoTest Host", Visible = false };
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Thread.Sleep(200);

            long origStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);

            long style = origStyle;
            style &= ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME |
                        Win32.WS_BORDER | Win32.WS_SYSMENU |
                        Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
            style |= Win32.WS_CHILD | Win32.WS_CLIPSIBLINGS;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);

            Win32.SetParent(hwnd, panel.Handle);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, panel.Width, panel.Height,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
            Win32.MoveWindow(hwnd, 0, 0, panel.Width, panel.Height, true);
            Thread.Sleep(300);

            long newStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            Assert.IsTrue((newStyle & Win32.WS_CHILD) != 0, "Window is not WS_CHILD after embed");

            // Release back
            ReleaseWindow(hwnd, origStyle);
            form.Close();
        });

        runner.Run("Resize: 600x400", () => TestResize(hwnd, 600, 400));
        runner.Run("Resize: 400x300", () => TestResize(hwnd, 400, 300));
        runner.Run("Resize: 1024x768", () => TestResize(hwnd, 1024, 768));
        runner.Run("Resize: 300x200 (very small)", () => TestResize(hwnd, 300, 200));
    }

    private static void TestResize(IntPtr hwnd, int targetW, int targetH)
    {
        using var form = new Form
        {
            Width = targetW + 20, Height = targetH + 40,
            Text = "AutoTest Host",
            StartPosition = FormStartPosition.Manual,
            Left = 50, Top = 50
        };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();
        Thread.Sleep(100);

        long origStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);

        long style = origStyle;
        style &= ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME |
                    Win32.WS_BORDER | Win32.WS_SYSMENU |
                    Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
        style |= Win32.WS_CHILD | Win32.WS_CLIPSIBLINGS;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);

        Win32.SetParent(hwnd, panel.Handle);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, panel.Width, panel.Height,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
        Win32.MoveWindow(hwnd, 0, 0, panel.Width, panel.Height, true);
        Thread.Sleep(300);

        Win32.GetClientRect(hwnd, out var clientRect);
        int tolerance = 20;
        Assert.InRange(clientRect.Width, panel.Width - tolerance, panel.Width + tolerance,
            $"Width: expected ~{panel.Width}, got {clientRect.Width}");
        Assert.InRange(clientRect.Height, panel.Height - tolerance, panel.Height + tolerance,
            $"Height: expected ~{panel.Height}, got {clientRect.Height}");

        ReleaseWindow(hwnd, origStyle);
        form.Close();
    }

    private static void ReleaseWindow(IntPtr hwnd, long origStyle)
    {
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)origStyle);
        Win32.SetParent(hwnd, IntPtr.Zero);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 800, 600,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
        Win32.ShowWindow(hwnd, Win32.SW_SHOW);
        Thread.Sleep(200);
    }
}
