using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>What the storm of the world is doing.</summary>
/// <param name="Active">Whether a storm is running now.</param>
/// <param name="NextStormDays">The day the next storm starts.</param>
/// <param name="Strength">The strength of the next storm.</param>
/// <param name="GlitchStrength">How much the world comes apart during the storm.</param>
/// <param name="SpawnPatternCode">The code of the pattern the storm spawns with.</param>
public sealed record TemporalStorm(
    bool Active,
    double NextStormDays,
    int Strength,
    float GlitchStrength,
    string SpawnPatternCode);

/// <summary>One rift of the world.</summary>
/// <param name="RiftId">The rift's id.</param>
/// <param name="Position">Where the rift is.</param>
/// <param name="SpawnedTotalHours">The hour the rift appeared.</param>
/// <param name="DieAtTotalHours">The hour the rift disappears.</param>
public sealed record TemporalRift(int RiftId, Vec3d Position, double SpawnedTotalHours, double DieAtTotalHours);

/// <summary>What a rift ward does.</summary>
/// <param name="On">Whether the ward holds the rifts back.</param>
public sealed record TemporalWard(bool On);

/// <summary>The strength of a temporal storm, as the game names it.</summary>
public enum TemporalStormStrength
{
    /// <summary>A light storm.</summary>
    Light = 0,

    /// <summary>A medium storm.</summary>
    Medium = 1,

    /// <summary>A heavy storm.</summary>
    Heavy = 2,
}

/// <summary>
/// The temporal systems of the world: how stable a place is, whether a storm runs, where the
/// rifts are and what the rift wards hold back.
/// </summary>
public sealed class TemporalDriver
{
    private const string StabilitySystem = "Vintagestory.GameContent.SystemTemporalStability";
    private const string RiftsSystem = "Vintagestory.GameContent.ModSystemRifts";
    private const string RiftWardBlockEntity = "BlockEntityRiftWard";

    private readonly EmbeddedServerHost _host;

