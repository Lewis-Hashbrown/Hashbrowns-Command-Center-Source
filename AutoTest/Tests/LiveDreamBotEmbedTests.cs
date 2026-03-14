using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ClientDashboard;

namespace AutoTest.Tests;

public static class LiveDreamBotEmbedTests
{
    private const int SizeTolerance = 40;

    public static void Run(TestRunner runner, int expectedClients, int timeoutSeconds)
    {
        runner.Run($"Live DreamBot: {expectedClients}+ embedded clients fill tiles", () =>
            ValidateLiveSnapshot(expectedClients, timeoutSeconds));
    }

    private static void ValidateLiveSnapshot(int expectedClients, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string lastReason = "No snapshot read yet.";

        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(LiveStatusWriter.StatusFilePath))
            {
                lastReason = $"Snapshot file not found at {LiveStatusWriter.StatusFilePath}";
                Thread.Sleep(500);
                continue;
            }

            LiveStatusSnapshot? snapshot;
            try
            {
                var json = File.ReadAllText(LiveStatusWriter.StatusFilePath);
                snapshot = JsonSerializer.Deserialize<LiveStatusSnapshot>(json);
            }
            catch (Exception ex)
            {
                lastReason = $"Failed to read snapshot: {ex.Message}";
                Thread.Sleep(500);
                continue;
            }

            if (snapshot == null)
            {
                lastReason = "Snapshot is null";
                Thread.Sleep(500);
                continue;
            }

            var age = DateTime.UtcNow - snapshot.TimestampUtc;
            if (age > TimeSpan.FromSeconds(20))
            {
                lastReason = $"Snapshot is stale ({age.TotalSeconds:F1}s old)";
                Thread.Sleep(500);
                continue;
            }

            if (snapshot.TotalClients < expectedClients)
            {
                lastReason = $"Only {snapshot.TotalClients} embedded client(s), expected at least {expectedClients}";
                Thread.Sleep(500);
                continue;
            }

            var dreamBotClients = snapshot.Clients
                .Where(c => c.Title.Contains("DreamBot", StringComparison.OrdinalIgnoreCase) &&
                            !c.Title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var clientsToValidate = dreamBotClients.Count >= expectedClients
                ? dreamBotClients
                : snapshot.Clients;

            if (clientsToValidate.Count < expectedClients)
            {
                lastReason =
                    $"Only {clientsToValidate.Count} client snapshot entries, expected at least {expectedClients}";
                Thread.Sleep(500);
                continue;
            }

            var bad = clientsToValidate
                .Select(c => new
                {
                    Client = c,
                    WidthDiff = Math.Abs(c.PanelWidth - c.ClientWidth),
                    HeightDiff = Math.Abs(c.PanelHeight - c.ClientHeight)
                })
                .FirstOrDefault(x =>
                    x.Client.PanelWidth <= 0 || x.Client.PanelHeight <= 0 ||
                    x.Client.ClientWidth <= 0 || x.Client.ClientHeight <= 0 ||
                    x.WidthDiff > SizeTolerance || x.HeightDiff > SizeTolerance);

            if (bad != null)
            {
                lastReason =
                    $"Size mismatch hwnd=0x{bad.Client.Hwnd:X} panel={bad.Client.PanelWidth}x{bad.Client.PanelHeight} " +
                    $"client={bad.Client.ClientWidth}x{bad.Client.ClientHeight} " +
                    $"diff=({bad.WidthDiff},{bad.HeightDiff})";
                Thread.Sleep(500);
                continue;
            }

            return;
        }

        throw new Exception($"Timed out after {timeoutSeconds}s. Last status: {lastReason}");
    }
}
