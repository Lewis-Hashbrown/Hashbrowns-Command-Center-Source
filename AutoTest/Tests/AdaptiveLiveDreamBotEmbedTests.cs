using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ClientDashboard;

namespace AutoTest.Tests;

public static class AdaptiveLiveDreamBotEmbedTests
{
    private const int SizeTolerance = 40;

    public static void Run(TestRunner runner, int timeoutSeconds, int stableSeconds, int minClients)
    {
        runner.Run(
            $"Adaptive Live DreamBot: detect count, stabilize {stableSeconds}s, validate all",
            () => ValidateAdaptive(timeoutSeconds, stableSeconds, minClients));
    }

    private static void ValidateAdaptive(int timeoutSeconds, int stableSeconds, int minClients)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        int lastCount = -1;
        DateTime lastCountChange = DateTime.UtcNow;
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
                lastReason = $"Failed reading snapshot: {ex.Message}";
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
                lastReason = $"Snapshot stale ({age.TotalSeconds:F1}s)";
                Thread.Sleep(500);
                continue;
            }

            int currentCount = snapshot.TotalClients;
            if (currentCount != lastCount)
            {
                lastCount = currentCount;
                lastCountChange = DateTime.UtcNow;
            }

            if (currentCount < minClients)
            {
                lastReason = $"Only {currentCount} embedded client(s), minimum is {minClients}";
                Thread.Sleep(500);
                continue;
            }

            var stableFor = DateTime.UtcNow - lastCountChange;
            if (stableFor < TimeSpan.FromSeconds(stableSeconds))
            {
                lastReason = $"Client count {currentCount} not stable yet ({stableFor.TotalSeconds:F1}s/{stableSeconds}s)";
                Thread.Sleep(500);
                continue;
            }

            var dreamBotClients = snapshot.Clients
                .Where(c => c.Title.Contains("DreamBot", StringComparison.OrdinalIgnoreCase) &&
                            !c.Title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var clientsToValidate = dreamBotClients.Count >= minClients
                ? dreamBotClients
                : snapshot.Clients;

            if (clientsToValidate.Count < minClients)
            {
                lastReason = $"Only {clientsToValidate.Count} valid client entries, minimum is {minClients}";
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
                    $"client={bad.Client.ClientWidth}x{bad.Client.ClientHeight} diff=({bad.WidthDiff},{bad.HeightDiff})";
                Thread.Sleep(500);
                continue;
            }

            return;
        }

        throw new Exception($"Timed out after {timeoutSeconds}s. Last status: {lastReason}");
    }
}
