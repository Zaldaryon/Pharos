using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// <see cref="EmbeddedServerHost.TakeSnapshot"/> and <see cref="EmbeddedServerHost.RestoreSnapshot"/>
/// on a live server.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
public class WorldSnapshotTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task RestoreSnapshot_PutsBlocksEntitiesAndPlayersBackWithoutARestart()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        string aliceUid = alice.PlayerUID;
        BlockPos pos = alice.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        int before = BlockIdAt(pos);
        int granite = this.GetBlockId("game:rock-granite");
        Assert.NotEqual(granite, before);

        WorldSnapshot snapshot = Host!.TakeSnapshot();
        alice.Dispose();

        this.SetBlock(pos, granite);
        Entity hen = this.SpawnEntity("game:chicken-hen", pos.ToVec3d().Add(0.5, 1, 0.5));
        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
        string bobUid = bob.PlayerUID;
        bob.GiveItem("game:stick", 3);
        bob.Dispose();

        int restored = Host.RestoreSnapshot(snapshot);

        Assert.True(Host.IsRunning);
        Assert.True(restored > 0, "No chunk was restored");
        Assert.Equal(before, BlockIdAt(pos));
        Assert.Null(Host.RunOnGameThread(() => Api!.World.GetEntityById(hen.EntityId)));
        Assert.False(Host.RunOnGameThread(() => Server!.PlayerDataManager.WorldDataByUID.ContainsKey(bobUid)), "A player who joined after the snapshot was kept");
        Assert.True(Host.RunOnGameThread(() => Server!.PlayerDataManager.WorldDataByUID.ContainsKey(aliceUid)), "A player from the snapshot was forgotten");
    }

    [ServerScenario]
    public async Task RestoreSnapshot_BringsBackAnEntityThatDied()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Entity hen = this.SpawnEntity("game:chicken-hen", alice.Entity!.Pos.XYZ.AddCopy(2, 0, 0));
        Host!.Ticks(5);

        WorldSnapshot snapshot = Host.TakeSnapshot();
        Host.RunOnGameThread(() => hen.Die(EnumDespawnReason.Death));
        Host.Ticks(5);
        Assert.False(Host.RunOnGameThread(() => Api!.World.GetEntityById(hen.EntityId)?.Alive ?? false), "The hen did not die");

        Host.RestoreSnapshot(snapshot);

        Entity? back = Host.RunOnGameThread(() => Api!.World.GetEntityById(hen.EntityId));
        Assert.NotNull(back);
        Assert.True(back!.Alive);
        Assert.Equal("chicken-hen", back.Code.Path);
    }

    [ServerScenario]
    public void RestoreSnapshot_LeavesAnUnchangedWorldAlone()
    {
        Host!.Ticks(5);
        WorldSnapshot snapshot = Host.TakeSnapshot();

        int restored = Host.RestoreSnapshot(snapshot);

        Assert.Equal(0, restored);
    }

    private int BlockIdAt(BlockPos pos) =>
        Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).BlockId);
}

/// <summary>
/// <see cref="WorldIsolation.Rollback"/>: both tests dirty the world the same way and check that
/// they start from a clean one, on the same server. Whichever runs second sees the rollback.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4243, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Rollback)]
public class WorldRollbackIsolationTests : ServerScenarioBase
{
    private static EmbeddedServerHost? s_previousHost;

    [ServerScenario]
    public Task FirstOrSecond_StartsFromTheBootedWorld() => DirtyTheWorldAsync();

    [ServerScenario]
    public Task SecondOrFirst_StartsFromTheBootedWorld() => DirtyTheWorldAsync();

    private async Task DirtyTheWorldAsync()
    {
        if (s_previousHost != null)
        {
            Assert.Same(s_previousHost, Host);
        }

        s_previousHost = Host;

        ServerTestPlayer player = await CreateTestPlayerAsync("Roller");
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        int granite = this.GetBlockId("game:rock-granite");

        Assert.NotEqual(granite, Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).BlockId));
        Assert.False(player.HasItem("game:stick"), "The player kept an item from the previous test");
        Assert.Empty(Host.RunOnGameThread(() => Api!.World.GetEntitiesAround(pos.ToVec3d(), 8, 8, e => e.Code.Path == "chicken-hen")));

        this.SetBlock(pos, granite);
        this.SpawnEntity("game:chicken-hen", pos.ToVec3d().Add(0.5, 1, 0.5));
        player.GiveItem("game:stick", 3);
        Host.Ticks(5);
    }
}
