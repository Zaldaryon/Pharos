using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>What the player-state tests change, and read back, on the server's game thread.</summary>
internal static class PlayerStateProbe
{
    public const string Key = "pharos:test";
    public const string KeptKey = "pharos:keep";
    public const string KeptAttribute = "pharostest:keep";

    /// <summary>What a test reads of a player and the world.</summary>
    public sealed record State(
        bool Alive, float Health, float Saturation, float WalkSpeed, string? Spawn, string Role, bool HasPrivilege, bool HasRuntimePrivilege,
        bool PlayerModData, bool SaveModData, bool RegionModData, int KeptModData, int KeptAttribute, bool SameServerData, int MaxHealthModifiers);

    public static State Read(ICoreServerAPI api, IServerPlayer player)
    {
        EntityPlayer entity = player.Entity;
        SaveGame save = (SaveGame)api.WorldManager.SaveGame;
        IMapRegion? region = Region(api, entity);
        byte[]? kept = save.GetData(KeptKey);
        return new State(
            entity.Alive,
            entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") ?? -1,
            entity.WatchedAttributes.GetTreeAttribute("hunger")?.GetFloat("currentsaturation") ?? -1,
            entity.Stats.GetBlended("walkspeed"),
            player.WorldData is ServerWorldPlayerData { SpawnPosition: { } spawn } ? spawn.ToString() : null,
            player.Role.Code,
            player.HasPrivilege("pharostest"),
            player.HasPrivilege("pharosruntime"),
            player.GetModdata(Key) != null,
            save.GetData(Key) != null,
            region?.GetModdata(Key) != null,
            kept == null ? 0 : kept[0],
            entity.WatchedAttributes.GetInt(KeptAttribute),
            ReferenceEquals(((ServerPlayer)player).serverdata, api.PlayerData.GetPlayerDataByUid(player.PlayerUID)),
            Modifiers(entity)?.Count ?? 0);
    }

    /// <summary>Hurts, starves, slows and moves the player's spawn, and leaves mod data everywhere.</summary>
    public static void Dirty(ICoreServerAPI api, IServerPlayer player)
    {
        EntityPlayer entity = player.Entity;
        entity.WatchedAttributes.GetTreeAttribute("health").SetFloat("currenthealth", 3);
        entity.WatchedAttributes.MarkPathDirty("health");
        entity.WatchedAttributes.GetTreeAttribute("hunger").SetFloat("currentsaturation", 100);
        entity.WatchedAttributes.MarkPathDirty("hunger");
        entity.Stats.Set("walkspeed", "pharostest", -0.5f);
        entity.GetBehavior("health")!.GetType().GetMethod("SetMaxHealthModifiers")!.Invoke(entity.GetBehavior("health"), ["pharostest", 5f]);
        entity.WatchedAttributes.SetInt(KeptAttribute, entity.WatchedAttributes.GetInt(KeptAttribute) + 1);

        player.SetSpawnPosition(new PlayerSpawnPos(7, 80, 7));
        api.Permissions.SetRole(player, player.Role.Code == "suvisitor" ? "admin" : "suvisitor");
        api.Permissions.GrantPrivilege(player.PlayerUID, "pharostest", permanent: true);
        api.Permissions.GrantPrivilege(player.PlayerUID, "pharosruntime");

        player.SetModdata(Key, [1]);
        SaveGame save = (SaveGame)api.WorldManager.SaveGame;
        save.StoreData(Key, [1]);
        byte[]? kept = save.GetData(KeptKey);
        save.StoreData(KeptKey, [(byte)((kept?[0] ?? 0) + 1)]);
        Region(api, entity)!.SetModdata(Key, [1]);
    }

    public static IServerPlayer Player(ICoreServerAPI api, string uid) => (IServerPlayer)api.World.PlayerByUid(uid);

    public static ITreeAttribute HungerTreeOfBehaviour(Entity entity)
    {
        EntityBehavior hunger = entity.GetBehavior("hunger")!;
        return (ITreeAttribute)hunger.GetType().GetField("hungerTree", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hunger)!;
    }

    private static IMapRegion? Region(ICoreServerAPI api, Entity entity)
    {
        BlockPos pos = entity.Pos.AsBlockPos;
        return api.WorldManager.GetMapRegion(pos.X / api.WorldManager.RegionSize, pos.Z / api.WorldManager.RegionSize);
    }

    private static Dictionary<string, float>? Modifiers(Entity entity) =>
        entity.GetBehavior("health")?.GetType().GetField("maxHealthModifiers", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entity.GetBehavior("health")) as Dictionary<string, float>;
}

