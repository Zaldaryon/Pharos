using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The crop stage time is randomised by ±10% with the game's own static random source, so a fixed world seed fixes it.</summary>
// The crop's random draw is static, shared by every host in the process, so this class runs alone.
[Collection("Sequential")]
[ServerWorld(seed: 4242)]
public sealed class FarmingSeededTests : ServerScenarioBase
{
    private const string Field = "game:farmland-moist-medium";

    [ServerScenario]
    public async Task FirstStageTime_IsTheSameForTheSameSeed()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("SeededStage");
        Host!.Random.Seed(4242);
        Host.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);
        Entity entity = player.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + 2, (int)Math.Floor(entity.Pos.Y) - 1, (int)Math.Floor(entity.Pos.Z));
        Host.Farming.PlaceField(pos, Field);
        Host.RunOnGameThread(() => ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World.BlockAccessor.SetBlock(
            ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World.GetBlock(new AssetLocation("game:water-still-7"))!.BlockId, pos.AddCopy(1, 0, 0)));
        Assert.True(await Host.Farming.PlantAsync(player, pos, "game:seeds-carrot"));

        double hoursToStage = Host.Farming.Farmland(pos).NextStageAtHours - Host.RunOnGameThread(() => Host.Calendar.TotalHours);

        // The crop's stage draw is part of the seeded statics, so the same seed gives the same time.
        Assert.Contains(Host.Random.Seeded, r => r.Member.Contains("BlockEntityFastForwardGrowth.rand", StringComparison.Ordinal));
        Assert.InRange(hoursToStage, GoldenHours - 0.001, GoldenHours + 0.001);
    }

    // Recorded with Host.Random.Seed(4242) on this game build; the same seed gives the same time on every run.
    private const double GoldenHours = 190.2708740234375;
}
