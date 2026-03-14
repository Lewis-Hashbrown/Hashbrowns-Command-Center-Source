using System;
using System.IO;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace ClientDashboard;

public partial class SettingsWindow : Window
{
    private AppConfig _working;

    public AppConfig? SavedConfig { get; private set; }

    public SettingsWindow(AppConfig current)
    {
        InitializeComponent();
        _working = current.Clone();
        PopulateFromConfig(_working);
    }

    private void PopulateFromConfig(AppConfig cfg)
    {
        DreamBotPathTextBox.Text = cfg.DreamBotPath;
        EnableTileClickControlCheckBox.IsChecked = cfg.EnableTileClickControl;
        AutoAcceptClientControlCheckBox.IsChecked = cfg.AutoAcceptClientControl;
        MuteAllExceptControlledCheckBox.IsChecked = cfg.MuteAllExceptControlled;
        HideManagedClientsFromTaskbarCheckBox.IsChecked = cfg.HideManagedClientsFromTaskbar;
        EnableTileClickAttentionFlashCheckBox.IsChecked = cfg.EnableTileClickAttentionFlash;
        TileAspectModeComboBox.SelectedIndex = string.Equals(cfg.TileAspectMode, AppConfig.TileAspectStretchFill, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        PreviewMaxFpsTextBox.Text = cfg.PreviewMaxFps.ToString();
        MaxTilesPerPageTextBox.Text = cfg.MaxTilesPerPage.ToString();
        GridColumnsOverrideTextBox.Text = cfg.GridColumnsOverride.ToString();
        ScanIntervalMsTextBox.Text = cfg.ScanIntervalMs.ToString();
        CaptureIntervalMsTextBox.Text = cfg.CaptureIntervalMs.ToString();
        UpdateEffectiveFpsLabel(cfg);
    }

    private void UpdateEffectiveFpsLabel(AppConfig cfg)
    {
        int intervalMs = cfg.CaptureIntervalMs > 0
            ? cfg.CaptureIntervalMs
            : Math.Max(33, (int)Math.Round(1000.0 / Math.Max(1, cfg.PreviewMaxFps)));
        double fps = 1000.0 / Math.Max(1, intervalMs);
        EffectiveFpsText.Text = $"Effective preview FPS: {fps:0.0}";
    }

    private void PerformanceInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        int previewFps = 8;
        int captureMs = 0;

        if (int.TryParse(PreviewMaxFpsTextBox.Text, out var parsedPreview))
            previewFps = Math.Clamp(parsedPreview, 1, 30);
        if (int.TryParse(CaptureIntervalMsTextBox.Text, out var parsedCapture))
            captureMs = Math.Max(0, parsedCapture);

        var temp = _working.Clone();
        temp.PreviewMaxFps = previewFps;
        temp.CaptureIntervalMs = captureMs;
        temp.Normalize();
        UpdateEffectiveFpsLabel(temp);
    }

    private void BrowsePathBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select DreamBot JAR",
            Filter = "JAR files (*.jar)|*.jar",
            InitialDirectory = Path.GetDirectoryName(DreamBotPathTextBox.Text) ?? "C:\\"
        };

        if (dialog.ShowDialog() == true)
            DreamBotPathTextBox.Text = dialog.FileName;
    }

    private void ResetDefaultsBtn_Click(object sender, RoutedEventArgs e)
    {
        _working = AppConfig.CreateDefaults();
        PopulateFromConfig(_working);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildConfigFromInputs(out var cfg, out var validationError))
        {
            WpfMessageBox.Show(validationError, "Settings Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        cfg.Save();
        SavedConfig = cfg;
        DialogResult = true;
        Close();
    }

    private bool TryBuildConfigFromInputs(out AppConfig cfg, out string validationError)
    {
        cfg = _working.Clone();
        validationError = "";

        cfg.DreamBotPath = DreamBotPathTextBox.Text?.Trim() ?? "";
        cfg.EnableTileClickControl = EnableTileClickControlCheckBox.IsChecked == true;
        cfg.AutoAcceptClientControl = AutoAcceptClientControlCheckBox.IsChecked == true;
        cfg.MuteAllExceptControlled = MuteAllExceptControlledCheckBox.IsChecked == true;
        cfg.HideManagedClientsFromTaskbar = HideManagedClientsFromTaskbarCheckBox.IsChecked == true;
        cfg.EnableTileClickAttentionFlash = EnableTileClickAttentionFlashCheckBox.IsChecked == true;
        cfg.TileAspectMode = (TileAspectModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? AppConfig.TileAspectFitWhole;

        if (!int.TryParse(PreviewMaxFpsTextBox.Text, out var previewMaxFps))
        {
            validationError = "Preview max FPS must be a number (1-30).";
            return false;
        }
        cfg.PreviewMaxFps = previewMaxFps;

        if (!int.TryParse(MaxTilesPerPageTextBox.Text, out var maxTilesPerPage))
        {
            validationError = "Max tiles per page must be a number (4-400).";
            return false;
        }
        cfg.MaxTilesPerPage = maxTilesPerPage;

        if (!int.TryParse(GridColumnsOverrideTextBox.Text, out var gridColumnsOverride))
        {
            validationError = "Grid columns override must be a number (0, or 1-20).";
            return false;
        }
        cfg.GridColumnsOverride = gridColumnsOverride;

        if (!int.TryParse(ScanIntervalMsTextBox.Text, out var scanInterval))
        {
            validationError = "Scan interval must be a number (500-10000).";
            return false;
        }
        cfg.ScanIntervalMs = scanInterval;

        if (!int.TryParse(CaptureIntervalMsTextBox.Text, out var captureInterval))
        {
            validationError = "Capture interval must be a number (0, or 33-2000).";
            return false;
        }
        cfg.CaptureIntervalMs = captureInterval;

        cfg.Normalize();

        if (!string.IsNullOrWhiteSpace(cfg.DreamBotPath) && !File.Exists(cfg.DreamBotPath))
        {
            validationError = "DreamBot JAR path does not exist.";
            return false;
        }

        if (cfg.DreamBotPath.Length == 0)
        {
            var result = WpfMessageBox.Show(
                "DreamBot JAR path is empty. Launch will use fallback discovery.\n\nSave anyway?",
                "Settings Validation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                validationError = "Save canceled.";
                return false;
            }
        }

        return true;
    }
}
