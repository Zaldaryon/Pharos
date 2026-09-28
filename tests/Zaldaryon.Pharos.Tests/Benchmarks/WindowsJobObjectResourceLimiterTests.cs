using System.Diagnostics;
using Zaldaryon.Pharos.Benchmarks;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

public sealed class WindowsJobObjectResourceLimiterTests
{
    [Fact]
    public void OptionsRequireAtLeastOneValidLimit()
    {
        Assert.Throws<ArgumentException>(() => new ProcessResourceLimitOptions().Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessResourceLimitOptions(CpuLimitPercent: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessResourceLimitOptions(CpuLimitPercent: 100.1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessResourceLimitOptions(JobMemoryLimitBytes: 0).Validate());

        new ProcessResourceLimitOptions(CpuLimitPercent: 25, JobMemoryLimitBytes: 1024 * 1024).Validate();
    }

    [Fact]
    public void JobObjectAppliesLimitsAndKillsOnlyWhenExplicitlyRequested()
    {
        if (!OperatingSystem.IsWindows()) return;

        using Process child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the resource-limiter test child process.");

        using (WindowsJobObjectResourceLimiter.Apply(child,
                   new ProcessResourceLimitOptions(CpuLimitPercent: 50, JobMemoryLimitBytes: 512L * 1024 * 1024,
                       KillProcessesOnClose: true)))
        {
            Assert.False(child.HasExited);
        }

        Assert.True(child.WaitForExit(5000), "Closing a kill-on-close Job Object did not stop its child.");
    }
}
