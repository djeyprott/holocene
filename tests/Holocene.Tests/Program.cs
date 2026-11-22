using System.Reflection;

namespace Holocene.Tests;

/// <summary>Minimal test runner: every public static void method named Test* in this assembly is a test.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0)
            .Where(m => filter == null || (m.DeclaringType!.Name + "." + m.Name).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name).ToList();
        int failed = 0;
        foreach (var m in tests)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { m.Invoke(null, null); Console.WriteLine($"  ok   {m.DeclaringType!.Name}.{m.Name} ({sw.ElapsedMilliseconds} ms)"); }
            catch (TargetInvocationException e)
            {
                failed++;
                Console.WriteLine($"  FAIL {m.DeclaringType!.Name}.{m.Name}: {e.InnerException?.Message}");
            }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }
}

public static class Assert
{
    public static void True(bool cond, string msg) { if (!cond) throw new Exception(msg); }
    public static void Near(double actual, double expected, double tol, string what)
    {
        if (!(Math.Abs(actual - expected) <= tol)) throw new Exception($"{what}: expected {expected} ± {tol}, got {actual}");
    }
}
