using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ClientDashboard;
using AutoTest.Tests;

namespace AutoTest;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // If "diag" argument, run multi-window diagnostic
        if (args.Length > 0 && args[0] == "diag")
        {
            int count = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 3;
            DiagnosticTest.RunMultiWindowTest(count);
            return 0;
        }

        // If "scan" argument, scan all windows
        if (args.Length > 0 && args[0] == "scan")
        {
            WindowScanner.ScanAll();
            return 0;
        }

        // If "scanall" argument, scan ALL windows including Java processes
        if (args.Length > 0 && args[0] == "scanall")
        {
            AllWindowScanner.ScanJavaWindows();
            return 0;
        }

        // If "deep" argument, deep scan including hidden windows
        if (args.Length > 0 && args[0] == "deep")
        {
            DeepScanner.ScanDeep();
            return 0;
        }

        // If "inspect" argument, inspect dashboard child windows
        if (args.Length > 0 && args[0] == "inspect")
        {
            DashboardInspector.Inspect();
            return 0;
        }

        // If "stress" argument, run strict multi-window stress tests only
        if (args.Length > 0 && args[0] == "stress")
        {
            var stressRunner = new TestRunner();
            MultiClientLayoutStressTests.Run(stressRunner);
            stressRunner.PrintSummary();
            return stressRunner.AllPassed ? 0 : 1;
        }

        // If "live" argument, validate real DreamBot clients embedded in dashboard
        if (args.Length > 0 && args[0] == "live")
        {
            int expectedClients = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 3;
            int timeoutSeconds = args.Length > 2 && int.TryParse(args[2], out int t) ? t : 180;
            var liveRunner = new TestRunner();
            LiveDreamBotEmbedTests.Run(liveRunner, expectedClients, timeoutSeconds);
            liveRunner.PrintSummary();
            return liveRunner.AllPassed ? 0 : 1;
        }

        // If "autolive" argument, one-click mode:
        // starts dashboard with --autolaunch, adapts to detected client count, validates sizes.
        if (args.Length > 0 && args[0] == "autolive")
        {
            int timeoutSeconds = args.Length > 1 && int.TryParse(args[1], out int t) ? t : 300;
            int stableSeconds = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 8;
            int minClients = args.Length > 3 && int.TryParse(args[3], out int m) ? m : 1;

            string? repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                Console.Error.WriteLine("Could not locate repo root.");
                return 1;
            }

            string dashboardExe = Path.Combine(repoRoot, "ClientDashboard", "bin", "Debug", "net8.0-windows", "ClientDashboard.exe");
            if (!File.Exists(dashboardExe))
            {
                Console.Error.WriteLine($"Dashboard exe not found: {dashboardExe}");
                Console.Error.WriteLine("Build first: dotnet build \"ClientDashboard/ClientDashboard.csproj\"");
                return 1;
            }

            if (File.Exists(LiveStatusWriter.StatusFilePath))
            {
                try { File.Delete(LiveStatusWriter.StatusFilePath); } catch { }
            }

            Process? dashboardProcess = null;
            try
            {
                Console.WriteLine("Starting dashboard with --autolaunch...");
                dashboardProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = dashboardExe,
                    Arguments = "--autolaunch",
                    UseShellExecute = true
                });

                Thread.Sleep(5000);
                if (dashboardProcess == null || dashboardProcess.HasExited)
                {
                    Console.Error.WriteLine($"Dashboard exited early (code {(dashboardProcess?.ExitCode.ToString() ?? "unknown")}).");
                    return 1;
                }

                var adaptiveRunner = new TestRunner();
                AdaptiveLiveDreamBotEmbedTests.Run(adaptiveRunner, timeoutSeconds, stableSeconds, minClients);
                adaptiveRunner.PrintSummary();
                return adaptiveRunner.AllPassed ? 0 : 1;
            }
            finally
            {
                try
                {
                    if (dashboardProcess is { HasExited: false })
                        dashboardProcess.Kill(true);
                }
                catch { }
            }
        }

        Console.WriteLine("+-----------------------------------------+");
        Console.WriteLine("|  OSRS Client Dashboard - AutoTest Suite  |");
        Console.WriteLine("+-----------------------------------------+");
        Console.WriteLine();

        var runner = new TestRunner();

        // Phase 1: Spawn Notepad as a test target
        Console.WriteLine("> Phase 1: Window Detection (Notepad)");
        Console.WriteLine(new string('-', 50));

        var notepad = Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            UseShellExecute = true
        })!;
        Thread.Sleep(2000); // Wait for Notepad to fully load

        // Refresh handle (Win11 Notepad may spawn a new process)
        notepad.Refresh();
        IntPtr notepadHwnd = notepad.MainWindowHandle;

        // If handle is zero, find by enumerating windows
        if (notepadHwnd == IntPtr.Zero)
        {
            Thread.Sleep(1000);
            notepad.Refresh();
            notepadHwnd = notepad.MainWindowHandle;
        }

        if (notepadHwnd == IntPtr.Zero)
        {
            // Win11 Notepad creates a different process - find by title
            Win32.EnumWindows((hWnd, _) =>
            {
                if (!Win32.IsWindowVisible(hWnd)) return true;
                var title = Win32.GetWindowTitle(hWnd);
                if (title.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Untitled", StringComparison.OrdinalIgnoreCase))
                {
                    notepadHwnd = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (notepadHwnd == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: Could not find Notepad window handle. Aborting.");
            Console.ResetColor();
            try { notepad.Kill(); } catch { }
            return 1;
        }

        Console.WriteLine($"  Found Notepad: hwnd=0x{notepadHwnd:X}, title=\"{Win32.GetWindowTitle(notepadHwnd)}\"");
        Console.WriteLine();

        WindowDetectionTests.Run(runner, notepadHwnd);

        Console.WriteLine();
        Console.WriteLine("> Phase 2: Style Manipulation (Notepad)");
        Console.WriteLine(new string('-', 50));
        StyleManipulationTests.Run(runner, notepadHwnd);

        Console.WriteLine();
        Console.WriteLine("> Phase 3: Embed & Resize (Notepad)");
        Console.WriteLine(new string('-', 50));
        EmbedAndResizeTests.Run(runner, notepadHwnd);

        Console.WriteLine();
        Console.WriteLine("> Phase 3b: Multi-Client Stress (Notepad)");
        Console.WriteLine(new string('-', 50));
        MultiClientLayoutStressTests.Run(runner);

        // Clean up Notepad
        try { notepad.Kill(); } catch { }
        // Also try to kill by hwnd process in case Win11 spawned a different process
        try
        {
            Win32.GetWindowThreadProcessId(notepadHwnd, out uint pid);
            if (pid != 0)
            {
                var proc = Process.GetProcessById((int)pid);
                proc.Kill();
            }
        }
        catch { }

        Console.WriteLine();
        Console.WriteLine("> Phase 4: Launcher Automation");
        Console.WriteLine(new string('-', 50));
        LauncherAutomationFlowTests.Run(runner);

        Console.WriteLine();
        Console.WriteLine("> Phase 4: DreamBot Integration");
        Console.WriteLine(new string('-', 50));
        DreamBotIntegrationTests.Run(runner);

        // Summary
        runner.PrintSummary();

        return runner.AllPassed ? 0 : 1;
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "ClientDashboard", "ClientDashboard.csproj");
            if (File.Exists(candidate)) return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}
