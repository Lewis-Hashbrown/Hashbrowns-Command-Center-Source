using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ClientDashboard;

namespace AutoTest.Tests;

public static class LauncherAutomationFlowTests
{
    public static void Run(TestRunner runner)
    {
        runner.Run("Launcher: auto-click opens DreamBot client window", RunMockLauncherFlow);
    }

    private static void RunMockLauncherFlow()
    {
        using var launcherForm = new Form
        {
            Width = 480,
            Height = 360,
            Text = "Dreambot launcher",
            StartPosition = FormStartPosition.CenterScreen
        };

        using var launchBtn = new Button
        {
            Text = "Launch",
            Width = 120,
            Height = 40,
            Left = 180,
            Top = 250
        };

        Form? clientForm = null;
        bool clicked = false;
        launchBtn.Click += (_, _) =>
        {
            clicked = true;
            clientForm = new Form
            {
                Width = 800,
                Height = 600,
                Text = "DreamBot 999999",
                StartPosition = FormStartPosition.CenterScreen
            };
            clientForm.Show();
        };

        launcherForm.Controls.Add(launchBtn);
        launcherForm.Show();

        var automation = new LauncherAutomation();
        var launcherHwnd = WaitForLauncherWindow(automation, TimeSpan.FromSeconds(5));
        Assert.IsTrue(launcherHwnd != IntPtr.Zero, "Mock launcher window was not detected");

        automation.TryClickLaunch(launcherHwnd);

        var clickDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < clickDeadline && !clicked)
        {
            Application.DoEvents();
            Thread.Sleep(40);
        }

        Assert.IsTrue(clicked, "Launcher automation did not click Launch");

        var clientDetected = WaitForWindowTitleContains("DreamBot 999999", TimeSpan.FromSeconds(5));
        Assert.IsTrue(clientDetected, "DreamBot client window did not appear after launcher click");

        if (clientForm != null && !clientForm.IsDisposed)
            clientForm.Close();
        launcherForm.Close();
    }

    private static IntPtr WaitForLauncherWindow(LauncherAutomation automation, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            var match = automation.FindLauncherWindows().FirstOrDefault();
            if (match != IntPtr.Zero) return match;
            Thread.Sleep(40);
        }
        return IntPtr.Zero;
    }

    private static bool WaitForWindowTitleContains(string titlePart, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            bool found = false;
            Win32.EnumWindows((hWnd, _) =>
            {
                if (!Win32.IsWindowVisible(hWnd)) return true;
                var title = Win32.GetWindowTitle(hWnd);
                if (title.Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found) return true;

            Application.DoEvents();
            Thread.Sleep(40);
        }
        return false;
    }
}
