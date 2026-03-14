using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace AutoTest;

/// <summary>
/// Simulates the dashboard grid layout with multiple embedded windows
/// and reports actual tile/window sizes to diagnose rendering issues.
/// </summary>
public static class DiagnosticTest
{
    public static void RunMultiWindowTest(int windowCount)
    {
        Console.WriteLine($"\n=== Multi-Window Embed Diagnostic ({windowCount} windows) ===\n");

        // Spawn Notepad instances as stand-ins
        var processes = new List<Process>();
        var handles = new List<IntPtr>();

        for (int i = 0; i < windowCount; i++)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                UseShellExecute = true
            })!;
            processes.Add(p);
        }

        Thread.Sleep(2500);

        // Find all notepad windows
        var notepadHandles = new List<IntPtr>();
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var title = Win32.GetWindowTitle(hWnd);
            if (title.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Untitled", StringComparison.OrdinalIgnoreCase))
            {
                notepadHandles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        // Take only as many as we need
        for (int i = 0; i < Math.Min(windowCount, notepadHandles.Count); i++)
            handles.Add(notepadHandles[i]);

        Console.WriteLine($"  Found {handles.Count} Notepad windows");

        if (handles.Count < windowCount)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  WARNING: Only found {handles.Count} of {windowCount} requested windows");
            Console.ResetColor();
        }

        // Simulate dashboard layout: 1280x720 window, 50px top bar, 4px padding
        int dashWidth = 1280;
        int dashHeight = 720;
        int topBar = 50;
        int padding = 8; // 4px border padding * 2
        int contentW = dashWidth - padding;
        int contentH = dashHeight - topBar - padding;

        // Calculate columns (same as updated dashboard)
        int cols = handles.Count switch
        {
            0 => 1,
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 2,
            <= 6 => 3,
            <= 9 => 3,
            _ => 4
        };
        int rows = (int)Math.Ceiling(handles.Count / (double)cols);

        int tileW = contentW / cols;
        int tileH = contentH / rows;

        Console.WriteLine($"  Dashboard: {dashWidth}x{dashHeight}");
        Console.WriteLine($"  Content area: {contentW}x{contentH}");
        Console.WriteLine($"  Grid: {cols} cols x {rows} rows");
        Console.WriteLine($"  Tile size: {tileW}x{tileH}");
        Console.WriteLine();

        // Create a host form simulating the dashboard content area
        using var hostForm = new Form
        {
            Width = contentW + 16, // Form chrome
            Height = contentH + 39,
            Text = $"Dashboard Simulator ({windowCount} clients)",
            StartPosition = FormStartPosition.CenterScreen
        };

        // Create grid of panels
        var panels = new List<Panel>();
        for (int i = 0; i < handles.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            var panel = new Panel
            {
                Left = col * tileW,
                Top = row * tileH,
                Width = tileW,
                Height = tileH,
                BorderStyle = BorderStyle.FixedSingle
            };
            hostForm.Controls.Add(panel);
            panels.Add(panel);
        }

        hostForm.Show();
        Thread.Sleep(300);

        // Embed each window
        var origStyles = new List<long>();
        for (int i = 0; i < handles.Count; i++)
        {
            var hwnd = handles[i];
            var panel = panels[i];

            long origStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            origStyles.Add(origStyle);

            // Strip styles (same as dashboard EmbedClient)
            long style = origStyle;
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
            Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, panel.Width, panel.Height,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
            Win32.MoveWindow(hwnd, 0, 0, panel.Width, panel.Height, true);
        }

        Thread.Sleep(500);

        // Report actual sizes
        Console.WriteLine("  Embedded window sizes:");
        for (int i = 0; i < handles.Count; i++)
        {
            var hwnd = handles[i];
            var panel = panels[i];
            Win32.GetWindowRect(hwnd, out var wr);
            Win32.GetClientRect(hwnd, out var cr);
            Console.WriteLine($"    Window {i + 1}: panel={panel.Width}x{panel.Height}, " +
                              $"windowRect={wr.Width}x{wr.Height}, clientRect={cr.Width}x{cr.Height}");

            bool widthOk = Math.Abs(cr.Width - panel.Width) <= 5;
            bool heightOk = Math.Abs(cr.Height - panel.Height) <= 5;

            if (!widthOk || !heightOk)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"             SIZE MISMATCH! Panel: {panel.Width}x{panel.Height}, Client: {cr.Width}x{cr.Height}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"             OK - window fills tile");
                Console.ResetColor();
            }
        }

        // Also test with maximized form (simulating maximized dashboard)
        Console.WriteLine();
        Console.WriteLine("  --- Testing with MAXIMIZED dashboard simulation ---");
        hostForm.WindowState = FormWindowState.Maximized;
        Thread.Sleep(300);

        // Resize panels for maximized
        var screen = Screen.PrimaryScreen!.WorkingArea;
        int maxContentW = screen.Width - padding;
        int maxContentH = screen.Height - topBar - padding;
        int maxTileW = maxContentW / cols;
        int maxTileH = maxContentH / rows;

        Console.WriteLine($"  Maximized content: {maxContentW}x{maxContentH}");
        Console.WriteLine($"  Maximized tile: {maxTileW}x{maxTileH}");

        for (int i = 0; i < handles.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            panels[i].SetBounds(col * maxTileW, row * maxTileH, maxTileW, maxTileH);
            Win32.MoveWindow(handles[i], 0, 0, maxTileW, maxTileH, true);
        }

        Thread.Sleep(500);

        Console.WriteLine("  Maximized embedded window sizes:");
        for (int i = 0; i < handles.Count; i++)
        {
            var hwnd = handles[i];
            var panel = panels[i];
            Win32.GetClientRect(hwnd, out var cr);
            Console.WriteLine($"    Window {i + 1}: panel={panel.Width}x{panel.Height}, clientRect={cr.Width}x{cr.Height}");
        }

        // Release all
        for (int i = 0; i < handles.Count; i++)
        {
            Win32.SetWindowLongPtr(handles[i], Win32.GWL_STYLE, (IntPtr)origStyles[i]);
            Win32.SetParent(handles[i], IntPtr.Zero);
            Win32.SetWindowPos(handles[i], IntPtr.Zero, 100 + i * 50, 100 + i * 50, 800, 600,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER);
            Win32.ShowWindow(handles[i], Win32.SW_SHOW);
        }

        hostForm.Close();

        // Kill notepads
        foreach (var p in processes)
            try { p.Kill(); } catch { }
        // Also kill by handle
        foreach (var h in handles)
        {
            try
            {
                Win32.GetWindowThreadProcessId(h, out uint pid);
                if (pid != 0) Process.GetProcessById((int)pid).Kill();
            }
            catch { }
        }
    }
}
