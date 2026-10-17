using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A seeded server: the world's generator, statics, block entities and players, replay and restore.</summary>
[Seeded(1234)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = Isolation.Rollback)]
public class SeededRandomServerTests : ServerScenarioBase
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [ServerScenario]
    public void TheWorldGenerator_DrawsTheTestsStream()
    {
        Assert.Equal(1234, Host!.Random.CurrentSeed);
        Assert.Equal("[Seeded] on the class", Host.Random.CurrentSource);

        int[] drawn = Host.RunOnGameThread(() => Draw(Api!.World.Rand));
        Assert.Equal(Draw(new Random(RandomStreams.Mix(1234, "server:main:Vintagestory.Server.ServerMain.rand@Pharos server game thread"))), drawn);
    }

    [ServerScenario]
    public void ReseedingWithTheSameSeed_ReplaysAStaticAndADropRoll()
    {
        // A static nothing else draws from while the test runs.
        FieldInfo shelves = GameType("Vintagestory.GameContent.BookShelfTypeProps").GetField("rnd", AnyStatic)!;
        BlockDropItemStack drop = Host!.RunOnGameThread(() =>
        {
            BlockDropItemStack stack = new() { Type = EnumItemClass.Item, Code = new AssetLocation("game:flint"), Quantity = new NatFloat(3, 2.5f, EnumDistribution.UNIFORM) };
            Assert.True(stack.Resolve(Api!.World, "seeded test", null));
            return stack;
        });

        (int[] Shelves, int[] Drops) Run(int seed)
        {
            Host.Random.Seed(seed);
            return Host.RunOnGameThread(() => (Draw((Random)shelves.GetValue(null)!), Enumerable.Range(0, 20).Select(_ => drop.GetNextItemStack()?.StackSize ?? 0).ToArray()));
        }

        (int[] Shelves, int[] Drops) first = Run(5);
        (int[] Shelves, int[] Drops) other = Run(99);
        (int[] Shelves, int[] Drops) again = Run(5);

        Assert.Equal(Draw(new Random(RandomStreams.Mix(5, "static:Vintagestory.GameContent.BookShelfTypeProps.rnd"))), first.Shelves);
        Assert.Equal(first.Shelves, again.Shelves);
        Assert.Equal(first.Drops, again.Drops);
        Assert.NotEqual(first.Shelves, other.Shelves);
        Assert.NotEqual(first.Drops, other.Drops);
    }

    [ServerScenario]
    public void Seeded_NamesTheGeneratorsItReached()
    {
        IReadOnlyList<SeededRandom> seeded = Host!.Random.Seeded;

        // Drawn by the block tick thread too: checked by name and kind, never drawn here.
        Assert.Contains(seeded, s => s.Member == "Vintagestory.Server.ServerSystemBlockSimulation.rand" && s.How == "in place");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.Server.ServerSystemEntitySpawner.rand" && s.How == "in place");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.Server.ServerMain.rand" && s.How == "per thread");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.API.Common.BlockDropItemStack.random" && s.How == "in place");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.GameContent.BlockEntityFastForwardGrowth.rand");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.API.Common.SimpleParticleProperties.randTL" && s.How == "per thread");
        Assert.Contains(seeded, s => s.Member == "Vintagestory.API.MathTools.NatFloat.threadsafeRand" && s.How.EndsWith("on the game thread", StringComparison.Ordinal));
        Assert.Contains(seeded, s => s.Member == "Vintagestory.GameContent.BlockCactus.rand" && s.Count >= 1);
    }

    [ServerScenario]
    public async Task APlayerAndABlockEntity_MadeDuringTheTest_AreSeeded()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("seedy");
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        this.SetBlock(pos, "game:clayoven-north");

        (int[] Oven, int[] Aiming, int Index) drawn = Host!.RunOnGameThread(() =>
        {
            BlockEntity oven = Api!.World.BlockAccessor.GetBlockEntity(pos);
            Random prng = (Random)oven.GetType().GetField("prng", AnyInstance)!.GetValue(oven)!;

            List<EntityBehavior> behaviors = player.Entity!.SidedProperties.Behaviors;
            int index = behaviors.FindIndex(b => b.GetType().Name == "EntityBehaviorAimingAccuracy");
            Random aiming = (Random)behaviors[index].GetType().GetField("Rand", AnyInstance)!.GetValue(behaviors[index])!;
            return (Draw(prng), Draw(aiming), index);
        });

        Assert.Equal(Draw(new Random(RandomStreams.Mix(1234, $"server:be:game:clayoven-north@{pos.X},{pos.Y},{pos.Z}#0:Vintagestory.GameContent.BlockEntityOven.prng"))), drawn.Oven);
        Assert.Equal(Draw(new Random(RandomStreams.Mix(1234, $"server:entity:game:player#0/{drawn.Index}:Vintagestory.GameContent.EntityBehaviorAimingAccuracy.Rand"))), drawn.Aiming);
        Assert.Contains(Host.Random.Seeded, s => s.Member == "Vintagestory.GameContent.BlockEntityOven.prng");
    }

    [ServerScenario]
    public void Restore_PutsTheGamesGeneratorsBack()
    {
        ThreadLocal<Random> seeded = Server!.Server.rand;
        Host!.Random.Restore();

        Assert.Null(Host.Random.CurrentSeed);
        Assert.Same(this, StaticRandomSeeding.Owner);
        ThreadLocal<Random> original = Server.Server.rand;
        Assert.NotSame(seeded, original);

        Host.Random.Seed(1);
        Assert.NotSame(original, Server.Server.rand);
        Assert.Same(Host.Random, StaticRandomSeeding.Owner);
        Host.Random.Restore();
        Assert.Same(original, Server.Server.rand);
        Assert.Null(StaticRandomSeeding.Owner);
        Assert.Empty(Host.Random.Seeded);
    }

    [ServerScenario]
    public void Unseeded_ListsWhatPharosCannotSeed()
    {
        IReadOnlyList<UnseededRandom> unseeded = Host!.Random.Unseeded;

        Assert.Contains(unseeded, u => u.Member == "Vintagestory.API.Common.AiTaskBase.randTL" && u.Reason.Contains("[ThreadStatic]", StringComparison.Ordinal));
        Assert.Contains(unseeded, u => u.Member.StartsWith("Vintagestory.GameContent.AuroraRenderer.", StringComparison.Ordinal) && u.Reason.Contains("with no seed", StringComparison.Ordinal));
        Assert.DoesNotContain(unseeded, u => u.Member == "Vintagestory.API.Common.BlockDropItemStack.random");
        Assert.DoesNotContain(unseeded, u => u.Member == "Vintagestory.GameContent.BlockEntityFastForwardGrowth.rand");
        Assert.DoesNotContain(unseeded, u => u.Member == "Vintagestory.GameContent.BlockEntityOven.prng");
    }

    private static int[] Draw(Random random) => [.. Enumerable.Range(0, 8).Select(_ => random.Next())];

    private static Type GameType(string name) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;
}

