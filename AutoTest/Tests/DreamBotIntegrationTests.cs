using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace AutoTest.Tests;

public static class DreamBotIntegrationTests
{
    public static void Run(TestRunner runner)
    {
        var clients = FindDreamBotClients();
        if (clients.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  SKIP DreamBot integration tests (no DreamBot client windows found)");
            Console.ResetColor();
            return;
        }

        var hwnd = clients[0];

        runner.Run("DreamBot: Client window is valid", () =>
        {
            Assert.IsTrue(Win32.IsWindow(hwnd), "DreamBot window handle invalid");
            Assert.IsTrue(Win32.IsWindowVisible(hwnd), "DreamBot window not visible");
        });

        runner.Run("DreamBot: Title does not contain 'Launcher'", () =>
        {
            var title = Win32.GetWindowTitle(hwnd);
            Assert.IsFalse(title.Contains("Launcher", StringComparison.OrdinalIgnoreCase),
                $"Found launcher, not client: '{title}'");
        });

        runner.Run("DreamBot: Strip styles and resize to 500x400", () =>
        {
            long origStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);

            long style = origStyle;
            style &= ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME |
                        Win32.WS_BORDER | Win32.WS_SYSMENU |
                        Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
            style |= Win32.WS_CLIPSIBLINGS;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);

            Win32.GetWindowRect(hwnd, out var beforeRect);
            Win32.MoveWindow(hwnd, beforeRect.Left, beforeRect.Top, 500, 400, true);
            Thread.Sleep(500);

            Win32.GetWindowRect(hwnd, out var afterRect);
            Assert.InRange(afterRect.Width, 490, 510, $"DreamBot width after resize: {afterRect.Width}");
            Assert.InRange(afterRect.Height, 390, 410, $"DreamBot height after resize: {afterRect.Height}");

            // Restore
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)origStyle);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
            Win32.MoveWindow(hwnd, beforeRect.Left, beforeRect.Top, beforeRect.Width, beforeRect.Height, true);
        });

        runner.Run("DreamBot: Embed in panel and resize to 600x400", () =>
        {
            using var form = new Form
            {
                Width = 620, Height = 440, Text = "DreamBot Embed Test",
                StartPosition = FormStartPosition.Manual,
                Left = 50, Top = 50
            };
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
            Thread.Sleep(500);

            Win32.GetClientRect(hwnd, out var cr);
            Assert.InRange(cr.Width, panel.Width - 30, panel.Width + 10,
                $"Embedded DreamBot width: {cr.Width} vs panel {panel.Width}");
            Assert.InRange(cr.Height, panel.Height - 30, panel.Height + 10,
                $"Embedded DreamBot height: {cr.Height} vs panel {panel.Height}");

            // Release
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)origStyle);
            Win32.SetParent(hwnd, IntPtr.Zero);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 800, 600,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
            Win32.ShowWindow(hwnd, Win32.SW_SHOW);
            Thread.Sleep(200);

            form.Close();
        });
    }

    private static List<IntPtr> FindDreamBotClients()
    {
        var windows = new List<IntPtr>();
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("DreamBot", StringComparison.OrdinalIgnoreCase) &&
                !title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
