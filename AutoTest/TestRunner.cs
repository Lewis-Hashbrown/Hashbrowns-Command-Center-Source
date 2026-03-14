using System;
using System.Collections.Generic;

namespace AutoTest;

public class TestResult
{
    public string Name { get; init; } = "";
    public bool Passed { get; init; }
    public string Message { get; init; } = "";
    public TimeSpan Duration { get; init; }
}

public class TestRunner
{
    private readonly List<TestResult> _results = new();

    public void Run(string name, Action test)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            test();
            sw.Stop();
            _results.Add(new TestResult { Name = name, Passed = true, Message = "OK", Duration = sw.Elapsed });
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  PASS ");
            Console.ResetColor();
            Console.WriteLine($"{name} ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _results.Add(new TestResult { Name = name, Passed = false, Message = ex.Message, Duration = sw.Elapsed });
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  FAIL ");
            Console.ResetColor();
            Console.WriteLine($"{name} ({sw.ElapsedMilliseconds}ms)");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"       {ex.Message}");
            Console.ResetColor();
        }
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        int passed = _results.FindAll(r => r.Passed).Count;
        int failed = _results.Count - passed;
        var color = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = color;
        Console.WriteLine($"  {passed} passed, {failed} failed, {_results.Count} total");
        Console.ResetColor();
        Console.WriteLine(new string('=', 60));

        if (failed > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed tests:");
            foreach (var r in _results.FindAll(r => !r.Passed))
                Console.WriteLine($"  - {r.Name}: {r.Message}");
            Console.ResetColor();
        }
    }

    public bool AllPassed => _results.TrueForAll(r => r.Passed);
}

public static class Assert
{
    public static void IsTrue(bool condition, string message = "Expected true")
    {
        if (!condition) throw new Exception(message);
    }

    public static void IsFalse(bool condition, string message = "Expected false")
    {
        if (condition) throw new Exception(message);
    }

    public static void AreEqual<T>(T expected, T actual, string? context = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}" + (context != null ? $" ({context})" : ""));
    }

    public static void IsNotNull(object? obj, string message = "Expected non-null")
    {
        if (obj == null) throw new Exception(message);
    }

    public static void InRange(int value, int min, int max, string context = "")
    {
        if (value < min || value > max)
            throw new Exception($"Value {value} not in range [{min}, {max}]" + (context != "" ? $" ({context})" : ""));
    }
}
