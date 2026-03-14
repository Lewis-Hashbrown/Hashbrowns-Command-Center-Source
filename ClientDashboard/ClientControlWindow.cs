using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClientDashboard;

public sealed class ClientControlWindow : Window
{
    private static readonly string[] ToolWindowTitles =
    {
        "Script Manager",
        "Console",
        "Account Manager",
        "Client settings"
    };

    private readonly IntPtr _hwnd;
    private readonly IntPtr _dashboardHwnd;
    private readonly DispatcherTimer _followTimer;
    private bool _finishedRequested;

    public ClientControlWindow(IntPtr hwnd, IntPtr dashboardHwnd)
    {
        _hwnd = hwnd;
        _dashboardHwnd = dashboardHwnd;

        Title = "Finished";
        Width = 120;
        Height = 34;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Red;

        var root = new DockPanel { Margin = new Thickness(0) };
        var doneBtn = new System.Windows.Controls.Button
        {
            Content = "Finished",
            Padding = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = System.Windows.Media.Brushes.Red,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.DarkRed,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.Bold
        };
        doneBtn.Click += (_, _) =>
        {
            _finishedRequested = true;
            Close();
        };
        root.Children.Add(doneBtn);

        Content = root;

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _followTimer.Tick += (_, _) =>
        {
            if (!NativeMethods.IsWindow(_hwnd))
            {
                Close();
                return;
            }
            PositionNearClientWindow();
        };

        Loaded += (_, _) =>
        {
            PositionNearClientWindow();
            BeginControlSession();
            _followTimer.Start();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _followTimer.Stop();
        if (_finishedRequested)
            CloseAssociatedToolWindows();
        if (_dashboardHwnd != IntPtr.Zero && NativeMethods.IsWindow(_dashboardHwnd))
            NativeMethods.SetForegroundWindow(_dashboardHwnd);
        base.OnClosed(e);
    }

    private void BeginControlSession()
    {
        if (!NativeMethods.IsWindow(_hwnd))
        {
            Close();
            return;
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private void PositionNearClientWindow()
    {
        if (!NativeMethods.GetWindowRect(_hwnd, out var rect))
            return;

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new System.Windows.Point(rect.Left, rect.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
        double clientLeft = topLeft.X;
        double clientTop = topLeft.Y;
        double clientWidth = Math.Max(1, bottomRight.X - topLeft.X);
        double clientBottom = bottomRight.Y;
        _ = clientBottom;

        var work = SystemParameters.WorkArea;
        double targetLeft = clientLeft + clientWidth - Width - 6.0;
        double targetTop = clientTop + 6.0;

        if (targetLeft < work.Left) targetLeft = work.Left;
        if (targetLeft + Width > work.Right) targetLeft = work.Right - Width;
        if (targetTop + Height > work.Bottom) targetTop = work.Bottom - Height;
        if (targetTop < work.Top) targetTop = work.Top;

        Left = targetLeft;
        Top = targetTop;
    }

    private void CloseAssociatedToolWindows()
    {
        if (!NativeMethods.IsWindow(_hwnd))
            return;

        NativeMethods.GetWindowThreadProcessId(_hwnd, out uint clientPid);
        if (clientPid == 0)
            return;

        var windowsToClose = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != clientPid)
                return true;

            string title = NativeMethods.GetWindowTitle(hWnd);
            bool isToolWindow = ToolWindowTitles.Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase));
            if (isToolWindow)
                windowsToClose.Add(hWnd);

            return true;
        }, IntPtr.Zero);

        foreach (var toolHwnd in windowsToClose)
            NativeMethods.PostMessage(toolHwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
