using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public abstract class FarmingCaveatsHelpers : ServerScenarioBase
{
    private const string Field = "game:farmland-moist-medium";
    private const string StillWater = "game:water-still-7";

    /// <summary>
    /// Builds a field above sea level, on a stone platform, under a roof, lit only by a torch beside it, with water
    /// beside it. Above sea level there is no depth penalty, so the growth light is the sunlight under the roof, or the
    /// block light of the torch when the world allows underground farming.
    /// </summary>
    protected BlockPos PrepareUndergroundField(ServerTestPlayer player)
    {
        int seaLevel = Host!.RunOnGameThread(() => ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World.SeaLevel);
        BlockPos pos = new((int)Math.Floor(player.Entity!.Pos.X) + 2, seaLevel + 1, (int)Math.Floor(player.Entity.Pos.Z));
        Host.Climate.Override(new Cuboidi(pos.X - 8, pos.Y - 2, pos.Z - 8, pos.X + 8, pos.Y + 4, pos.Z + 8), temperature: 20);
        Host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = Api!.World.BlockAccessor;
            Block rock = Api.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            // The platform the field stands on, and the roof over it, fifteen blocks each way.
            for (int dx = -7; dx <= 7; dx++)
            {
                for (int dz = -7; dz <= 7; dz++)
                {
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X + dx, pos.Y - 1, pos.Z + dz));
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X + dx, pos.Y + 2, pos.Z + dz));
                }
            }
            // Walls on all four sides close the field off from the sky, so only the torch's block light reaches it.
            for (int d = -4; d <= 4; d++)
            {
                for (int h = 0; h <= 1; h++)
                {
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X + d, pos.Y + h, pos.Z - 4));
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X + d, pos.Y + h, pos.Z + 4));
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X - 4, pos.Y + h, pos.Z + d));
                    blocks.SetBlock(rock.BlockId, new BlockPos(pos.X + 4, pos.Y + h, pos.Z + d));
                }
            }
            blocks.SetBlock(Api.World.GetBlock(new AssetLocation("game:torch-basic-lit-up"))!.BlockId, new BlockPos(pos.X + 1, pos.Y + 1, pos.Z));
            blocks.SetBlock(Api.World.GetBlock(new AssetLocation(StillWater))!.BlockId, new BlockPos(pos.X - 1, pos.Y, pos.Z));
        });
        Host.Farming.PlaceField(pos, Field);
        Host.Ticks(60);
        return pos;
    }

    protected int SunAt(BlockPos pos) => Host!.RunOnGameThread(() =>
        ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World.BlockAccessor.GetLightLevel(pos.UpCopy(), EnumLightLevelType.OnlySunLight));

    protected BlockPos PrepareField(ServerTestPlayer player, int dx, int dz)
    {
        Host!.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);
        Entity entity = player.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + dx, (int)Math.Floor(entity.Pos.Y) - 1, (int)Math.Floor(entity.Pos.Z) + dz);
        Host.Farming.PlaceField(pos, Field);
        return pos;
    }

    protected static Cuboidi AroundField(BlockPos pos) =>
        new(pos.X - 2, pos.Y - 2, pos.Z - 2, pos.X + 2, pos.Y + 2, pos.Z + 2);

}

public sealed class FarmingCaveatsTests : FarmingCaveatsHelpers
{
    [ServerScenario]
    public async Task Fertilizer_RaisesTheNitrogenAFieldCanUseOverTwoDays()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FertCompost");
        BlockPos plain = PrepareField(player, 2, 0);
        BlockPos fertilized = PrepareField(player, 2, 4);
        double before = Host!.Farming.Farmland(fertilized).Nitrogen;

        Assert.True(Host.Farming.Fertilize(player, fertilized, "game:compost"), "The field refused the compost.");
        await Host.Farming.PlayAsync([plain, fertilized], TimeSpan.FromDays(2));

