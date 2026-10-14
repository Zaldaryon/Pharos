using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>The state of a farmland block and the crop on it.</summary>
/// <param name="Nitrogen">The nitrogen in the soil.</param>
/// <param name="Phosphorus">The phosphorus in the soil.</param>
/// <param name="Potassium">The potassium in the soil.</param>
/// <param name="Moisture">The moisture level of the soil.</param>
/// <param name="Crop">The crop block code on the farmland, or null when nothing is planted.</param>
/// <param name="CropStage">The crop's current growth stage, or 0 when nothing is planted.</param>
/// <param name="NextStageAtHours">The total game hours at which the crop reaches its next stage.</param>
public readonly record struct FarmlandReading(
    float Nitrogen,
    float Phosphorus,
    float Potassium,
    float Moisture,
    string? Crop,
    int CropStage,
    double NextStageAtHours);

/// <summary>Plants crops on farmland and reads and advances them. Crops and their stages come from the game's farming system.</summary>
public sealed class FarmingDriver
{
    private readonly EmbeddedServerHost _host;

    internal FarmingDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Places a farmland block the way the game creates it from soil: the block is set, then its block entity
    /// takes the soil's starting nutrients from the block's fertility. Without that step the field has no nutrients.
    /// </summary>
    /// <param name="pos">The position of the new farmland block.</param>
    /// <param name="farmlandCode">The farmland block code, such as <c>game:farmland-moist-medium</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="farmlandCode"/> is empty or names no block.</exception>
    public void PlaceField(BlockPos pos, string farmlandCode)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentException.ThrowIfNullOrWhiteSpace(farmlandCode);
        _host.RunOnGameThread(() =>
        {
            Block field = ServerApi.World.GetBlock(new AssetLocation(farmlandCode))
                ?? throw new ArgumentException($"The game has no block named {farmlandCode}.", nameof(farmlandCode));
            ServerApi.World.BlockAccessor.SetBlock(field.BlockId, pos);
            object soil = ServerApi.World.BlockAccessor.GetBlockEntity(pos)
                ?? throw new InvalidOperationException($"The farmland at {pos} has no block entity.");
            soil.GetType().GetMethod("OnCreatedFromSoil")?.Invoke(soil, [field, null]);
        });
    }

    /// <summary>Reads the soil and the crop at a farmland position.</summary>
    /// <param name="pos">The farmland block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The block at the position is not farmland.</exception>
    public FarmlandReading Farmland(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = ServerApi.World.BlockAccessor;
            object soil = blocks.GetBlockEntity(pos)
                ?? throw new InvalidOperationException($"There is no farmland block entity at {pos}.");
            float[] nutrients = (float[])Read(soil, "Nutrients")!;
            float moisture = Convert.ToSingle(Read(soil, "MoistureLevel") ?? 0f);
            double nextStage = Convert.ToDouble(Read(soil, "TotalHoursForNextStage") ?? 0d);

            Block above = blocks.GetBlock(pos.UpCopy());
            bool planted = above.Code.Path.StartsWith("crop-", StringComparison.Ordinal);
            int stage = planted ? CropStage(above) : 0;
            return new FarmlandReading(
                nutrients.Length > 0 ? nutrients[0] : 0,
                nutrients.Length > 1 ? nutrients[1] : 0,
                nutrients.Length > 2 ? nutrients[2] : 0,
                moisture,
                planted ? above.Code.ToString() : null,
                stage,
                nextStage);
        });
    }

    /// <summary>
    /// Plants a seed on farmland the way a player does: the player holds the seed and uses it on the farmland.
    /// The seed is given to the player first, so the player must have joined.
    /// </summary>
    /// <param name="player">The joined player who plants.</param>
    /// <param name="pos">The farmland block position.</param>
    /// <param name="seedCode">The seed item code, such as <c>game:seeds-carrot</c>.</param>
    /// <returns>True when the farmland now has a crop.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="pos"/> is null.</exception>
    public async Task<bool> PlantAsync(ServerTestPlayer player, BlockPos pos, string seedCode)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedCode);
        player.GiveItem(seedCode);
        await player.Bot.UseHeldItemOnBlockAsync(pos, BlockFacing.UP);
        return Farmland(pos).Crop is not null;
    }

    /// <summary>
    /// Plays game time forward until the crop at a position has grown the given number of stages. Each step
    /// advances the calendar one hour, then runs the server's tick listeners on the field for a simulated
    /// interval, so the field's own growth update runs at its normal rate. Growth rates, climate and
    /// moisture are the game's.
    /// </summary>
    /// <param name="pos">The farmland block position.</param>
    /// <param name="stages">How many stages to grow.</param>
    /// <param name="maxHours">The most game hours to play before giving up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The crop stage after growing, and the game hours played.</returns>
    /// <exception cref="InvalidOperationException">There is no crop at the position.</exception>
    public async Task<(int Stage, double HoursPlayed)> GrowAsync(BlockPos pos, int stages, double maxHours = 2000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stages);
        FarmlandReading start = Farmland(pos);
        if (start.Crop is null) throw new InvalidOperationException($"There is no crop at {pos} to grow.");
        int target = start.CropStage + stages;
        double played = 0;
        while (Farmland(pos).CropStage < target && played < maxHours)
        {
            ct.ThrowIfCancellationRequested();
            _host.Calendar.Advance(TimeSpan.FromHours(1));
            // A span of two listener periods guarantees the field's update fires once, whatever its phase.
            await _host.TickListeners.FastForwardAsync(pos, TimeSpan.FromMilliseconds(9000), ct: ct);
            played += 1;
        }
        return (Farmland(pos).CropStage, played);
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    /// <summary>The stage is the number at the end of the crop's code, such as 3 for <c>game:crop-carrot-3</c>.</summary>
    private static int CropStage(Block crop)
    {
        string path = crop.Code.Path;
        int dash = path.LastIndexOf('-');
        return dash >= 0 && int.TryParse(path.AsSpan(dash + 1), out int stage) ? stage : 0;
    }

    private static object? Read(object target, string name)
    {
        PropertyInfo? property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(target);
    }
}