/// <summary>A test that is not seeded keeps the game's own generators.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = Isolation.Rollback)]
public class UnseededServerTests : ServerScenarioBase
{
    [ServerScenario]
    public void WithoutSeeded_NothingIsSeeded()
    {
        Assert.Null(Host!.Random.CurrentSeed);
        Assert.Null(StaticRandomSeeding.Owner);
        Assert.Empty(Host.Random.Seeded);
    }
}

/// <summary>A seeded fixture-mode client.</summary>
[Seeded(7)]
[Collection("Sequential")]
public class SeededRandomClientTests : ClientScenarioBase
{
    [ClientScenario]
    public void TheClientsWorldGenerator_DrawsTheTestsStream()
    {
        Assert.Equal(7, Client!.Random.CurrentSeed);
        (int[] Drawn, string Thread) result = Client.RunOnClientThread(() => (Draw(Client.Client.Rand), System.Threading.Thread.CurrentThread.Name ?? "unnamed"));
        Assert.Equal(Draw(new Random(RandomStreams.Mix(7, $"client:main:Vintagestory.Client.NoObf.ClientMain.rand@{result.Thread}"))), result.Drawn);

        // A fixture-mode client runs on the test's thread: its [ThreadStatic] generators are reported, not seeded.
        Assert.Contains(Client.Random.Unseeded, u => u.Member == "Vintagestory.API.MathTools.NatFloat.threadsafeRand" && u.Reason.Contains("fixture-mode", StringComparison.Ordinal));
        Assert.DoesNotContain(Client.Random.Seeded, s => s.Member == "Vintagestory.API.MathTools.NatFloat.threadsafeRand");

        ThreadLocal<Random> seeded = Client.Client.rand;
        Client.Random.Restore();
        Assert.NotSame(seeded, Client.Client.rand);

        // The statics belong to the scenario, which puts them back when the test ends.
        Assert.Same(this, StaticRandomSeeding.Owner);
    }

    private static int[] Draw(Random random) => [.. Enumerable.Range(0, 8).Select(_ => random.Next())];
}

/// <summary>A seeded client-server scenario: both sides, and SeedRandom for both.</summary>
[Seeded(11)]
[Collection("Sequential")]
public class SeededRandomClientServerTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = Zaldaryon.Pharos.Bootstrap.ClientBootMode.Engine, Width = 640, Height = 360 };

    [ClientServerScenario]
    public void BothSides_AreSeeded_AndSeedRandomReseedsBoth()
    {
        Assert.Equal(11, ServerHost!.Random.CurrentSeed);
        Assert.Equal(11, Client!.Random.CurrentSeed);
        Assert.Same(this, StaticRandomSeeding.Owner);

        // An engine-mode client has a thread of its own, where [ThreadStatic] generators are seeded.
        Assert.Contains(Client.Random.Seeded, s => s.Member == "Vintagestory.API.MathTools.NatFloat.threadsafeRand" && s.How.EndsWith("on the game thread", StringComparison.Ordinal));
        Assert.Contains(Client.Random.Seeded, s => s.Member == "Vintagestory.Client.NoObf.ClientMain.rand" && s.How == "per thread");

        ThreadLocal<Random> clientRand = Client.Client.rand;
        SeedRandom(12);
        Assert.Equal(12, ServerHost.Random.CurrentSeed);
        Assert.Equal(12, Client.Random.CurrentSeed);
        Assert.Equal("SeedRandom(12)", Client.Random.CurrentSource);
        Assert.NotSame(clientRand, Client.Client.rand);
        Assert.Same(this, StaticRandomSeeding.Owner);
    }
}
