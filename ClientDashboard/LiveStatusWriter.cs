using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClientDashboard;

public static class LiveStatusWriter
{
    public static readonly string StatusFilePath =
        Path.Combine(Path.GetTempPath(), "clientdashboard-live-status.json");

    public static void WriteSnapshot(LiveStatusSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StatusFilePath, json);
    }
}

public sealed class LiveStatusSnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int TotalClients { get; set; }
    public List<LiveClientStatus> Clients { get; set; } = new();
}

public sealed class LiveClientStatus
{
    public long Hwnd { get; set; }
    public string Title { get; set; } = "";
    public int PanelWidth { get; set; }
    public int PanelHeight { get; set; }
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
}
