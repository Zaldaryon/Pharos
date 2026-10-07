using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

[ProtoContract]
public class PharosTestMessage
{
    [ProtoMember(1)]
    public string Text { get; set; } = "";

    [ProtoMember(2)]
    public int Number { get; set; }
}

[Collection("Sequential")]
[ServerWorld(seed: 5151, Isolation = WorldIsolation.Restart)]
public class ReceivedPacketsTests : ServerScenarioBase
{
    private ICoreServerAPI Sapi => Api!;

    [ServerScenario]
    public async Task Chat_FromOnePlayerReachesTheOther()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");

        await alice.SayAsync("hello bob");
        Host!.Ticks(5);

        Assert.Contains(bob.Received.ChatMessages, m => m.Contains("hello bob"));
    }

    [ServerScenario]
    public async Task ServerMessage_ReachesThePlayer()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");

        Host!.RunOnGameThread(() => alice.Player!.SendMessage(0, "direct note", EnumChatType.Notification));
        Host.Ticks(3);

        Assert.Contains(alice.Received.Chat, c => c.Message.Contains("direct note"));
    }

    [ServerScenario]
    public async Task Entities_ArriveAndDepartForTheTrackingPlayer()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Vec3d near = alice.Entity!.Pos.XYZ.AddCopy(3, 0, 3);

        Entity hen = this.SpawnEntity("game:chicken-hen", near);
        await Host!.TickUntilAsync(() => alice.Received.KnowsEntity(hen.EntityId), maxTicks: 300);

        Assert.True(alice.Received.HasReceivedEntity(hen.EntityId));
        Assert.Contains(alice.Received.EntityArrivals, e => e.EntityId == hen.EntityId && e.EntityType.Contains("chicken"));

        Host.RunOnGameThread(() => hen.Die(EnumDespawnReason.Removed));
        await Host.TickUntilAsync(() => !alice.Received.KnowsEntity(hen.EntityId), maxTicks: 300);

        Assert.Contains(alice.Received.EntityDepartures, d => d.EntityId == hen.EntityId);
    }

    [ServerScenario]
    public async Task PlayerData_OfAnotherPlayerReachesThePlayer()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");

        await Host!.TickUntilAsync(() => alice.Received.HasReceivedPlayerData(bob.PlayerUID), maxTicks: 300);

        Assert.True(alice.Received.HasReceivedPlayerData(alice.PlayerUID));
        Assert.Contains(alice.Received.PlayerData, p => p.PlayerUid == bob.PlayerUID && p.PlayerName == "Bob");
    }

    [ServerScenario]
    public async Task ParticlesSoundsAndHighlights_ReachThePlayer()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Vec3d at = alice.Entity!.Pos.XYZ;
        List<BlockPos> marked = [at.AsBlockPos.AddCopy(1, 0, 0), at.AsBlockPos.AddCopy(2, 0, 0)];

        Host!.RunOnGameThread(() =>
        {
            Sapi.World.SpawnParticles(new SimpleParticleProperties(5, 5, ColorUtil.WhiteArgb, at, at.AddCopy(1, 1, 1), new Vec3f(), new Vec3f()));
            Sapi.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), at.X, at.Y, at.Z, null, false, 32f, 1f);
            Sapi.World.HighlightBlocks(alice.Player, 7, marked, [ColorUtil.WhiteArgb]);
        });
        Host.Ticks(5);

        Assert.NotEmpty(alice.Received.Particles);
        Assert.Contains(alice.Received.Sounds, s => s.Name.Contains("planks") && s.Position.DistanceTo(at) < 1);
        Assert.Equal(marked, alice.Received.HighlightedBlocks(7));
    }

    [ServerScenario]
    public async Task BlockChanges_NearThePlayerReachThePlayer()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");

        // Inside the chunk the player stands in: the server holds back changes in chunks it has
        // not sent the player yet, and only the player's own chunk is guaranteed at join.
        BlockPos at = alice.Entity!.Pos.AsBlockPos;
        BlockPos pos = new(at.X / 32 * 32 + (at.X % 32 < 16 ? 20 : 10), at.Y, at.Z / 32 * 32 + 16, 0);
        int granite = this.GetBlockId("game:rock-granite");

        this.SetBlock(pos, granite);
        await Host!.TickUntilAsync(() => alice.Received.BlockChanges.Any(c => c.Position.Equals(pos)), maxTicks: 300);

        Assert.Contains(alice.Received.BlockChanges, c => c.Position.Equals(pos) && c.BlockId == granite);
    }

    [ServerScenario]
    public async Task ModPackets_AreDecodedByMessageType()
    {
        IServerNetworkChannel channel = Host!.RunOnGameThread(() =>
            Sapi.Network.RegisterChannel("pharostest").RegisterMessageType<PharosTestMessage>());
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");

        Host.RunOnGameThread(() => channel.SendPacket(new PharosTestMessage { Text = "ping", Number = 42 }, alice.Player));
        Host.Ticks(3);

        PharosTestMessage message = Assert.Single(Host.ModPackets<PharosTestMessage>(alice, "pharostest"));
        Assert.Equal("ping", message.Text);
        Assert.Equal(42, message.Number);
        Assert.Contains(alice.Received.ModPackets, p => p.Channel == "pharostest");
    }
}
