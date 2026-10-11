using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A live server's tick listeners, and firepits and querns run on simulated time.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
public class LiveServerTickListenerTests : ServerScenarioBase
{
    private const string FirepitType = "Vintagestory.GameContent.BlockEntityFirepit";

    private ServerTickListenerInspector Listeners => Host!.TickListeners;

    [ServerScenario]
    public async Task AFirepit_ListsItsListeners_WithTheirOwners()
    {
        BlockPos pos = await GroundAsync();
        this.SetBlock(pos, "game:firepit-cold");

        IReadOnlyList<ServerTickListenerInfo> at = Listeners.At(pos);

        // Its own two, the inventory's 10 s tick it inherits, and its temperature behavior's.
        Assert.True(at.Count == 4, string.Join("\n", at));
        Assert.All(at, l => Assert.Equal(ListenerKind.Block, l.Kind));
        Assert.All(at, l => Assert.Equal("survival", l.Mod));
        ServerTickListenerInfo burn = Assert.Single(at, l => l.Handler.EndsWith(".OnBurnTick"));
        Assert.Equal(100, burn.IntervalMs);
        Assert.Equal(FirepitType, burn.OwnerType);
        ServerTickListenerInfo slow = Assert.Single(at, l => l.Handler.EndsWith(".On500msTick"));
        Assert.Equal(500, slow.IntervalMs);
        Assert.Equal(FirepitType, slow.OwnerType);
        ServerTickListenerInfo sensor = Assert.Single(at, l => l.Handler.EndsWith(".onTick"));
        Assert.Equal("Vintagestory.GameContent.BEBehaviorTemperatureSensitive", sensor.OwnerType);
        Assert.InRange(sensor.IntervalMs, 1900, 2099);
        ServerTickListenerInfo inventory = Assert.Single(at, l => l.Handler == "Vintagestory.GameContent.BlockEntityContainer.OnTick");
        Assert.Equal(10000, inventory.IntervalMs);
        Assert.Equal(FirepitType, inventory.OwnerType);

        Type firepit = Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlockEntity(pos)!.GetType());
        Assert.Equal(new[] { burn.Id, slow.Id, inventory.Id }.Order(), Listeners.OfType(firepit).Where(l => pos.Equals(l.Pos)).Select(l => l.Id).Order());
        Assert.Contains(Listeners.Of("survival"), l => l.Id == burn.Id);
        Assert.Contains(Listeners.All(), l => l.Kind == ListenerKind.World);
    }

    [ServerScenario]
    public async Task AFirepit_CooksMeat_UnderFastForward()
    {
        BlockPos pos = await GroundAsync();
        StillWeather();
        Host!.RunOnGameThread(() => PlaceFirepit(pos));
        Assert.Equal("redmeat-raw", Slot(pos, 1)?.Collectible.Code.Path);
        ServerTickListenerInfo burn = Assert.Single(Listeners.At(pos), l => l.Handler.EndsWith(".OnBurnTick"));
        ServerTickListenerInfo slow = Assert.Single(Listeners.At(pos), l => l.Handler.EndsWith(".On500msTick"));

        FastForwardResult result = await Listeners.FastForwardAsync(pos, TimeSpan.FromMinutes(2), TimeSpan.FromMilliseconds(100));

        Assert.Equal(TimeSpan.FromMinutes(2), result.Simulated);
        Assert.Equal(1200, result.Steps);
        Assert.Equal(1200, result.CallsTo(burn));
        Assert.Equal(240, result.CallsTo(slow));
        Assert.Equal("redmeat-cooked", Slot(pos, 2)?.Collectible.Code.Path);
        Assert.Null(Slot(pos, 1));
        Assert.True(Host.RunOnGameThread(() => Field<int>(Api!.World.BlockAccessor.GetBlockEntity(pos)!, "maxTemperature")) > 0, "The firepit never lit");

        // No 2 minute backlog for the next tick.
        Assert.All(Listeners.At(pos), l => Assert.InRange(l.SinceLastRunMs, 0, l.IntervalMs + 5000));
    }

    [ServerScenario]
    public async Task AFastForwardSplitInTwo_EndsWhereOneRunEnds()
    {
        BlockPos a = await GroundAsync();
        BlockPos b = a.AddCopy(3, 0, 0);
        StillWeather();
        Host!.RunOnGameThread(() =>
        {
            PlaceFirepit(a);
            PlaceFirepit(b);
        });

        TimeSpan step = TimeSpan.FromMilliseconds(100);
        // The 500 ms listener ends the first part 200 ms into an interval: the second part carries it on.
        await Listeners.FastForwardAsync(a, TimeSpan.FromMilliseconds(61300), step);
        await Listeners.FastForwardAsync(b, TimeSpan.FromMilliseconds(30700), step);
        await Listeners.FastForwardAsync(b, TimeSpan.FromMilliseconds(30600), step);

        foreach (string field in new[] { "furnaceTemperature", "fuelBurnTime", "inputStackCookingTime" })
        {
            (float one, float two) = Host.RunOnGameThread(() => (
                Field<float>(Api!.World.BlockAccessor.GetBlockEntity(a)!, field),
                Field<float>(Api!.World.BlockAccessor.GetBlockEntity(b)!, field)));
            Assert.Equal(one, two);
        }

        Assert.Equal(Slot(a, 2)?.Collectible.Code.Path, Slot(b, 2)?.Collectible.Code.Path);
    }

    [ServerScenario]
    public async Task BreakingAFirepit_LeavesNoListeners()
    {
        BlockPos pos = await GroundAsync();
        this.SetBlock(pos, "game:firepit-cold");

        Assert.Throws<PharosAssertException>(() => PharosAssert.NoTickListenersAt(Host!, pos));
        Host!.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(0, pos));

        PharosAssert.NoTickListenersAt(Host, pos);
    }

    [ServerScenario]
    public async Task AQuern_GrindsUnderFastForward_WhileAPlayerTurnsIt()
    {
        BlockPos pos = await GroundAsync();
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        this.SetBlock(pos, "game:quern-granite");
        Host!.RunOnGameThread(() =>
        {
            BlockEntity quern = Api!.World.BlockAccessor.GetBlockEntity(pos)!;
            ((IBlockEntityContainer)quern).Inventory[0].Itemstack = new ItemStack(Api.World.GetItem(new AssetLocation("game:grain-spelt")), 10);
            quern.GetType().GetMethod("SetPlayerGrinding")!.Invoke(quern, [alice.Player, true]);
        });

        // 4 s a grain. The quern counts its grinding players when SetPlayerGrinding is called, and
        // no tick runs before the fast-forward to call it again; the world's clock does not move
        // during the run either, so the player is never dropped.
        await Listeners.FastForwardAsync(pos, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

        Assert.Equal("flour-spelt", Slot(pos, 1)?.Collectible.Code.Path);
        // 7 grains in 30 s; 0.1 s steps summed in floats may leave the seventh just short.
        Assert.InRange(Slot(pos, 0)?.StackSize ?? 0, 3, 4);
        Assert.Equal(10 - Slot(pos, 0)!.StackSize, Slot(pos, 1)!.StackSize);
    }

    // A block on the surface at spawn, with its chunks loaded.
    private async Task<BlockPos> GroundAsync()
    {
        BlockPos spawn = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        await Host.EnsureChunksLoadedAsync(spawn.AddCopy(-8, 0, -8), spawn.AddCopy(8, 0, 8));
        return Host.RunOnGameThread(() =>
        {
            BlockPos pos = spawn.Copy();
            pos.Y = Api!.World.BlockAccessor.GetRainMapHeightAt(pos.X, pos.Z) + 1;
            return pos;
        });
    }

    // Rain cools a firepit at random; keep it dry and the clock still.
    private void StillWeather()
    {
        Host!.Calendar.Freeze();
        Host.Weather.SetPrecipitation(0);
    }

    // A firepit with fuel and raw meat that lights on its first tick. Run on the game thread.
    private void PlaceFirepit(BlockPos pos)
    {
        IWorldAccessor world = Api!.World;
        world.BlockAccessor.SetBlock(world.GetBlock(new AssetLocation("game:firepit-cold")).Id, pos);
        BlockEntity firepit = world.BlockAccessor.GetBlockEntity(pos)!;
        IInventory inventory = ((IBlockEntityContainer)firepit).Inventory;
        inventory[0].Itemstack = new ItemStack(world.GetItem(new AssetLocation("game:firewood")), 4);
        inventory[1].Itemstack = new ItemStack(world.GetItem(new AssetLocation("game:redmeat-raw")), 1);
        firepit.GetType().GetField("canIgniteFuel")!.SetValue(firepit, true);
    }

    private ItemStack? Slot(BlockPos pos, int slot) =>
        Host!.RunOnGameThread(() => ((IBlockEntityContainer)Api!.World.BlockAccessor.GetBlockEntity(pos)!).Inventory[slot].Itemstack);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
}
