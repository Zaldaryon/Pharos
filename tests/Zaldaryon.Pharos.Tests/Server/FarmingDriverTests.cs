using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class FarmingDriverTests : ServerScenarioBase
{
    private const string Field = "game:farmland-moist-medium";
    private const string Seed = "game:seeds-carrot";
    private const string StillWater = "game:water-still-7";

    [ServerScenario]
    public async Task Farmland_ReadsTheSoilBeforeAnythingIsPlanted()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FarmRead");
        BlockPos pos = PrepareField(player, 2, 0);

        FarmlandReading reading = Host!.Farming.Farmland(pos);

        Assert.Null(reading.Crop);
        Assert.Equal(0, reading.CropStage);
        Assert.True(reading.Nitrogen >= 0 && reading.Phosphorus >= 0 && reading.Potassium >= 0, "The soil reports negative nutrients.");
        Assert.InRange(reading.Moisture, 0, 1);
    }

    [ServerScenario]
    public async Task Plant_PutsTheSeedsCropOnTheField()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FarmPlant");
        BlockPos pos = PrepareField(player, 2, 0);

        bool planted = await Host!.Farming.PlantAsync(player, pos, Seed);

        Assert.True(planted, "The field has no crop after the player used the seeds on it.");
        Assert.Equal("game:crop-carrot-1", Host.Farming.Farmland(pos).Crop);
    }

    [ServerScenario]
    public async Task Grow_AdvancesThePlantedCropByStagesAtTheGamesRate()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FarmGrow");
        BlockPos pos = PrepareField(player, 2, 0);
        // The default summer climate reaches 40 °C here, above a carrot's heat limit of 32 °C, and kills it.
        // A mild climate keeps the test about growth; default growth rate and crop death stay on.
        using ClimateOverride mild = Host!.Climate.Override(AroundField(pos), temperature: 20);
        Assert.True(await Host.Farming.PlantAsync(player, pos, Seed));

        (int stage, double hours) = await Host.Farming.GrowAsync(pos, 1);

        Assert.True(stage >= 2, $"The crop stayed at stage {stage} after {hours} game hours.");
        // The first stage of a carrot takes about 124 hours at full rate; the game's rate is lower on a dry, unfed field.
        Assert.InRange(hours, 60, 1000);
    }

    [ServerScenario]
    public async Task ColdClimate_StopsTheCropGrowingWhileAWarmFieldGrows()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FarmCold");
        BlockPos cold = PrepareField(player, 2, 0);
        BlockPos warm = PrepareField(player, 2, 4);
        using ClimateOverride freezing = Host!.Climate.Override(AroundField(cold), temperature: -5);
        using ClimateOverride mild = Host.Climate.Override(AroundField(warm), temperature: 20);
        Assert.True(await Host.Farming.PlantAsync(player, cold, Seed));
        Assert.True(await Host.Farming.PlantAsync(player, warm, Seed));

        (int warmStage, double hours) = await Host.Farming.GrowAsync(warm, 1);
        FarmlandReading coldField = Host.Farming.Farmland(cold);

        Assert.True(warmStage >= 2, $"The warm field stayed at stage {warmStage} after {hours} game hours.");
        Assert.Equal(1, coldField.CropStage);
    }

    private static Cuboidi AroundField(BlockPos pos) =>
        new(pos.X - 2, pos.Y - 2, pos.Z - 2, pos.X + 2, pos.Y + 2, pos.Z + 2);

    /// <summary>
    /// Puts a moist medium farmland block in front of the player and moves the player beside it. The
    /// field is at (x+2, y-1, z+<paramref name="dz"/>), so two fields can share one player.
    /// </summary>
    private BlockPos PrepareField(ServerTestPlayer player, int dx, int dz)
    {
        Host!.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);
        Entity entity = player.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + dx, (int)Math.Floor(entity.Pos.Y) - 1, (int)Math.Floor(entity.Pos.Z) + dz);
        Host.Farming.PlaceField(pos, Field);
        Host.RunOnGameThread(() =>
        {
            // A moist field needs an adjacent still water block, not salt water, a spring or a rapid.
            Block water = Api.World.GetBlock(new AssetLocation(StillWater))!;
            Api.World.BlockAccessor.SetBlock(water.BlockId, pos.AddCopy(1, 0, 0));
        });
        return pos;
    }
}
