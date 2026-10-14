using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Reinforces blocks through the survival mod's block reinforcement system. Pharos does not reference the
/// survival mod, so this driver finds its system by name at run time and calls it by reflection.
/// </summary>
public sealed class ReinforcementDriver
{
    private const string SurvivalAssembly = "VSSurvivalMod";
    private const string SystemTypeName = "Vintagestory.GameContent.ModSystemBlockReinforcement";
    private readonly EmbeddedServerHost _host;
    private readonly HashSet<(int X, int Y, int Z)> _placed = [];

    internal ReinforcementDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Reinforces the block at a position for a player. The player's reinforcement grant applies to the block.</summary>
    /// <param name="pos">The block position.</param>
    /// <param name="strength">The strength to give the block. Must be positive.</param>
    /// <param name="owner">The player who reinforces the block.</param>
    /// <returns>True when the survival system accepted the reinforcement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> or <paramref name="owner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strength"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The survival mod is not loaded.</exception>
    public bool Reinforce(BlockPos pos, int strength, ServerTestPlayer owner)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strength);
        return _host.RunOnGameThread(() =>
        {
            object system = SurvivalSystem();
            bool accepted = (bool)(system.GetType().GetMethod("StrengthenBlock")!
                .Invoke(system, [pos, owner.Player!, strength, strength]) ?? false);
            if (accepted) _placed.Add((pos.X, pos.Y, pos.Z));
            return accepted;
        });
    }

    /// <summary>Reads the reinforcement strength at a position, or null when the block is not reinforced.</summary>
    /// <param name="pos">The block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    public int? Strength(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            object system = SurvivalSystem();
            object? reinforcement = system.GetType().GetMethod("GetReinforcment")!.Invoke(system, [pos]);
            return reinforcement is null
                ? null
                : (int?)reinforcement.GetType().GetField("Strength")!.GetValue(reinforcement);
        });
    }

    /// <summary>Records the reinforcements the driver has placed so far, to be removed by <see cref="Baseline.Restore"/>.</summary>
    internal Baseline Capture() => new(this);

    private object SurvivalSystem()
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == SurvivalAssembly)?
            .GetType(SystemTypeName, throwOnError: false)
            ?? throw new InvalidOperationException($"The survival mod ({SurvivalAssembly}) is not loaded, so block reinforcement is unavailable.");
        MethodInfo getModSystem = typeof(Vintagestory.API.Common.IModLoader).GetMethods()
            .Single(method => method.Name == "GetModSystem" && method.IsGenericMethodDefinition);
        object? system = getModSystem.MakeGenericMethod(type).Invoke(_host.Server.Api.ModLoader, [false]);
        return system ?? throw new InvalidOperationException($"The server has no {SystemTypeName} system.");
    }

    private void ClearPlaced()
    {
        if (_placed.Count == 0) return;
        object system = SurvivalSystem();
        MethodInfo clear = system.GetType().GetMethod("ClearReinforcement")!;
        foreach ((int x, int y, int z) in _placed) clear.Invoke(system, [new BlockPos(x, y, z)]);
        _placed.Clear();
    }

    /// <summary>The reinforcements the world started with: none. A restore removes the ones a test placed.</summary>
    internal sealed class Baseline
    {
        private readonly ReinforcementDriver _driver;

        internal Baseline(ReinforcementDriver driver) => _driver = driver;

        /// <summary>Clears every reinforcement the driver placed since the world started.</summary>
        public void Restore() => _driver._host.RunOnGameThread(_driver.ClearPlaced);
    }
}