/// <summary>
/// <see cref="EmbeddedServerHost.RestoreSnapshot(WorldSnapshot, WorldRestoreFilter)"/> on a
/// connected player: entity state, player data and mod data come back.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4244, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
public class PlayerStateSnapshotTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task RestoreSnapshot_PutsBackAConnectedPlayersVitalsSpawnPrivilegesAndModData()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Host!.Ticks(5);
        string uid = alice.PlayerUID;
        PlayerStateProbe.State before = Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid)));
        WorldSnapshot snapshot = Host.TakeSnapshot();
        ITreeAttribute hunger = Host.RunOnGameThread(() => PlayerStateProbe.HungerTreeOfBehaviour(alice.Entity!));

        Host.RunOnGameThread(() => PlayerStateProbe.Dirty(Api!, PlayerStateProbe.Player(Api!, uid)));
        PlayerStateProbe.State dirty = Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid)));
        Assert.NotEqual(before.Health, dirty.Health);
        Assert.NotEqual(before.Spawn, dirty.Spawn);
        Assert.True(dirty.HasPrivilege && dirty.HasRuntimePrivilege && dirty.SaveModData && dirty.RegionModData && dirty.PlayerModData, dirty.ToString());

        WorldRestoreResult result = Host.RestoreSnapshot(snapshot, new WorldRestoreFilter(ModData: k => k != PlayerStateProbe.KeptKey, PlayerAttribute: k => k != PlayerStateProbe.KeptAttribute));
        PlayerStateProbe.State after = Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid)));

        Assert.Equal(1, result.PlayersRestored);
        Assert.Equal(1, result.ModDataKeysRestored);
        Assert.True(result.MapRegionsRestored >= 1, result.ToString());
        Assert.Equal(before with { KeptModData = 1, KeptAttribute = before.KeptAttribute + 1 }, after);
        Assert.True(after.SameServerData, "The player and the permissions API hold different server data");
        Assert.Same(hunger, Host.RunOnGameThread(() => alice.Entity!.WatchedAttributes.GetTreeAttribute("hunger")));

        // Nothing changed since: a second restore puts back no mod data key and no player state.
        WorldRestoreResult again = Host.RestoreSnapshot(snapshot, new WorldRestoreFilter(ModData: k => k != PlayerStateProbe.KeptKey, PlayerAttribute: k => k != PlayerStateProbe.KeptAttribute));
        Assert.Equal(0, again.ModDataKeysRestored);
        Assert.Equal(0, again.ChunksRestored);
        Assert.Equal(after, Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid))));
    }

    [ServerScenario]
    public async Task RestoreSnapshot_RevivesAPlayerThatDied()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Host!.Ticks(5);
        string uid = alice.PlayerUID;
        PlayerStateProbe.State before = Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid)));
        WorldSnapshot snapshot = Host.TakeSnapshot();

        Host.RunOnGameThread(() => alice.Entity!.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void, Type = EnumDamageType.Injury }));
        Host.Ticks(5);
        Assert.False(Host.RunOnGameThread(() => alice.Entity!.Alive), "The player did not die");

        Host.RestoreSnapshot(snapshot, WorldRestoreFilter.All);
        Host.Ticks(5);

        PlayerStateProbe.State after = Host.RunOnGameThread(() => PlayerStateProbe.Read(Api!, PlayerStateProbe.Player(Api!, uid)));
        Assert.True(after.Alive);
        Assert.Equal(before.Health, after.Health);
        Assert.Equal(0, Host.RunOnGameThread(() => alice.Entity!.WatchedAttributes.GetInt("entityDead")));
    }
}

/// <summary>
/// <see cref="WorldIsolation.Rollback"/> on a server scenario: save-game and map region mod data
/// come back, except a key the class keeps. Whichever test runs later checks the earlier one.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4245, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Rollback)]
public class LiveServerModDataRollbackTests : ServerScenarioBase
{
    private static int s_tests;

    protected override bool RestoreModData(string key) => key != PlayerStateProbe.KeptKey;

    [ServerScenario]
    public void First() => LeavesModDataBehind();

    [ServerScenario]
    public void Second() => LeavesModDataBehind();

