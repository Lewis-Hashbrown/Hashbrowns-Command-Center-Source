using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AutoTest.Tests;

public static class MultiClientLayoutStressTests
{
    private const int DashboardWidth = 1280;
    private const int DashboardHeight = 720;
    private const int TopBarHeight = 50;
    private const int ContentPadding = 8;
    private const int Tolerance = 30;

    public static void Run(TestRunner runner)
    {
        runner.Run("Stress: 3 clients fill all tiles", () => RunScenario(3));
        runner.Run("Stress: 6 clients fill all tiles", () => RunScenario(6));
        runner.Run("Stress: 20 clients fill all tiles", () => RunScenario(20));
    }

    private static void RunScenario(int count)
    {
        var processes = new List<Process>();
        var handles = new List<IntPtr>();
        var originalStyles = new Dictionary<IntPtr, long>();
        var panels = new List<Panel>();

        using var hostForm = new Form
        {
            Width = DashboardWidth + 16,
            Height = DashboardHeight + 39,
            Text = $"AutoTest Dashboard Stress {count}",
            StartPosition = FormStartPosition.CenterScreen
        };

        try
        {
            SpawnNotepads(count, processes, handles);
            Assert.AreEqual(count, handles.Count, "Did not collect all Notepad windows");

            int contentWidth = DashboardWidth - ContentPadding;
            int contentHeight = DashboardHeight - TopBarHeight - ContentPadding;
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            int rows = (int)Math.Ceiling(count / (double)cols);
            int tileWidth = contentWidth / cols;
            int tileHeight = contentHeight / rows;

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var panel = new Panel
                {
                    Left = col * tileWidth,
                    Top = row * tileHeight,
                    Width = tileWidth,
                    Height = tileHeight,
                    BorderStyle = BorderStyle.None
                };
                hostForm.Controls.Add(panel);
                panels.Add(panel);
            }

            hostForm.Show();
            Thread.Sleep(350);

            for (int i = 0; i < handles.Count; i++)
            {
                var hwnd = handles[i];
                var panel = panels[i];
                EmbedAndResizeWithRetries(hwnd, panel, originalStyles);
            }

            foreach (var (hwnd, index) in handles.Select((h, i) => (h, i)))
            {
                var panel = panels[index];
                AssertWindowFillsPanel(hwnd, panel, count, index + 1);
            }
        }
        finally
        {
            foreach (var hwnd in handles)
            {
                if (!Win32.IsWindow(hwnd)) continue;
                if (originalStyles.TryGetValue(hwnd, out long style))
                    Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);
                Win32.SetParent(hwnd, IntPtr.Zero);
                Win32.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 900, 700,
                    Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
                Win32.ShowWindow(hwnd, Win32.SW_SHOW);
            }

            foreach (var p in processes)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
            }
        }
    }

    private static void SpawnNotepads(int count, List<Process> processes, List<IntPtr> handles)
    {
        var baseline = EnumerateNotepadLikeWindows();

        for (int i = 0; i < count; i++)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                UseShellExecute = true
            })!;
            processes.Add(p);
        }

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline && handles.Count < count)
        {
            handles.Clear();
            foreach (var hWnd in EnumerateNotepadLikeWindows())
            {
                if (baseline.Contains(hWnd)) continue;
                if (Win32.IsWindow(hWnd) && !handles.Contains(hWnd))
                    handles.Add(hWnd);
            }
            Thread.Sleep(250);
        }
    }

    private static HashSet<IntPtr> EnumerateNotepadLikeWindows()
    {
        var windows = new HashSet<IntPtr>();
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Untitled", StringComparison.OrdinalIgnoreCase))
            {
                windows.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static void EmbedAndResizeWithRetries(IntPtr hwnd, Panel panel, IDictionary<IntPtr, long> originalStyles)
    {
        long originalStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        originalStyles[hwnd] = originalStyle;

        long style = originalStyle;
        style &= ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME |
                   Win32.WS_BORDER | Win32.WS_SYSMENU |
                   Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
        style |= Win32.WS_CHILD | Win32.WS_CLIPSIBLINGS;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (IntPtr)style);

        long exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        exStyle &= ~(Win32.WS_EX_APPWINDOW | Win32.WS_EX_WINDOWEDGE |
                     Win32.WS_EX_CLIENTEDGE | Win32.WS_EX_DLGMODALFRAME);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)exStyle);

        Win32.SetParent(hwnd, panel.Handle);
        ForceResizeAll(hwnd, panel.Width, panel.Height);
        Thread.Sleep(50);
        ForceResizeAll(hwnd, panel.Width, panel.Height);
        Thread.Sleep(150);
        ForceResizeAll(hwnd, panel.Width, panel.Height);
        Thread.Sleep(300);
        ForceResizeAll(hwnd, panel.Width, panel.Height);
    }

    private static void ForceResizeAll(IntPtr hwnd, int width, int height)
    {
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
        Win32.MoveWindow(hwnd, 0, 0, width, height, true);

        Win32.EnumChildWindows(hwnd, (child, _) =>
        {
            Win32.MoveWindow(child, 0, 0, width, height, true);
            return true;
        }, IntPtr.Zero);
    }

    private static void AssertWindowFillsPanel(IntPtr hwnd, Panel panel, int scenarioCount, int index)
    {
        Win32.GetClientRect(hwnd, out var clientRect);
        int widthDiff = Math.Abs(clientRect.Width - panel.Width);
        int heightDiff = Math.Abs(clientRect.Height - panel.Height);

        if (widthDiff > Tolerance || heightDiff > Tolerance)
        {
            throw new Exception(
                $"Scenario {scenarioCount}, window {index}: panel={panel.Width}x{panel.Height}, " +
                $"client={clientRect.Width}x{clientRect.Height}, diff=({widthDiff},{heightDiff})");
        }
    }
}
