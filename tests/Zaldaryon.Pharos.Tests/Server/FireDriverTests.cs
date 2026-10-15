using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Fire on the server: the game's fire block, its burning behaviour, the spread between fuel blocks, and rain.</summary>
[Seeded(1234)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class FireDriverTests : ServerScenarioBase
{
    private const string Fuel = "game:log-placed-oak-ud";

    // The game's burning behaviour draws a spread chance on every 25 ms tick, and the chance is zero for the first 2.5 s
    // (BEBehaviorBurning.OnTick). The first spread therefore comes after 51 ticks of 50 ms, and it is near certain within 600.
    private const int TickMs = 50;
    private const int NoSpreadTicks = 2500 / TickMs;
    private const int SpreadBoundTicks = 600;

    [ServerScenario]
    public async Task Ignite_PlacesAFireThatBurnsTheFuelBelow()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos fuel = GroundNextTo(player, -6);
        this.SetBlock(fuel, Fuel);

        BlockPos fire = fuel.UpCopy();
        Host!.World.Fire.Ignite(fire);

        FireState state = Host.World.Fire.State(fire);
        Assert.Equal("game:fire", state.Block);
        Assert.True(state.Burning, "A fire lit on fuel should burn.");
        Assert.Equal(Fuel, BlockCode(fuel));
    }

    [ServerScenario]
    public async Task Spread_CarriesTheFireToFuelNextDoor()
    {
        using VirtualClock clock = Host!.UseVirtualClock(50);
        using IDisposable dry = HoldPrecipitation(-1);
        BlockPos fuel = GroundNextTo(await CreateTestPlayerAsync(PlayerNames.Next()), -6);
        BlockPos neighbour = fuel.AddCopy(1, 0, 0);
        this.SetBlock(fuel, Fuel);
        this.SetBlock(neighbour, Fuel);

        await TicksUntilSpread(fuel.UpCopy(), neighbour.UpCopy());

        FireState next = Host.World.Fire.State(neighbour.UpCopy());
        Assert.Equal("game:fire", next.Block);
        Assert.True(next.Burning, "The fire that spread should burn.");
    }

    [ServerTheory]
    [InlineData(1)]
    [InlineData(1234)]
    [InlineData(987654)]
    public async Task Spread_StartsAfterTheGameDelayAndWithinTheBound(int seed)
    {
        using VirtualClock clock = Host!.UseVirtualClock(TickMs);
        using IDisposable dry = HoldPrecipitation(-1);
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos fuel = GroundNextTo(player, -6);
        BlockPos neighbour = fuel.AddCopy(1, 0, 0);
        this.SetBlock(fuel, Fuel);
        this.SetBlock(neighbour, Fuel);
        Host.Random.Seed(seed);

        int ticks = await TicksUntilSpread(fuel.UpCopy(), neighbour.UpCopy(), SpreadBoundTicks);

        Assert.InRange(ticks, NoSpreadTicks + 1, SpreadBoundTicks);
    }

    [ServerScenario]
    public async Task Rain_PutsOutTheFireAndKeepsTheFuel()
    {
        using VirtualClock clock = Host!.UseVirtualClock(50);
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos fuel = GroundNextTo(player, -6);
        this.SetBlock(fuel, Fuel);
        BlockPos fire = fuel.UpCopy();

        // Dry while the fire starts, so only the rain can put it out.
        using IDisposable weather = HoldPrecipitation(-1);
        Host.World.Fire.Ignite(fire);
        await Host.World.Fire.SpreadAsync(40);
        Assert.True(Host.World.Fire.State(fire).Burning, "The fire should burn before the rain falls.");

        Host.Weather.SetPrecipitation(1);
        for (int step = 0; step < 60 && Host.World.Fire.State(fire).Block == "game:fire"; step++)
        {
            await Host.World.Fire.SpreadAsync(20);
        }

        Assert.Equal("game:air", BlockCode(fire));
        Assert.Equal(Fuel, BlockCode(fuel));
    }

    [ServerScenario]
    public async Task Ignite_RefusesABlockThatIsNotReplaceable()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos wall = GroundNextTo(player, -6).UpCopy();
        this.SetBlock(wall, "game:rock-granite");

        Assert.Throws<InvalidOperationException>(() => Host!.World.Fire.Ignite(wall));
        Assert.Equal("game:rock-granite", BlockCode(wall));
    }

    [ServerScenario]
    public async Task Fire_BurnsOutWhenNothingNearbyBurns()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos fire = GroundNextTo(player, -6).UpCopy();

        // No fuel below and none beside: the game gives the fire one second, then removes it.
        Host!.World.Fire.Ignite(fire);
        for (int step = 0; step < 50 && Host.World.Fire.State(fire).Block == "game:fire"; step++)
        {
            await Host.World.Fire.SpreadAsync(10);
        }

        Assert.Equal("game:air", BlockCode(fire));
    }

    [ServerScenario]
    public async Task SpreadAsync_RejectsANegativeCount() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Host!.World.Fire.SpreadAsync(-1));

    [ServerScenario]
    public void SpreadAsync_StopsBeforeTheTicksWhenCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => { _ = Host!.World.Fire.SpreadAsync(10, cancelled.Token); });
    }

    // Lights the fire and runs the server one tick at a time until the fire reaches the spread position. Returns the ticks it took.
    private async Task<int> TicksUntilSpread(BlockPos fire, BlockPos spreadTo, int maxTicks = 20000)
    {
        Host!.World.Fire.Ignite(fire);
        int ticks = 0;
        while (ticks < maxTicks && Host.World.Fire.State(spreadTo).Block != "game:fire")
        {
            await Host.World.Fire.SpreadAsync(1);
            ticks++;
        }

        Assert.True(Host.World.Fire.State(spreadTo).Block == "game:fire", $"The fire did not spread within {maxTicks} ticks.");
        return ticks;
    }

    // Sets precipitation for the test, and hands it back to the calendar when disposed.
    private IDisposable HoldPrecipitation(float level)
    {
        Host!.Weather.SetPrecipitation(level);
        return new Released(() => Host.Weather.SetPrecipitation(null));
    }

    private sealed class Released(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    // The block at the player's feet, offset along x: it sits on the ground the player stands on.
    private static BlockPos GroundNextTo(ServerTestPlayer player, int dx) =>
        player.Entity!.Pos.AsBlockPos.AddCopy(dx, 0, 0);

    private string BlockCode(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());
}