    private void LeavesModDataBehind()
    {
        int earlier = s_tests++;
        Assert.True(Isolation!.Kind is IsolationKind.FirstBoot or IsolationKind.RolledBack, Isolation.ToString());
        if (Isolation.Kind == IsolationKind.RolledBack)
        {
            Assert.Equal(1, Isolation.ModDataKeysRestored);
            Assert.True(Isolation.MapRegionsRestored >= 1, Isolation.ToString());
        }

        int kept = Host!.RunOnGameThread(() =>
        {
            SaveGame save = (SaveGame)Api!.WorldManager.SaveGame;
            Assert.Null(save.GetData(PlayerStateProbe.Key));
            IMapRegion region = Api.WorldManager.GetMapRegion(0, 0) ?? Api.WorldManager.AllLoadedMapRegions.Values.First();
            Assert.Null(region.GetModdata(PlayerStateProbe.Key));

            byte[]? keep = save.GetData(PlayerStateProbe.KeptKey);
            save.StoreData(PlayerStateProbe.Key, [1]);
            save.StoreData(PlayerStateProbe.KeptKey, [(byte)((keep?[0] ?? 0) + 1)]);
            region.SetModdata(PlayerStateProbe.Key, [1]);
            return keep?[0] ?? 0;
        });

        if (Isolation.Kind == IsolationKind.RolledBack) Assert.Equal(earlier, kept);
    }
}

/// <summary>
/// <see cref="WorldIsolation.Rollback"/> on a client-server pair: every test hurts, starves and
/// slows the player, moves its spawn, changes its role and privileges, and leaves mod data; one
/// also kills it. Whichever runs later checks that it starts from the booted state, on the server
/// and on the client, and that the keys the class keeps were kept.
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4246, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Rollback)]
public class LiveClientServerPlayerStateRollbackTests : ClientServerScenarioBase
{
    private static PlayerStateProbe.State? s_booted;
    private static int s_tests;

    protected override bool RestoreModData(string key) => key != PlayerStateProbe.KeptKey;

    protected override bool RestorePlayerAttribute(string key) => key != PlayerStateProbe.KeptAttribute;

    [ClientServerScenario]
    public Task First() => StartsFromTheBootedStateAsync(die: false);

    [ClientServerScenario]
    public Task Second() => StartsFromTheBootedStateAsync(die: true);

    [ClientServerScenario]
    public Task Third() => StartsFromTheBootedStateAsync(die: false);

    private async Task StartsFromTheBootedStateAsync(bool die)
    {
        int earlier = s_tests++;
        string uid = ServerHost!.RunOnGameThread(() => Server!.GetClientByPlayername(PlayerName).Player.PlayerUID);
        ICoreServerAPI api = (ICoreServerAPI)Server!.Api;
        PlayerStateProbe.State now = ServerHost.RunOnGameThread(() => PlayerStateProbe.Read(api, PlayerStateProbe.Player(api, uid)));
        Assert.True(Isolation!.Kind is IsolationKind.FirstBoot or IsolationKind.RolledBack, Isolation.ToString());

        if (Isolation!.Kind == IsolationKind.FirstBoot)
        {
            s_booted = now;
        }
        else if (Isolation.Kind == IsolationKind.RolledBack && s_booted is { } booted)
        {
            Assert.Equal(1, Isolation.PlayersRestored);
            Assert.Equal(1, Isolation.ModDataKeysRestored);
            Assert.True(Isolation.MapRegionsRestored >= 1, Isolation.ToString());
            Assert.Equal(booted with { KeptModData = earlier, KeptAttribute = earlier }, now);

            // The client has what the server has.
            (bool alive, float health, float saturation, BlockPos? spawn, bool dead) = Client!.RunOnClientThread(() =>
            {
                EntityPlayer entity = Client.Client.EntityPlayer;
                return (entity.Alive,
                    entity.WatchedAttributes.GetTreeAttribute("health").GetFloat("currenthealth"),
                    entity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("currentsaturation"),
                    Client.Client.player.SpawnPosition?.Copy(),
                    Client.Client.api.Gui.OpenedGuis.Any(g => g.GetType().Name == "GuiDialogDead"));
            });
            Assert.True(alive);
            Assert.False(dead, "The death dialog is still open");
            Assert.Equal(now.Health, health);
            Assert.Equal(now.Saturation, saturation);
            BlockPos serverSpawn = ServerHost.RunOnGameThread(() => PlayerStateProbe.Player(api, uid).GetSpawnPosition(false).AsBlockPos);
            Assert.Equal(serverSpawn, spawn);
        }

        ServerHost.RunOnGameThread(() => PlayerStateProbe.Dirty(api, PlayerStateProbe.Player(api, uid)));
        if (die)
        {
            ServerHost.RunOnGameThread(() => PlayerStateProbe.Player(api, uid).Entity.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void, Type = EnumDamageType.Injury }));
        }

        await Session!.StepFramesAsync(20);
        if (die) Assert.False(Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Alive), "The client did not see the player die");
    }
}
