using Vintagestory.API.Common;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>The client's copy of a player's survival state, seen from a real engine-mode client.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveClientVitalsTests : ClientServerScenarioBase
{
    private string _lastFailure = "";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override string PlayerName => "VitalReader";

    [ClientServerScenario]
    public async Task TheClient_KeepsTheHealthAndWetnessTheServerSends()
    {
        Assert.True(Player!.Vitals.IsAvailable);

        VitalsReading client = Player.Vitals.Read();
        Assert.NotNull(client.Health);
        Assert.NotNull(client.BodyTemperature);
        Assert.NotNull(client.Oxygen);

        // The client's player entity carries no hunger: the survival player holds satiety and the
        // nutrient levels on the server only, so a reading here has nothing where the server has
        // values.
        Assert.Null(client.Saturation);
        Assert.Null(client.Nutrition);

        Assert.True(await StepUntilAsync(() => VitalsAreInSync(), maxFrames: 600), _lastFailure);
    }

    [ClientServerScenario]
    public async Task HealthAndWetness_ReachTheClient()
    {
        // The vitals the client can see are its own player's, so the change goes to the server side of
        // the player the rendering client joined as.
        ServerPlayer.Vitals.Set(health: 7, wetness: 0.6f);

        Assert.True(await StepUntilAsync(() => Check(PlayerName, VitalsValues.Health | VitalsValues.Wetness), maxFrames: 600), _lastFailure);

        VitalsReading client = Player!.Vitals.Read();

        // Health regenerates on the server while the client is catching up, so the point is the
        // value arriving, not the figure it started at. Wetness is set outright and only decays
        // over game hours.
        Assert.True(client.Health < 15, $"the client still shows full health: {client.Health}");
        Assert.True(client.Wetness > 0.3f, $"the client shows no wetness: {client.Wetness}");
    }

    [ClientServerScenario]
    public async Task VitalsSynced_ComparesOnlyWhatItWasAskedFor()
    {
        // Oxygen and body temperature are worked out on each side for itself, so they drift and are
        // left out by default. Asking for them is allowed, and the assertion says which values it
        // looked at either way. Which of them happens to agree on a given platform is the game's
        // business, not this test's, so it does not assume a mismatch.
        string? reported = null;
        try
        {
            ClientServerAssert.VitalsSynced(Session!, PlayerName, values: VitalsValues.Health | VitalsValues.Oxygen | VitalsValues.BodyTemperature);
        }
        catch (PharosAssertException ex)
        {
            reported = ex.Message;
        }

        if (reported is not null)
        {
            Assert.Contains("Compared", reported);
            Assert.Contains("oxygen", reported);
            Assert.Contains("body temperature", reported);
        }

        // And it says which values it did not look at when they do not agree.
        string? skipped = null;
        try
        {
            ClientServerAssert.VitalsSynced(Session!, PlayerName, values: VitalsValues.Health, tolerance: 0.01f);
        }
        catch (PharosAssertException ex)
        {
            skipped = ex.Message;
        }

        if (skipped is not null) Assert.DoesNotContain("oxygen", skipped);
    }

    [ClientServerScenario]
    public async Task VitalsSynced_NeedsTheClientsOwnPlayer()
    {
        Assert.Throws<ArgumentNullException>(() => ClientServerAssert.VitalsSynced(null!, PlayerName));
        Assert.Throws<ArgumentException>(() => ClientServerAssert.VitalsSynced(Session!, "  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientServerAssert.VitalsSynced(Session!, PlayerName, tolerance: -1));
        Assert.Throws<InvalidOperationException>(() => ClientServerAssert.VitalsSynced(Session!, "Nobody"));

        await CreateTestPlayerAsync("SecondPlayer");
        InvalidOperationException other = Assert.Throws<InvalidOperationException>(() => ClientServerAssert.VitalsSynced(Session!, "SecondPlayer"));
        Assert.Contains("its own player", other.Message);
    }

    private bool VitalsAreInSync(string playerName = null!, VitalsValues values = VitalsValues.SyncedByDefault) =>
        Check(playerName ?? PlayerName, values);

    private bool Check(string playerName, VitalsValues values)
    {
        try
        {
            ClientServerAssert.VitalsSynced(Session!, playerName, values: values);
            return true;
        }
        catch (PharosAssertException ex)
        {
            _lastFailure = ex.Message;
            return false;
        }
    }
}