    internal TemporalDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// How stable a place is, the way the game says it.
    /// </summary>
    /// <param name="pos">The place to read.</param>
    public float StabilityAt(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            object? system = System(StabilitySystem);
            if (system == null) return 1f;
            return (float)(GameMembers.Call(system, "GetTemporalStability", pos) ?? 1f);
        });
    }

    /// <summary>
    /// The storm of the world, as the game keeps it.
    /// </summary>
    public TemporalStorm Storm
    {
        get
        {
            return _host.RunOnGameThread(() =>
            {
                object? system = System(StabilitySystem);
                if (system == null) return new TemporalStorm(false, 0, 0, 0, "");

                object? data = GameMembers.TryGet(system, "StormData");
                if (data == null) return new TemporalStorm(false, 0, 0, 0, "");

                return new TemporalStorm(
                    (bool)(GameMembers.TryGet(data, "nowStormActive") ?? false),
                    (double)(GameMembers.TryGet(data, "nextStormTotalDays") ?? 0d),
                    Convert.ToInt32(GameMembers.TryGet(data, "nextStormStrength") ?? 0),
                    (float)(GameMembers.TryGet(data, "stormGlitchStrength") ?? 0f),
                    (string)(GameMembers.TryGet(data, "spawnPatternCode") ?? ""));
            });
        }
    }

    /// <summary>
    /// Starts a storm now, with a strength, the way the game's own storm tick does.
    /// </summary>
    /// <param name="strength">The strength of the storm.</param>
    /// <param name="ct">Cancels the wait.</param>
    public Task StartStormAsync(TemporalStormStrength strength = TemporalStormStrength.Heavy, CancellationToken ct = default)
    {
        int level = (int)strength;
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 2);

        ct.ThrowIfCancellationRequested();
        _host.RunOnGameThread(() =>
        {
            object? system = System(StabilitySystem);
            if (system == null) return;

            object? data = GameMembers.TryGet(system, "StormData");
            if (data == null) return;

            // The field is the game's own enumeration, light, medium and heavy.
            object strengthValue = GameMembers.TryGet(data, "nextStormStrength") ?? 0;
            GameMembers.Set(data, "nextStormStrength", Enum.ToObject(strengthValue.GetType(), level));
            GameMembers.Set(data, "nextStormStrDouble", (double)level);
            GameMembers.Set(data, "nextStormTotalDays", _host.Api.World.Calendar.TotalDays);
            GameMembers.Set(data, "stormDayNotify", 0);

            // The storm begins when the world's own storm tick runs.
            GameMembers.Call(system, "onTempStormTick", 0f);
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ends the storm of the world now.
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    public Task EndStormAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _host.RunOnGameThread(() =>
        {
            object? system = System(StabilitySystem);
            if (system == null) return;

            object? data = GameMembers.TryGet(system, "StormData");
            if (data == null) return;

            GameMembers.Set(data, "nowStormActive", false);
            GameMembers.Set(data, "stormGlitchStrength", 0f);
            GameMembers.Set(data, "stormActiveTotalDays", 0d);

            // Move the next storm into the future, so the world's own storm tick does not begin
            // another one right away.
            GameMembers.Call(system, "prepareNextStorm");
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// The rifts that are open now.
    /// </summary>
    public IReadOnlyList<TemporalRift> Rifts
    {
        get
        {
            return _host.RunOnGameThread(() =>
            {
                List<TemporalRift> rifts = [];
                object? system = System(RiftsSystem);
                if (system == null) return (IReadOnlyList<TemporalRift>)rifts;

                if (GameMembers.TryGet(system, "ServerRifts") is System.Collections.IEnumerable list)
                {
                    foreach (object? rift in list)
                    {
                        if (rift != null) rifts.Add(RiftOf(rift));
                    }
                }

                return (IReadOnlyList<TemporalRift>)rifts;
            });
        }
    }

    /// <summary>
    /// Opens a rift at a place, with the hours it stays, and adds it to the game's own list.
    /// </summary>
    /// <param name="pos">Where the rift opens.</param>
    /// <param name="hours">How long the rift stays open.</param>
    /// <returns>The rift that was opened.</returns>
    public TemporalRift SpawnRift(BlockPos pos, double hours = 1)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hours);

        return _host.RunOnGameThread(() =>
        {
            object system = System(RiftsSystem)
                ?? throw new InvalidOperationException("The rifts system is not loaded.");

            object rifts = GameMembers.Get(system, "ServerRifts")!;
            Type riftType = rifts.GetType().GetGenericArguments()[0];
            object rift = Activator.CreateInstance(riftType)
                ?? throw new InvalidOperationException("The game's rift type could not be created.");

            double now = _host.Api.World.Calendar.TotalHours;
            int id = Convert.ToInt32(GameMembers.Get(system, "NextRiftId"));
            GameMembers.Set(rift, "RiftId", id);
            GameMembers.Set(rift, "Position", new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5));
            GameMembers.Set(rift, "SpawnedTotalHours", now);
            GameMembers.Set(rift, "DieAtTotalHours", now + hours);

            ((System.Collections.IList)rifts).Add(rift);
            ((System.Collections.IDictionary)GameMembers.Get(system, "riftsById")!).Add(id, rift);

            // The wards listen to the game's own event, so tell them what was opened.
            if (GameMembers.TryGet(system, "OnRiftSpawned") is Delegate spawned)
            {
                spawned.DynamicInvoke(rift);
            }

            return RiftOf(rift);
        });
    }

    /// <summary>Closes every rift that is open now.</summary>
    public void ClearRifts()
    {
        _host.RunOnGameThread(() =>
        {
            object? system = System(RiftsSystem);
            if (system == null) return;

            ((System.Collections.IList)GameMembers.Get(system, "ServerRifts")!).Clear();
            ((System.Collections.IDictionary)GameMembers.Get(system, "riftsById")!).Clear();
        });
    }

    /// <summary>
    /// Reads what a rift ward does.
    /// </summary>
    /// <param name="pos">The rift ward.</param>
    public TemporalWard Ward(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            object? ward = GameMembers.FindBlockEntity(_host.Api.World.BlockAccessor, pos, RiftWardBlockEntity);
            if (ward == null) return new TemporalWard(false);
            return new TemporalWard((bool)(GameMembers.TryGet(ward, "On") ?? false));
        });
    }

    /// <summary>
    /// Switches a rift ward on or off, the way the ward does.
    /// </summary>
    /// <param name="pos">The rift ward.</param>
    /// <param name="on">Whether the ward holds the rifts back.</param>
    public bool SetWard(BlockPos pos, bool on)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            object? ward = GameMembers.FindBlockEntity(_host.Api.World.BlockAccessor, pos, RiftWardBlockEntity);
            if (ward == null) return false;

            if (on)
            {
                // A ward that was just placed may not have been initialized yet, and it only
                // switches on while it has fuel.
                if (ward is BlockEntity blockEntity && blockEntity.Api == null)
                {
                    blockEntity.Initialize(_host.Api);
                }

                if (GameMembers.TryGet(ward, "fuelDays") is double fuel && fuel <= 0)
                {
                    GameMembers.Set(ward, "fuelDays", 1d);
                }

                GameMembers.Call(ward, "Activate");
            }
            else GameMembers.Call(ward, "Deactivate");
            return true;
        });
    }

    /// <summary>
    /// Holds a temporal gear on a block until it sets the player's spawn point, the way a player
    /// does.
    /// </summary>
    /// <param name="player">The player who holds the gear.</param>
    /// <param name="slot">The hotbar slot the gear is in.</param>
    /// <param name="ticks">How many ticks to hold it.</param>
    /// <param name="target">The block to hold it on, or the player's own place.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The result of the held use.</returns>
    public async Task<BotResult> UseGearAsync(
        ServerTestPlayer player,
        int slot = 0,
        int ticks = 90,
        BlockPos? target = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);

        await player.Bot.SelectSlotAsync(slot, ct: ct).ConfigureAwait(false);
        BlockPos at = target
            ?? player.Entity?.Pos.AsBlockPos
            ?? throw new InvalidOperationException($"The player '{player.PlayerName}' has no entity.");

        // The gear measures its hold in the time that passed, not in ticks, so the hold runs on a
        // clock the ticks drive.
        using VirtualClock? clock = _host.VirtualClock is null ? _host.UseVirtualClock(tickMs: 50) : null;
        return await player.Bot.UseHeldAsync(ticks, at, ct: ct).ConfigureAwait(false);
    }

    private static TemporalRift RiftOf(object rift) => new(
        Convert.ToInt32(GameMembers.TryGet(rift, "RiftId") ?? 0),
        GameMembers.TryGet(rift, "Position") as Vec3d ?? new Vec3d(),
        (double)(GameMembers.TryGet(rift, "SpawnedTotalHours") ?? 0d),
        (double)(GameMembers.TryGet(rift, "DieAtTotalHours") ?? 0d));

    private object? System(string typeName) => _host.Api.ModLoader.GetModSystem(typeName);
}
