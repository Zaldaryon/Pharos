namespace Zaldaryon.Pharos.Debug;

/// <summary>Whether a debugger is attached or debug mode is requested.</summary>
public static class DebugMode
{
    /// <summary>True when a debugger is attached or PHAROS_DEBUG=1.</summary>
    public static bool IsDebugging =>
        System.Diagnostics.Debugger.IsAttached ||
        string.Equals(Environment.GetEnvironmentVariable("PHAROS_DEBUG"), "1", StringComparison.Ordinal);

    /// <summary>Whether the watchdog should be suspended.</summary>
    public static bool SuspendWatchdog => IsDebugging;
}