        FarmlandReading fed = Host.Farming.Farmland(fertilized);
        FarmlandReading unfed = Host.Farming.Farmland(plain);
        Assert.True(fed.Nitrogen > unfed.Nitrogen, $"The compost field has {fed.Nitrogen} nitrogen, the plain field {unfed.Nitrogen}.");
        Assert.True(fed.Nitrogen > before, "The compost did not add any nitrogen.");
    }

    [ServerScenario]
    public async Task Rain_WetsAnOpenFieldMoreThanADryOneOverTwoDays()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("RainWet");
        BlockPos rainy = PrepareField(player, 2, 0);
        BlockPos dry = PrepareField(player, 2, 4);
        using ClimateOverride wet = Host!.Climate.Override(AroundField(rainy), rainfall: 1f);
        using ClimateOverride arid = Host.Climate.Override(AroundField(dry), rainfall: 0f);

        await Host.Farming.PlayAsync([rainy, dry], TimeSpan.FromDays(2));

        float rainMoisture = Host.Farming.Farmland(rainy).Moisture;
        float dryMoisture = Host.Farming.Farmland(dry).Moisture;
        Assert.True(rainMoisture > dryMoisture, $"The rainy field is at {rainMoisture} moisture, the dry field at {dryMoisture}.");
    }

    [ServerScenario]
    public async Task Shade_ARoofLowersTheSunLightBelowTheGrowthPenalty()
    {
        // The stored sunlight does not change with the hour in this game version: the day and night levels only
        // shade the picture. Sunlight below the penalty threshold therefore comes from shade, such as a roof.
        ServerTestPlayer player = await CreateTestPlayerAsync("ShadeLight");
        BlockPos open = PrepareField(player, 2, 0);
        BlockPos roofed = PrepareField(player, 2, 4);
        Host!.RunOnGameThread(() =>
        {
            // A small roof lets light in from the sides, so the roof spans nine blocks each way.
            Block rock = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    Api.World.BlockAccessor.SetBlock(rock.BlockId, roofed.AddCopy(dx, 2, dz));
                }
            }
        });
        Host.Ticks(40);

        int sunOpen = SunAt(open);
        int sunRoofed = SunAt(roofed);

        Assert.True(sunOpen >= 19, $"The open field has {sunOpen} sun light, below the growth penalty threshold of 19.");
        Assert.True(sunRoofed < 19, $"The roofed field has {sunRoofed} sun light, which does not slow growth.");
    }

    [ServerScenario]
    public async Task BellPepper_IsNotPlantedAsASeed()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("PepperNoPlant");
        BlockPos pos = PrepareField(player, 2, 0);

        bool planted = await Host!.Farming.PlantAsync(player, pos, "game:seeds-bellpepper");

        Assert.False(planted, "The bell pepper seed was planted on the field.");
        Assert.Null(Host.Farming.Farmland(pos).Crop);
    }
}

/// <summary>Underground farming with the game's default setting, which is off: a field lit only by a torch does not grow.</summary>
public sealed class FarmingUndergroundDeniedTests : FarmingCaveatsHelpers
{
    [ServerScenario]
    public async Task Underground_FieldLitOnlyByATorchDoesNotGrowWhenUndergroundFarmingIsOff()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("UndergroundOff");
        BlockPos pos = PrepareUndergroundField(player);
        Assert.True(await Host!.Farming.PlantAsync(player, pos, "game:seeds-carrot"));
        // Sealed from the sky, the field has no sunlight, only the torch's block light.
        Assert.Equal(0, SunAt(pos));

        await Host.Farming.PlayAsync([pos], TimeSpan.FromHours(300));
        Assert.Equal(1, Host.Farming.Farmland(pos).CropStage);
    }
}

/// <summary>Underground farming turned on in the world configuration: the same field grows on torch light.</summary>
[ServerWorld(WorldConfigurationJson = "{\"allowUndergroundFarming\": true}")]
public sealed class FarmingUndergroundAllowedTests : FarmingCaveatsHelpers
{
    [ServerScenario]
    public async Task Underground_FieldLitOnlyByATorchGrowsWhenUndergroundFarmingIsOn()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("UndergroundOn");
        BlockPos pos = PrepareUndergroundField(player);
        Assert.True(await Host!.Farming.PlantAsync(player, pos, "game:seeds-carrot"));

        await Host.Farming.PlayAsync([pos], TimeSpan.FromHours(300));

        Assert.True(Host.Farming.Farmland(pos).CropStage >= 2, $"The field stayed at stage {Host.Farming.Farmland(pos).CropStage}.");
    }
}
