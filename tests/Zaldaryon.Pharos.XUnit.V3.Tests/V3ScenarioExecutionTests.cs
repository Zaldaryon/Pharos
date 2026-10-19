using Xunit;
using Xunit.v3;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit.V3.Tests;

public abstract class SampleV3SmokeTest : ModSmokeTest
{
}

public class V3ScenarioExecutionTests
{
    [Fact]
    public void ModSmokeTest_InheritsFromModSmokeTestBase()
    {
        Assert.True(typeof(ModSmokeTestBase).IsAssignableFrom(typeof(SampleV3SmokeTest)));
        var method = typeof(SampleV3SmokeTest).GetMethod("ModBootsJoinsAndPlays");
        Assert.NotNull(method);
        var attr = method.GetCustomAttributes(typeof(ClientServerScenarioAttribute), inherit: true);
        Assert.NotEmpty(attr);
    }

    [Fact]
    public void NativeSkip_TriggersSkipException()
    {
        var ex = Record.Exception(() => Assert.Skip("Test skip reason"));
        Assert.NotNull(ex);
        Assert.Equal("SkipException", ex.GetType().Name);
        Assert.Equal("Test skip reason", ex.Message);
    }

    [Fact]
    public void TestContext_AllowsAttachments()
    {
        Assert.NotNull(TestContext.Current);
        TestContext.Current.AddAttachment("test-note.txt", "Sample note content");
        Assert.True(TestContext.Current!.Attachments?.ContainsKey("test-note.txt") == true);
    }

    [GameVersionFact("0.0.1")]
    public void GameVersionFact_SkipsOnUnmatchedVersion()
    {
        Assert.Fail("This test should be skipped by GameVersionGate.");
    }
}
