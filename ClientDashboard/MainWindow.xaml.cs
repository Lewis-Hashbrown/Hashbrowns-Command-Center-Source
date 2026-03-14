using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;

namespace ClientDashboard;

public partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly AppConfig _config;
    private readonly ClientDetector _detector = new();
    private readonly Dictionary<IntPtr, WindowsFormsHost> _embeddedClients = new();
    private readonly Dictionary<IntPtr, System.Windows.Forms.Panel> _clientPanels = new();
    private readonly Dictionary<IntPtr, Border> _scaledClients = new();
    private readonly Dictionary<IntPtr, (int Width, int Height)> _scaledSourceSizes = new();
    private readonly Dictionary<IntPtr, IntPtr> _clientToolWindows = new();
    private readonly Dictionary<IntPtr, IntPtr> _toolToClient = new();
    private readonly Dictionary<IntPtr, long> _originalTaskbarExStyles = new();
    private readonly HashSet<IntPtr> _manuallyMutedClients = new();
    private readonly HashSet<IntPtr> _manuallyUnmutedClients = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _captureTimer;
    private readonly DispatcherTimer _usageTimer;
    private PerformanceCounter? _cpuTotalCounter;
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private readonly LauncherAutomation _launcherAutomation = new();
    private readonly AudioMuteManager _audioMuteManager = new();
    private readonly HashSet<IntPtr> _autoClickedLaunchers = new();
    private readonly bool _autoLaunchRequested;
    private bool _forceScaleMode = true;
    private IntPtr _selectedClientHwnd = IntPtr.Zero;
    private IntPtr _controlledClientHwnd = IntPtr.Zero;
    private ClientControlWindow? _activeControlWindow;
    private DispatcherTimer? _tileClickAttentionTimer;
    private int _tileClickAttentionTicks;
    private System.Windows.Media.Brush? _tileClickControlDefaultBackground;
    private bool _tileClickAttentionActive;
    private int _currentPage;
    private bool _closePromptShown;
    private const int MinInteractiveTileWidth = 760;
    private const int MinInteractiveTileHeight = 500;

    public MainWindow()
    {
        InitializeComponent();
        _config = AppConfig.Load();
        _autoLaunchRequested = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--autolaunch", StringComparison.OrdinalIgnoreCase));

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();

        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _captureTimer.Tick += CaptureTimer_Tick;
        _captureTimer.Start();

        _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _usageTimer.Tick += (_, _) => UpdateUsageLabel();
        _usageTimer.Start();

        Loaded += (_, _) =>
        {
            _tileClickControlDefaultBackground = TileClickControlBtn.Background;
            ApplySettings(_config);
            if (!EnsureDreamBotPathConfiguredOnStartup())
                return;
            PrimeUsageSampling();
            UpdateUsageLabel();
            Dispatcher.BeginInvoke(new Action(UpdateTopBarBrandSizing), DispatcherPriority.Loaded);
            if (_autoLaunchRequested)
                LaunchBtn_Click(this, new RoutedEventArgs());
        };

        SizeChanged += (_, _) =>
        {
            RebuildGrid();
            UpdateTopBarBrandSizing();
        };
    }

    private bool EnsureDreamBotPathConfiguredOnStartup()
    {
        bool needsPrompt = string.IsNullOrWhiteSpace(_config.DreamBotPath) || !System.IO.File.Exists(_config.DreamBotPath);
        if (!needsPrompt)
            return true;

        var intro = WpfMessageBox.Show(
            "DreamBot JAR path is not set.\n\nSelect your DreamBot .jar now?",
            "First-Time Setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (intro != MessageBoxResult.Yes)
            return true;

        while (true)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select DreamBot JAR",
                Filter = "JAR files (*.jar)|*.jar",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = @"C:\"
            };

            if (dialog.ShowDialog(this) == true)
            {
                if (System.IO.File.Exists(dialog.FileName))
                {
                    _config.DreamBotPath = dialog.FileName;
                    _config.Save();
                    return true;
                }

                WpfMessageBox.Show(
                    "Selected file does not exist.",
                    "Invalid Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                continue;
            }

            var cancelResult = WpfMessageBox.Show(
                "No DreamBot JAR selected.\n\nYes: continue without setting path\nNo: choose file again",
                "First-Time Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (cancelResult == MessageBoxResult.Yes)
                return true;
        }
    }

    /// <summary>
    /// Resolves the DreamBot client.jar path from the configured DreamBot path.
    /// DreamBot stores client.jar in ~/DreamBot/BotData/client.jar
    /// </summary>
    private static string? ResolveClientJar()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var clientJar = System.IO.Path.Combine(userHome, "DreamBot", "BotData", "client.jar");
        return System.IO.File.Exists(clientJar) ? clientJar : null;
    }

    /// <summary>
    /// Resolves the bundled JRE path from DreamBot's installation.
    /// </summary>
    private static string? ResolveDreamBotJava()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var jreDir = System.IO.Path.Combine(userHome, "DreamBot", "BotData");
        // Find the JRE directory (jre11.x.x+y-64)
        if (System.IO.Directory.Exists(jreDir))
        {
            foreach (var dir in System.IO.Directory.GetDirectories(jreDir, "jre*"))
            {
                var javaExe = System.IO.Path.Combine(dir, "bin", "javaw.exe");
                if (System.IO.File.Exists(javaExe)) return javaExe;
                javaExe = System.IO.Path.Combine(dir, "bin", "java.exe");
                if (System.IO.File.Exists(javaExe)) return javaExe;
            }
        }
        return null;
    }

    private void LaunchBtn_Click(object sender, RoutedEventArgs e)
    {
        const string launchGuide =
            "To launch multiple clients, use DreamBot Launcher:\n\n" +
            "Launch Queue -> Show Queue -> Add (repeat for as many clients as you want) -> Start Queue.\n\n" +
            "Click OK to open the launcher.";
        var proceed = WpfMessageBox.Show(
            launchGuide,
            "Launch DreamBot",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (proceed != MessageBoxResult.OK)
            return;

        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(_config.DreamBotPath) &&
            System.IO.File.Exists(_config.DreamBotPath))
        {
            if (TryLaunchJar(_config.DreamBotPath, errors))
                return;
        }

        var clientJar = ResolveClientJar();
        if (!string.IsNullOrWhiteSpace(clientJar) && TryLaunchJar(clientJar, errors))
            return;

        var message = "Failed to launch DreamBot.\n\n" +
                      (errors.Count > 0 ? string.Join("\n", errors) : "No launch candidates were found.") +
                      "\n\nOpen Settings (gear icon) and set the correct DreamBot .jar path.";
        WpfMessageBox.Show(message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static bool TryLaunchJar(string jarPath, List<string> errors)
    {
        var workingDir = System.IO.Path.GetDirectoryName(jarPath) ?? Environment.CurrentDirectory;
        var javaCandidates = new[]
        {
            ResolveDreamBotJava(),
            "javaw",
            "java"
        }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var java in javaCandidates)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = java!,
                    Arguments = $"-jar \"{jarPath}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process != null)
                    return true;
                errors.Add($"Start returned null for executable '{java}'.");
            }
            catch (Exception ex)
            {
                errors.Add($"'{java}' failed: {ex.Message}");
            }
        }

        // Last resort: rely on shell file association for .jar
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = jarPath,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            });
            if (process != null)
                return true;
            errors.Add($"Shell-open returned null for '{jarPath}'.");
        }
        catch (Exception ex)
        {
            errors.Add($"Shell-open failed for '{jarPath}': {ex.Message}");
        }

        return false;
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true && settingsWindow.SavedConfig != null)
        {
            var saved = settingsWindow.SavedConfig;
            _config.DreamBotPath = saved.DreamBotPath;
            _config.AutoAcceptClientControl = saved.AutoAcceptClientControl;
            _config.EnableTileClickControl = saved.EnableTileClickControl;
            _config.EnableTileClickAttentionFlash = saved.EnableTileClickAttentionFlash;
            _config.PreviewMaxFps = saved.PreviewMaxFps;
            _config.MaxTilesPerPage = saved.MaxTilesPerPage;
            _config.GridColumnsOverride = saved.GridColumnsOverride;
            _config.ScanIntervalMs = saved.ScanIntervalMs;
            _config.CaptureIntervalMs = saved.CaptureIntervalMs;
            _config.MuteAllExceptControlled = saved.MuteAllExceptControlled;
            _config.TileAspectMode = saved.TileAspectMode;
            _config.HideManagedClientsFromTaskbar = saved.HideManagedClientsFromTaskbar;
            _config.Normalize();
            _config.Save();
            ApplySettings(_config);
            RebuildGrid();
            RefreshCurrentPageScaledTiles();
        }
    }

    private void ApplySettings(AppConfig cfg)
    {
        cfg.Normalize();
        UpdateTileClickControlButtonText();
        UpdateMuteAllButtonText();

        _pollTimer.Interval = TimeSpan.FromMilliseconds(cfg.ScanIntervalMs);

        int captureMs = cfg.CaptureIntervalMs > 0
            ? cfg.CaptureIntervalMs
            : Math.Max(33, (int)Math.Round(1000.0 / Math.Max(1, cfg.PreviewMaxFps)));
        _captureTimer.Interval = TimeSpan.FromMilliseconds(captureMs);

        ApplyTileStretchModeToAllTiles();
        ApplyTaskbarVisibilityToAllTrackedWindows();
        UpdateAudioMuteState();
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        // Remove dead windows
        IEnumerable<IntPtr> tracked = _forceScaleMode
            ? _scaledClients.Keys
            : _embeddedClients.Keys;
        var dead = tracked
            .Where(h => !NativeMethods.IsWindow(h))
            .ToList();
        bool addedAny = false;
        foreach (var h in dead)
        {
            // Find and remove from grid
            if (_embeddedClients.TryGetValue(h, out var host))
            {
                foreach (var child in TileGrid.Children.OfType<WindowsFormsHost>().ToList())
                {
                    if (child == host)
                        TileGrid.Children.Remove(child);
                }
                host.Dispose();
                _embeddedClients.Remove(h);
            }
            if (_scaledClients.ContainsKey(h))
                _scaledClients.Remove(h);
            if (_scaledSourceSizes.ContainsKey(h))
                _scaledSourceSizes.Remove(h);
            RestoreTaskbarStyle(h);
            foreach (var tool in _toolToClient.Where(kvp => kvp.Value == h).Select(kvp => kvp.Key).ToList())
                RemoveToolMapping(tool);
            _clientPanels.Remove(h);
            _manuallyMutedClients.Remove(h);
            _manuallyUnmutedClients.Remove(h);
            if (_controlledClientHwnd == h)
                _controlledClientHwnd = IntPtr.Zero;
        }

        foreach (var tool in _toolToClient.Keys.Where(t => !NativeMethods.IsWindow(t)).ToList())
            RemoveToolMapping(tool);

        // Find new windows
        var found = _detector.FindClientWindows();
        foreach (var hwnd in found)
        {
            if (_embeddedClients.ContainsKey(hwnd) || _scaledClients.ContainsKey(hwnd)) continue;
            if (_forceScaleMode)
                AddScaledClient(hwnd);
            else
                EmbedClient(hwnd);
            ApplyTaskbarVisibilityForWindow(hwnd);
            addedAny = true;
        }

        if (dead.Count > 0 || addedAny)
            RebuildGrid();

        if (_forceScaleMode)
            HandleToolOverlays();

        // Auto-click DreamBot launcher if detected
        var launchers = _launcherAutomation.FindLauncherWindows();
        var newLaunchers = launchers.Where(l => !_autoClickedLaunchers.Contains(l)).ToList();
        if (newLaunchers.Count > 0)
        {
            foreach (var launcher in newLaunchers)
            {
                _launcherAutomation.TryClickLaunch(launcher);
                _autoClickedLaunchers.Add(launcher);
            }
        }
        // Clean dead launcher handles
        _autoClickedLaunchers.RemoveWhere(h => !NativeMethods.IsWindow(h));

        if (_forceScaleMode)
            RefreshCurrentPageScaledTiles();
        else
        {
            // DreamBot can recreate/reassert window styles while loading; keep enforcing embed state.
            foreach (var (hwnd, panel) in _clientPanels.ToList())
                EnsureEmbeddedState(hwnd, panel);
        }

        UpdateAudioMuteState();
        WriteLiveStatusSnapshot();
    }

    private void CaptureTimer_Tick(object? sender, EventArgs e)
    {
        if (!_forceScaleMode) return;
        RefreshCurrentPageScaledTiles();
    }

    private void RefreshCurrentPageScaledTiles()
    {
        foreach (var hwnd in GetCurrentPageHandles())
        {
            if (_scaledClients.TryGetValue(hwnd, out var tile))
                UpdateScaledTile(hwnd, tile);
        }
    }

    private void EmbedClient(IntPtr hwnd)
    {
        var host = new WindowsFormsHost();
        var panel = new System.Windows.Forms.Panel();
        host.Child = panel;

        panel.HandleCreated += (_, _) =>
        {
            // Strip window styles before reparenting
            StripWindowStyles(hwnd);

            // Reparent
            NativeMethods.SetParent(hwnd, panel.Handle);

            ResizeEmbeddedWindowWithRetries(hwnd, panel);
        };

        panel.Resize += (_, _) =>
        {
            if (NativeMethods.IsWindow(hwnd))
                ResizeEmbeddedWindowWithRetries(hwnd, panel);
        };

        _embeddedClients[hwnd] = host;
        _clientPanels[hwnd] = panel;
    }

    private void AddScaledClient(IntPtr hwnd)
    {
        var img = new System.Windows.Controls.Image
        {
            Stretch = GetTileStretchMode()
        };
        img.MouseLeftButtonDown += (_, e) =>
        {
            _selectedClientHwnd = hwnd;
            HandleScaledTileClick(hwnd, img, e.GetPosition(img));
        };
        var contextMenu = new System.Windows.Controls.ContextMenu();
        var muteItem = new System.Windows.Controls.MenuItem { Header = "Mute Client" };
        contextMenu.Opened += (_, _) =>
        {
            muteItem.Header = IsClientMutedByPolicy(hwnd) ? "Unmute Client" : "Mute Client";
        };
        muteItem.Click += (_, _) => ToggleClientManualMute(hwnd);
        var closeItem = new System.Windows.Controls.MenuItem { Header = "Close Client" };
        closeItem.Click += (_, _) => CloseClientByHandle(hwnd);
        contextMenu.Items.Add(muteItem);
        contextMenu.Items.Add(closeItem);
        img.ContextMenu = contextMenu;

        var tileGrid = new Grid();
        tileGrid.Children.Add(img);

        var tile = new Border
        {
            Background = System.Windows.Media.Brushes.Black,
            Child = tileGrid
        };
        _scaledClients[hwnd] = tile;
        UpdateScaledTile(hwnd, tile);
    }

    private async void CloseClientByHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;
        _manuallyMutedClients.Remove(hwnd);
        _manuallyUnmutedClients.Remove(hwnd);
        UpdateAudioMuteState();

        if (_clientToolWindows.TryGetValue(hwnd, out var toolHwnd) && NativeMethods.IsWindow(toolHwnd))
            NativeMethods.PostMessage(toolHwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        await Task.Delay(900);

        if (!NativeMethods.IsWindow(hwnd))
            return;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (!proc.HasExited)
                proc.Kill(true);
        }
        catch
        {
            // best effort
        }
    }

    private bool IsClientMutedByPolicy(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return false;

        if (_manuallyUnmutedClients.Contains(hwnd))
            return false;
        if (_manuallyMutedClients.Contains(hwnd))
            return true;

        if (!_config.MuteAllExceptControlled)
            return false;

        return hwnd != _controlledClientHwnd;
    }

    private void ToggleClientManualMute(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (IsClientMutedByPolicy(hwnd))
        {
            _manuallyMutedClients.Remove(hwnd);
            _manuallyUnmutedClients.Add(hwnd);
        }
        else
        {
            _manuallyUnmutedClients.Remove(hwnd);
            _manuallyMutedClients.Add(hwnd);
        }

        UpdateAudioMuteState();
    }

    private void UpdateScaledTile(IntPtr hwnd, Border tile)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        if (tile.Child is not Grid tileGrid) return;
        var img = tileGrid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
        if (img == null) return;
        IntPtr captureHwnd = GetCaptureHwndForClient(hwnd);
        var frame = WindowCapture.Capture(captureHwnd);
        if (frame == null) return;

        img.Source = frame.Image;
        _scaledSourceSizes[hwnd] = (frame.Width, frame.Height);
    }

    /// <summary>
    /// Strips all window chrome styles from a window and its children.
    /// </summary>
    private static void StripWindowStyles(IntPtr hwnd)
    {
        long style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        style &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION |
                    NativeMethods.WS_THICKFRAME | NativeMethods.WS_BORDER |
                    NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX |
                    NativeMethods.WS_MAXIMIZEBOX);
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

        long exStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle &= ~(NativeMethods.WS_EX_APPWINDOW | NativeMethods.WS_EX_WINDOWEDGE |
                      NativeMethods.WS_EX_CLIENTEDGE | NativeMethods.WS_EX_DLGMODALFRAME);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);
    }

    /// <summary>
    /// Force-resizes a window and ALL its child windows to the given dimensions.
    /// This is critical for Java AWT/Swing windows where the canvas child may enforce
    /// a minimum size larger than the parent frame.
    /// </summary>
    private static void ForceResizeAll(IntPtr hwnd, int width, int height)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.MoveWindow(hwnd, 0, 0, width, height, true);

        // Also force-resize all descendant windows to fill the frame.
        NativeMethods.EnumChildWindows(hwnd, (child, _) =>
        {
            NativeMethods.MoveWindow(child, 0, 0, width, height, true);
            return true;
        }, IntPtr.Zero);
    }

    private static async void ResizeEmbeddedWindowWithRetries(IntPtr hwnd, System.Windows.Forms.Control panel)
    {
        if (!panel.IsHandleCreated || panel.Width <= 0 || panel.Height <= 0 || !NativeMethods.IsWindow(hwnd))
            return;

        ForceResizeAll(hwnd, panel.Width, panel.Height);

        // Java/AWT clients may create child surfaces after initial embedding.
        foreach (var delayMs in new[] { 50, 150, 350 })
        {
            await Task.Delay(delayMs);
            if (!panel.IsHandleCreated || panel.Width <= 0 || panel.Height <= 0 || !NativeMethods.IsWindow(hwnd))
                return;
            ForceResizeAll(hwnd, panel.Width, panel.Height);
        }
    }

    private static void EnsureEmbeddedState(IntPtr hwnd, System.Windows.Forms.Panel panel)
    {
        if (!panel.IsHandleCreated || panel.Width <= 0 || panel.Height <= 0 || !NativeMethods.IsWindow(hwnd))
            return;

        StripWindowStyles(hwnd);

        IntPtr parent = NativeMethods.GetParent(hwnd);
        if (parent != panel.Handle)
            NativeMethods.SetParent(hwnd, panel.Handle);

        ForceResizeAll(hwnd, panel.Width, panel.Height);
    }

    private void RebuildGrid()
    {
        int total = _forceScaleMode ? _scaledClients.Count : _embeddedClients.Count;
        ClientCountLabel.Text = $"{total} client{(total == 1 ? "" : "s")}";

        GetLayoutMetrics(total, out var cols, out var perPage, out var totalPages);
        bool needsPaging = total > perPage;
        PrevPageBtn.Visibility = needsPaging ? Visibility.Visible : Visibility.Collapsed;
        NextPageBtn.Visibility = needsPaging ? Visibility.Visible : Visibility.Collapsed;
        PageLabel.Visibility = needsPaging ? Visibility.Visible : Visibility.Collapsed;

        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);
        PageLabel.Text = $"Page {_currentPage + 1} / {totalPages}";

        var pageHandles = GetCurrentPageHandles();

        int count = pageHandles.Count;

        TileGrid.Columns = cols;
        TileGrid.Children.Clear();

        if (_forceScaleMode)
        {
            foreach (var hwnd in pageHandles)
                TileGrid.Children.Add(_scaledClients[hwnd]);
        }
        else
        {
            foreach (var hwnd in pageHandles)
                TileGrid.Children.Add(_embeddedClients[hwnd]);
        }

        UpdateTopBarBrandSizing();
    }

    private void UpdateTopBarBrandSizing()
    {
        if (!IsLoaded)
            return;

        double total = TopBarRootGrid.ActualWidth;
        double left = LeftTopBarPanel.ActualWidth;
        double right = RightTopBarPanel.ActualWidth;
        const double horizontalBuffer = 24;

        double available = total - left - right - horizontalBuffer;
        if (available <= 140)
        {
            BrandTitleViewbox.Visibility = Visibility.Collapsed;
            return;
        }

        BrandTitleViewbox.Visibility = Visibility.Visible;
        BrandTitleViewbox.MaxWidth = Math.Clamp(available, 220, 900);
    }

    private void PrevPageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0) { _currentPage--; RebuildGrid(); }
    }

    private void NextPageBtn_Click(object sender, RoutedEventArgs e)
    {
        int totalCount = _forceScaleMode ? _scaledClients.Count : _embeddedClients.Count;
        GetLayoutMetrics(totalCount, out _, out _, out var totalPages);
        if (_currentPage < totalPages - 1) { _currentPage++; RebuildGrid(); }
    }

    private void CloseAllBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseTrackedDreamBotClients();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closePromptShown)
            return;

        _closePromptShown = true;
        var result = WpfMessageBox.Show(
            "Do you want to close all DreamBot clients as well?\n\nYes: close dashboard + all DreamBot clients\nNo: close dashboard only\nCancel: keep dashboard open",
            "Close Dashboard",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            _closePromptShown = false;
            return;
        }

        if (result == MessageBoxResult.Yes)
            CloseTrackedDreamBotClients();
    }

    private void CloseTrackedDreamBotClients()
    {
        var handles = _detector.FindClientWindows();
        var pids = new HashSet<int>();
        foreach (var hwnd in handles)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0) pids.Add((int)pid);
        }

        foreach (var pid in pids)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill(true);
            }
            catch
            {
                // best effort
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop();
        _captureTimer.Stop();
        _usageTimer.Stop();
        DisposeGpuCounters();
        if (_tileClickAttentionTimer != null)
            _tileClickAttentionTimer.Stop();
        _config.MuteAllExceptControlled = false;
        UpdateAudioMuteState();
        RestoreTaskbarStyleForAllTrackedWindows();
        WriteLiveStatusSnapshot();
        ReleaseAllEmbeddedWindows();
        foreach (var tool in _toolToClient.Keys.ToList())
            RemoveToolMapping(tool);
        _scaledClients.Clear();
        _scaledSourceSizes.Clear();
        _clientPanels.Clear();
        base.OnClosed(e);
    }

    private void WriteLiveStatusSnapshot()
    {
        var snapshot = new LiveStatusSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            TotalClients = _forceScaleMode ? _scaledClients.Count : _embeddedClients.Count
        };

        if (_forceScaleMode)
        {
            foreach (var (hwnd, tile) in _scaledClients)
            {
                int panelW = (int)Math.Max(0, tile.ActualWidth);
                int panelH = (int)Math.Max(0, tile.ActualHeight);
                snapshot.Clients.Add(new LiveClientStatus
                {
                    Hwnd = hwnd.ToInt64(),
                    Title = NativeMethods.GetWindowTitle(hwnd),
                    PanelWidth = panelW,
                    PanelHeight = panelH,
                    ClientWidth = panelW,
                    ClientHeight = panelH
                });
            }

            LiveStatusWriter.WriteSnapshot(snapshot);
            return;
        }

        foreach (var (hwnd, panel) in _clientPanels)
        {
            int clientW = 0;
            int clientH = 0;
            if (NativeMethods.IsWindow(hwnd) && NativeMethods.GetClientRect(hwnd, out var rect))
            {
                clientW = rect.Width;
                clientH = rect.Height;
            }

            snapshot.Clients.Add(new LiveClientStatus
            {
                Hwnd = hwnd.ToInt64(),
                Title = NativeMethods.GetWindowTitle(hwnd),
                PanelWidth = panel.Width,
                PanelHeight = panel.Height,
                ClientWidth = clientW,
                ClientHeight = clientH
            });
        }

        LiveStatusWriter.WriteSnapshot(snapshot);
    }

    private void ReleaseAllEmbeddedWindows()
    {
        foreach (var hwnd in _embeddedClients.Keys.ToList())
        {
            if (NativeMethods.IsWindow(hwnd))
            {
                long style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
                style &= ~NativeMethods.WS_CHILD;
                style |= NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION |
                          NativeMethods.WS_THICKFRAME | NativeMethods.WS_SYSMENU |
                          NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX;
                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, (IntPtr)style);

                NativeMethods.SetParent(hwnd, IntPtr.Zero);
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 800, 600,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER);
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }
        }

        foreach (var host in _embeddedClients.Values)
            host.Dispose();
        _embeddedClients.Clear();
    }

    private void HandleToolOverlays()
    {
        var toolWindows = _detector.FindToolWindows();

        foreach (var toolHwnd in toolWindows)
        {
            if (_toolToClient.ContainsKey(toolHwnd)) continue;

            IntPtr owner = ResolveOverlayOwner(toolHwnd);
            if (owner == IntPtr.Zero || !_scaledClients.ContainsKey(owner)) continue;

            _toolToClient[toolHwnd] = owner;
            _clientToolWindows[owner] = toolHwnd;
        }

        foreach (var tool in _toolToClient.Keys.ToList())
        {
            if (!NativeMethods.IsWindow(tool))
            {
                RemoveToolMapping(tool);
                continue;
            }

            if (!toolWindows.Contains(tool))
            {
                RemoveToolMapping(tool);
                continue;
            }

            if (!_toolToClient.TryGetValue(tool, out var owner) || !_scaledClients.ContainsKey(owner))
            {
                RemoveToolMapping(tool);
            }
        }
    }

    private void HandleScaledTileClick(IntPtr hwnd, FrameworkElement imageElement, System.Windows.Point clickPos)
    {
        _selectedClientHwnd = hwnd;
        NativeMethods.POINT? clickScreenPoint = TryMapScaledClickToScreenPoint(hwnd, imageElement, clickPos);

        if (!_config.EnableTileClickControl)
        {
            TriggerTileClickControlAttention();
            return;
        }

        OpenControlFromTileWithPrompt(hwnd, clickScreenPoint);
    }

    private void OpenControlFromTileWithPrompt(IntPtr hwnd, NativeMethods.POINT? clickScreenPoint)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        if (_config.AutoAcceptClientControl)
        {
            OpenControlWindowForClient(hwnd, clickScreenPoint);
            return;
        }

        var title = NativeMethods.GetWindowTitle(hwnd);
        var dialog = new ClientControlConfirmDialog(title, _config.AutoAcceptClientControl)
        {
            Owner = this
        };

        bool? confirmed = dialog.ShowDialog();
        if (confirmed != true)
            return;

        if (dialog.AutoAcceptChecked != _config.AutoAcceptClientControl)
        {
            _config.AutoAcceptClientControl = dialog.AutoAcceptChecked;
            _config.Save();
        }

        OpenControlWindowForClient(hwnd, clickScreenPoint);
    }

    private void OpenControlWindowForClient(IntPtr hwnd, NativeMethods.POINT? clickScreenPoint = null)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        if (_activeControlWindow is { IsVisible: true })
            _activeControlWindow.Close();

        var dashboardHwnd = IntPtr.Zero;
        var interop = new System.Windows.Interop.WindowInteropHelper(this);
        if (interop.Handle != IntPtr.Zero)
            dashboardHwnd = interop.Handle;

        var controlWindow = new ClientControlWindow(hwnd, dashboardHwnd);
        _activeControlWindow = controlWindow;
        _controlledClientHwnd = hwnd;
        UpdateAudioMuteState();
        controlWindow.Closed += (_, _) =>
        {
            _activeControlWindow = null;
            _controlledClientHwnd = IntPtr.Zero;
            UpdateAudioMuteState();
        };
        controlWindow.Show();

        _ = clickScreenPoint;
    }

    private void TileClickControlBtn_Click(object sender, RoutedEventArgs e)
    {
        _config.EnableTileClickControl = !_config.EnableTileClickControl;
        _config.Save();
        UpdateTileClickControlButtonText();
    }

    private void MuteAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _config.MuteAllExceptControlled = !_config.MuteAllExceptControlled;
        _config.Save();
        UpdateMuteAllButtonText();
        // Global toggle is authoritative: reset all per-client mute overrides each time.
        _manuallyMutedClients.Clear();
        _manuallyUnmutedClients.Clear();

        UpdateAudioMuteState();
    }

    private IntPtr ResolveOverlayOwner(IntPtr toolHwnd)
    {
        if (_selectedClientHwnd != IntPtr.Zero && _scaledClients.ContainsKey(_selectedClientHwnd))
            return _selectedClientHwnd;

        NativeMethods.GetWindowThreadProcessId(toolHwnd, out uint toolPid);
        if (toolPid == 0) return IntPtr.Zero;

        foreach (var client in _scaledClients.Keys)
        {
            NativeMethods.GetWindowThreadProcessId(client, out uint clientPid);
            if (clientPid == toolPid)
                return client;
        }
        return IntPtr.Zero;
    }

    private void RemoveToolMapping(IntPtr toolHwnd)
    {
        if (_toolToClient.TryGetValue(toolHwnd, out var owner))
        {
            if (_clientToolWindows.TryGetValue(owner, out var mappedTool) && mappedTool == toolHwnd)
                _clientToolWindows.Remove(owner);
        }
        _toolToClient.Remove(toolHwnd);
    }

    private IntPtr GetCaptureHwndForClient(IntPtr clientHwnd)
    {
        if (_forceScaleMode &&
            _clientToolWindows.TryGetValue(clientHwnd, out var toolHwnd) &&
            NativeMethods.IsWindow(toolHwnd))
        {
            return toolHwnd;
        }

        return clientHwnd;
    }

    private HashSet<int> GetTrackedClientPids()
    {
        var pids = new HashSet<int>();
        var trackedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handles = _scaledClients.Keys.Concat(_embeddedClients.Keys).Distinct().ToList();
        foreach (var hwnd in handles)
        {
            if (!NativeMethods.IsWindow(hwnd))
                continue;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0)
            {
                int processId = (int)pid;
                pids.Add(processId);
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    if (!string.IsNullOrWhiteSpace(proc.ProcessName))
                        trackedProcessNames.Add(proc.ProcessName);
                }
                catch
                {
                    // ignore dead/inaccessible process
                }
            }
        }

        // Include descendants because DreamBot UI and audio sessions can live in child java/javaw processes
        // (shown in mixer as "OpenJDK Platform Binary"), not the top-level tracked window PID.
        foreach (var childPid in GetDescendantProcessIds(pids, maxDepth: 6))
            pids.Add(childPid);

        // Broaden matching to sibling processes with same executable name.
        // DreamBot clients often run as multiple java/javaw processes and sessions may attach variably.
        if (trackedProcessNames.Count > 0)
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (trackedProcessNames.Contains(proc.ProcessName))
                        pids.Add(proc.Id);
                }
                catch
                {
                    // ignore inaccessible processes
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        return pids;
    }

    private static HashSet<int> GetDescendantProcessIds(HashSet<int> roots, int maxDepth)
    {
        var descendants = new HashSet<int>();
        if (roots.Count == 0 || maxDepth <= 0)
            return descendants;

        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == NativeMethods.INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero)
            return descendants;

        try
        {
            var childrenByParent = new Dictionary<int, List<int>>();
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
            };

            if (!NativeMethods.Process32First(snapshot, ref entry))
                return descendants;

            do
            {
                int pid = unchecked((int)entry.th32ProcessID);
                int parentPid = unchecked((int)entry.th32ParentProcessID);
                if (pid <= 0 || parentPid <= 0)
                    continue;

                if (!childrenByParent.TryGetValue(parentPid, out var list))
                {
                    list = new List<int>();
                    childrenByParent[parentPid] = list;
                }
                list.Add(pid);
            } while (NativeMethods.Process32Next(snapshot, ref entry));

            var frontier = new HashSet<int>(roots);
            for (int depth = 0; depth < maxDepth; depth++)
            {
                var next = new HashSet<int>();
                foreach (var parent in frontier)
                {
                    if (!childrenByParent.TryGetValue(parent, out var kids))
                        continue;
                    foreach (var kid in kids)
                    {
                        if (roots.Contains(kid) || descendants.Contains(kid))
                            continue;
                        descendants.Add(kid);
                        next.Add(kid);
                    }
                }
                if (next.Count == 0)
                    break;
                frontier = next;
            }
        }
        finally
        {
            _ = NativeMethods.CloseHandle(snapshot);
        }

        return descendants;
    }

    private void UpdateAudioMuteState()
    {
        var pids = GetTrackedClientPids();
        int? controlledPid = null;
        if (!_config.MuteAllExceptControlled &&
            _controlledClientHwnd != IntPtr.Zero &&
            NativeMethods.IsWindow(_controlledClientHwnd))
        {
            NativeMethods.GetWindowThreadProcessId(_controlledClientHwnd, out uint pid);
            if (pid != 0)
                controlledPid = (int)pid;
        }
        var forcedMutePids = new HashSet<int>();
        var forcedUnmutePids = new HashSet<int>();
        foreach (var hwnd in _manuallyMutedClients.Where(NativeMethods.IsWindow).ToList())
        {
            foreach (var pid in GetClientProcessFamilyPids(hwnd, includeSameNameSiblings: false))
                forcedMutePids.Add(pid);
        }
        foreach (var hwnd in _manuallyUnmutedClients.Where(NativeMethods.IsWindow).ToList())
        {
            foreach (var pid in GetClientProcessFamilyPids(hwnd, includeSameNameSiblings: false))
                forcedUnmutePids.Add(pid);
        }

        _ = _audioMuteManager.ApplyMuteState(
            pids,
            controlledPid,
            _config.MuteAllExceptControlled,
            forcedMutePids,
            forcedUnmutePids);
    }

    private HashSet<int> GetClientProcessFamilyPids(IntPtr hwnd, bool includeSameNameSiblings = false)
    {
        var pids = new HashSet<int>();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return pids;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return pids;

        int rootPid = (int)pid;
        pids.Add(rootPid);
        foreach (var childPid in GetDescendantProcessIds(new HashSet<int> { rootPid }, maxDepth: 6))
            pids.Add(childPid);

        if (includeSameNameSiblings)
        {
            try
            {
                using var rootProc = Process.GetProcessById(rootPid);
                string name = rootProc.ProcessName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try { pids.Add(proc.Id); }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return pids;
    }

    private void ApplyTileStretchModeToAllTiles()
    {
        var stretch = GetTileStretchMode();
        foreach (var tile in _scaledClients.Values)
        {
            if (tile.Child is not Grid grid)
                continue;
            var img = grid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
            if (img != null)
                img.Stretch = stretch;
        }
    }

    private Stretch GetTileStretchMode()
    {
        return string.Equals(_config.TileAspectMode, AppConfig.TileAspectStretchFill, StringComparison.OrdinalIgnoreCase)
            ? Stretch.Fill
            : Stretch.Uniform;
    }

    private void ApplyTaskbarVisibilityToAllTrackedWindows()
    {
        var handles = _scaledClients.Keys.Concat(_embeddedClients.Keys).Distinct().ToList();
        foreach (var hwnd in handles)
            ApplyTaskbarVisibilityForWindow(hwnd);
    }

    private void RestoreTaskbarStyleForAllTrackedWindows()
    {
        var handles = _scaledClients.Keys.Concat(_embeddedClients.Keys).Concat(_originalTaskbarExStyles.Keys).Distinct().ToList();
        foreach (var hwnd in handles)
            RestoreTaskbarStyle(hwnd);
    }

    private void ApplyTaskbarVisibilityForWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        if (!_config.HideManagedClientsFromTaskbar)
        {
            RestoreTaskbarStyle(hwnd);
            return;
        }

        if (!_originalTaskbarExStyles.ContainsKey(hwnd))
            _originalTaskbarExStyles[hwnd] = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        long ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void RestoreTaskbarStyle(IntPtr hwnd)
    {
        if (!_originalTaskbarExStyles.TryGetValue(hwnd, out var originalEx))
            return;
        if (NativeMethods.IsWindow(hwnd))
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)originalEx);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        _originalTaskbarExStyles.Remove(hwnd);
    }

    private NativeMethods.POINT? TryMapScaledClickToScreenPoint(IntPtr hwnd, FrameworkElement imageElement, System.Windows.Point clickPos)
    {
        if (!_scaledSourceSizes.TryGetValue(hwnd, out var srcSize))
            return null;

        double sourceW = Math.Max(1, srcSize.Width);
        double sourceH = Math.Max(1, srcSize.Height);
        double targetW = Math.Max(1, imageElement.ActualWidth);
        double targetH = Math.Max(1, imageElement.ActualHeight);

        int sourceX;
        int sourceY;
        if (GetTileStretchMode() == Stretch.Fill)
        {
            // Fill mode: no letterboxing, independent X/Y scaling.
            sourceX = (int)Math.Round((clickPos.X / targetW) * sourceW);
            sourceY = (int)Math.Round((clickPos.Y / targetH) * sourceH);
        }
        else
        {
            // Uniform mode: account for letterboxing.
            double scale = Math.Min(targetW / sourceW, targetH / sourceH);
            double drawnW = sourceW * scale;
            double drawnH = sourceH * scale;
            double offsetX = (targetW - drawnW) / 2.0;
            double offsetY = (targetH - drawnH) / 2.0;

            if (clickPos.X < offsetX || clickPos.Y < offsetY ||
                clickPos.X > offsetX + drawnW || clickPos.Y > offsetY + drawnH)
                return null;

            sourceX = (int)Math.Round((clickPos.X - offsetX) / scale);
            sourceY = (int)Math.Round((clickPos.Y - offsetY) / scale);
        }

        sourceX = Math.Clamp(sourceX, 0, srcSize.Width - 1);
        sourceY = Math.Clamp(sourceY, 0, srcSize.Height - 1);

        IntPtr captureHwnd = GetCaptureHwndForClient(hwnd);
        if (!NativeMethods.GetWindowRect(captureHwnd, out var rect))
            return null;

        int screenX = rect.Left + (int)Math.Round(sourceX * (rect.Width / (double)Math.Max(1, srcSize.Width)));
        int screenY = rect.Top + (int)Math.Round(sourceY * (rect.Height / (double)Math.Max(1, srcSize.Height)));
        return new NativeMethods.POINT { X = screenX, Y = screenY };
    }

    private void PrimeUsageSampling()
    {
        try
        {
            _cpuTotalCounter?.Dispose();
            _cpuTotalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _ = _cpuTotalCounter.NextValue(); // prime

            RefreshGpuCounters();
        }
        catch
        {
            _cpuTotalCounter = null;
        }
    }

    private void UpdateUsageLabel()
    {
        try
        {
            double cpuPercent = _cpuTotalCounter?.NextValue() ?? 0.0;
            double? gpuPercent = SampleGpuUsagePercent();
            string gpuText = gpuPercent.HasValue ? $"{gpuPercent.Value:0}%" : "--";
            var mem = GetSystemMemoryUsage();
            UsageLabel.Text = $"CPU {cpuPercent:0}%  GPU {gpuText}  MEM {mem.UsedPercent:0}%";
        }
        catch
        {
            UsageLabel.Text = "CPU --  GPU --  MEM --";
        }
    }

    private double? SampleGpuUsagePercent()
    {
        try
        {
            RefreshGpuCounters();
            if (_gpuCounters.Count == 0)
                return null;

            float total = 0f;
            foreach (var counter in _gpuCounters.Values)
                total += counter.NextValue();
            return Math.Clamp(total, 0f, 100f);
        }
        catch
        {
            return null;
        }
    }

    private void RefreshGpuCounters()
    {
        var category = new PerformanceCounterCategory("GPU Engine");
        var wanted = new HashSet<string>(
            category.GetInstanceNames().Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)));

        foreach (var stale in _gpuCounters.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _gpuCounters[stale].Dispose();
            _gpuCounters.Remove(stale);
        }

        foreach (var instance in wanted)
        {
            if (_gpuCounters.ContainsKey(instance))
                continue;
            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
            _ = counter.NextValue(); // prime
            _gpuCounters[instance] = counter;
        }
    }

    private void DisposeGpuCounters()
    {
        foreach (var c in _gpuCounters.Values)
            c.Dispose();
        _gpuCounters.Clear();
        _cpuTotalCounter?.Dispose();
        _cpuTotalCounter = null;
    }

    private static (double UsedGb, double TotalGb, double UsedPercent) GetSystemMemoryUsage()
    {
        MEMORYSTATUSEX mem = new() { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem) || mem.ullTotalPhys == 0)
            return (0, 0, 0);

        double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
        double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
        double usedGb = Math.Max(0, totalGb - availGb);
        double usedPercent = Math.Clamp((usedGb / Math.Max(0.001, totalGb)) * 100.0, 0, 100);
        return (usedGb, totalGb, usedPercent);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private List<IntPtr> GetCurrentPageHandles()
    {
        var allHandles = _forceScaleMode
            ? _scaledClients.Keys.ToList()
            : _embeddedClients.Keys.ToList();
        int total = allHandles.Count;
        GetLayoutMetrics(total, out _, out var perPage, out _);

        return allHandles
            .Skip(_currentPage * perPage)
            .Take(perPage)
            .ToList();
    }

    private void GetLayoutMetrics(int total, out int cols, out int perPage, out int totalPages)
    {
        if (_forceScaleMode)
        {
            int maxTilesPerPage = Math.Max(4, _config.MaxTilesPerPage);
            int visibleCount = Math.Max(1, Math.Min(total, maxTilesPerPage));
            cols = _config.GridColumnsOverride > 0
                ? _config.GridColumnsOverride
                : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(visibleCount)));
            perPage = maxTilesPerPage;
            totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
            return;
        }

        // Interactive mode: enforce minimum tile size, paginate instead of shrinking below usable size.
        double w = TileGrid.ActualWidth;
        double h = TileGrid.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            w = Math.Max(800, ActualWidth - 20);
            h = Math.Max(500, ActualHeight - 70);
        }

        int fitCols = Math.Max(1, (int)Math.Floor(w / MinInteractiveTileWidth));
        int fitRows = Math.Max(1, (int)Math.Floor(h / MinInteractiveTileHeight));
        perPage = Math.Max(1, fitCols * fitRows);
        cols = fitCols;
        totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
    }

    private void UpdateTileClickControlButtonText()
    {
        TileClickControlBtn.Content = _config.EnableTileClickControl
            ? "Tile Click Control: On"
            : "Tile Click Control: Off";
    }

    private void UpdateMuteAllButtonText()
    {
        MuteAllBtn.Content = _config.MuteAllExceptControlled
            ? "Mute All: On"
            : "Mute All: Off";
    }

    private void TriggerTileClickControlAttention()
    {
        if (!_config.EnableTileClickAttentionFlash)
            return;

        if (_tileClickAttentionTimer == null)
        {
            _tileClickAttentionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _tileClickAttentionTimer.Tick += (_, _) =>
            {
                _tileClickAttentionTicks++;
                bool onPulse = _tileClickAttentionTicks % 2 == 1;
                TileClickControlBtn.Background = onPulse
                    ? (System.Windows.Media.Brush)(FindResource("Accent") ?? System.Windows.Media.Brushes.OrangeRed)
                    : (_tileClickControlDefaultBackground ?? System.Windows.Media.Brushes.SteelBlue);

                if (_tileClickAttentionTicks >= 8)
                {
                    _tileClickAttentionTimer.Stop();
                    _tileClickAttentionTicks = 0;
                    _tileClickAttentionActive = false;
                    TileClickControlBtn.Background = _tileClickControlDefaultBackground ?? System.Windows.Media.Brushes.SteelBlue;
                }
            };
        }

        // Restart pulse if user keeps clicking tiles while disabled.
        if (_tileClickAttentionActive)
        {
            _tileClickAttentionTimer.Stop();
            _tileClickAttentionTicks = 0;
        }

        _tileClickAttentionActive = true;
        _tileClickAttentionTimer.Start();
    }
}
