using System;
using System.IO;
using System.Text.Json;

namespace ClientDashboard;

public class AppConfig
{
    public const string TileAspectFitWhole = "FitWhole";
    public const string TileAspectStretchFill = "StretchFill";

    public string DreamBotPath { get; set; } = "";
    public bool AutoAcceptClientControl { get; set; } = false;
    public bool EnableTileClickControl { get; set; } = true;
    public bool EnableTileClickAttentionFlash { get; set; } = true;
    public int PreviewMaxFps { get; set; } = 8; // 1..30
    public int MaxTilesPerPage { get; set; } = 100; // 4..400
    public int GridColumnsOverride { get; set; } = 0; // 0 = auto, 1..20 fixed
    public int ScanIntervalMs { get; set; } = 3000; // 500..10000
    public int CaptureIntervalMs { get; set; } = 0; // 0 = derive from PreviewMaxFps
    public bool MuteAllExceptControlled { get; set; } = false;
    public string TileAspectMode { get; set; } = TileAspectStretchFill;
    public bool HideManagedClientsFromTaskbar { get; set; } = true;

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public static AppConfig CreateDefaults()
    {
        return new AppConfig();
    }

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return CreateDefaults();
        var json = File.ReadAllText(ConfigPath);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? CreateDefaults();
        cfg.Normalize();
        return cfg;
    }

    public void Save()
    {
        Normalize();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public void Normalize()
    {
        DreamBotPath ??= "";
        PreviewMaxFps = Math.Clamp(PreviewMaxFps, 1, 30);
        MaxTilesPerPage = Math.Clamp(MaxTilesPerPage, 4, 400);
        if (GridColumnsOverride != 0)
            GridColumnsOverride = Math.Clamp(GridColumnsOverride, 1, 20);
        ScanIntervalMs = Math.Clamp(ScanIntervalMs, 500, 10000);
        if (CaptureIntervalMs < 0)
            CaptureIntervalMs = 0;
        if (CaptureIntervalMs > 0)
            CaptureIntervalMs = Math.Clamp(CaptureIntervalMs, 33, 2000);
        if (!string.Equals(TileAspectMode, TileAspectFitWhole, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(TileAspectMode, TileAspectStretchFill, StringComparison.OrdinalIgnoreCase))
        {
            TileAspectMode = TileAspectStretchFill;
        }
    }

    public AppConfig Clone()
    {
        return new AppConfig
        {
            DreamBotPath = DreamBotPath,
            AutoAcceptClientControl = AutoAcceptClientControl,
            EnableTileClickControl = EnableTileClickControl,
            EnableTileClickAttentionFlash = EnableTileClickAttentionFlash,
            PreviewMaxFps = PreviewMaxFps,
            MaxTilesPerPage = MaxTilesPerPage,
            GridColumnsOverride = GridColumnsOverride,
            ScanIntervalMs = ScanIntervalMs,
            CaptureIntervalMs = CaptureIntervalMs,
            MuteAllExceptControlled = MuteAllExceptControlled,
            TileAspectMode = TileAspectMode,
            HideManagedClientsFromTaskbar = HideManagedClientsFromTaskbar
        };
    }
}
