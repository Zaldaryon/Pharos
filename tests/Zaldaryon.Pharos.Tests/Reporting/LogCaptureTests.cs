using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Reporting;

public class LogCaptureTests
{
    private sealed class TestLogger : LoggerBase
    {
        protected override void LogImpl(EnumLogType logType, string format, params object[] args)
        {
        }
    }

    private static (LogCapture Capture, TestLogger Logger) Capture()
    {
        TestLogger logger = new();
        LogCapture capture = new(EnumAppSide.Client);
        capture.Attach(logger);
        return (capture, logger);
    }

    [Fact]
    public void Entries_KeepTheFormattedMessageAndLevel_ButNotDebug()
    {
        var (capture, logger) = Capture();

        logger.Error("chunk {0} failed", 12);
        logger.Warning("slow tick");
        logger.Debug("noise");

        Assert.Equal(2, capture.Entries.Count);
        LogEntry error = Assert.Single(capture.Errors);
        Assert.Equal("chunk 12 failed", error.Message);
        Assert.Equal(EnumAppSide.Client, error.Side);
        Assert.Equal("slow tick", Assert.Single(capture.Warnings).Message);
    }

    [Fact]
    public void UnexpectedErrors_SkipsAllowedFragments()
    {
        var (capture, logger) = Capture();
        logger.Error("Known vanilla noise");
        logger.Fatal("Real failure");

        IReadOnlyList<LogEntry> unexpected = capture.UnexpectedErrors(["vanilla NOISE"]);

        Assert.Equal("Real failure", Assert.Single(unexpected).Message);
    }

    [Fact]
    public void Gate_ThrowsOnlyWhenEnabledAndSomethingWasLogged()
    {
        var (capture, logger) = Capture();
        logger.Error("boom");

        Assert.Empty(LoggedErrorGate.Collect(false, [], capture));
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(true, ["boom"], capture));

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(true, [], capture)));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void Capacity_DropsTheOldestEntries()
    {
        var (capture, logger) = Capture();

        for (int i = 0; i <= LogCapture.Capacity; i++) logger.Notification("n{0}", i);

        Assert.Equal(LogCapture.Capacity, capture.Entries.Count);
        Assert.Equal("n1", capture.Entries[0].Message);
    }
}

[Collection("Sequential")]
public class LiveLogCaptureTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override bool FailOnLoggedErrors => true;

    protected override IEnumerable<string> AllowedLoggedErrors => ["pharos expected"];

    [ClientServerScenario]
    public void ErrorsOnBothSides_AreCaptured_AndAllowedOnesPassTheGate()
    {
        ServerHost!.RunOnGameThread(() => ServerMain.Logger.Error("pharos expected server error"));
        Client!.RunOnClientThread(() => Client.Client.Logger.Error("pharos expected client error"));

        Assert.Contains(ServerHost.Logs.Errors, e => e.Message == "pharos expected server error" && e.Side == EnumAppSide.Server);
        Assert.Contains(Client.Logs.Errors, e => e.Message == "pharos expected client error" && e.Side == EnumAppSide.Client);
    }
}
