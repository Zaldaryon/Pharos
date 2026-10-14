using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class ClimateDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task At_ReadsTheClimateTheGameUsesAtThePosition()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("ClimateRead");
        BlockPos pos = PositionOf(player);

        ClimateReading reading = Host!.Climate.At(pos);

        Assert.InRange(reading.Temperature, -60, 80);
        Assert.InRange(reading.Rainfall, 0, 1);
        Assert.InRange(reading.Fertility, 0, 1);
    }

    [ServerScenario]
    public async Task Override_ChangesTemperatureInsideItsRegionOnly()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("ClimateTemp");
        BlockPos inside = PositionOf(player);
        BlockPos outside = inside.AddCopy(100, 0, 100);
        float before = Host!.Climate.At(outside).Temperature;

        using ClimateOverride cold = Host.Climate.Override(Region(inside), temperature: -5);

        Assert.Equal(-5, Host.Climate.At(inside).Temperature, precision: 3);
        Assert.Equal(before, Host.Climate.At(outside).Temperature, precision: 3);
    }

    [ServerScenario]
    public async Task Override_EndsWhenItIsDisposed()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("ClimateEnd");
        BlockPos pos = PositionOf(player);
        ClimateReading before = Host!.Climate.At(pos);

        using (Host.Climate.Override(Region(pos), temperature: 40, rainfall: 0.9f))
        {
            ClimateReading during = Host.Climate.At(pos);
            Assert.Equal(40, during.Temperature, precision: 3);
            Assert.Equal(0.9f, during.Rainfall, precision: 3);
        }

        Assert.Equal(before.Temperature, Host.Climate.At(pos).Temperature, precision: 3);
        Assert.Equal(before.Rainfall, Host.Climate.At(pos).Rainfall, precision: 3);
    }

    [ServerScenario]
    public async Task Baseline_EndsOverridesALiveTestLeftBehind()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("ClimateBase");
        BlockPos pos = PositionOf(player);
        float before = Host!.Climate.At(pos).Temperature;
        ClimateDriver.Baseline baseline = Host.Climate.Capture();
        Host.Climate.Override(Region(pos), temperature: 55);

        baseline.Restore();

        Assert.Equal(before, Host.Climate.At(pos).Temperature, precision: 3);
    }

    private static BlockPos PositionOf(ServerTestPlayer player)
    {
        Entity entity = player.Entity!;
        return new BlockPos((int)Math.Floor(entity.Pos.X), (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z));
    }

    private static Cuboidi Region(BlockPos pos) =>
        new(pos.X - 4, pos.Y - 2, pos.Z - 4, pos.X + 4, pos.Y + 4, pos.Z + 4);
}
