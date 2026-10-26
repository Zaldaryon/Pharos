using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using Xunit.Abstractions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild", Isolation = Isolation.Restart)]
public sealed class WeatherReadoutTests(ITestOutputHelper output) : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario(TimeoutMs = 600_000)]
    public async Task Snapshot_FollowsActualWetDryAndWindTransitions_AndDetachesWind()
    {
        Server.Calendar.Freeze();
        Server.Weather.StopChanges();
        Server.Weather.SetWind("storm");
        Server.Weather.SetPrecipitation(1);

        // The particle renderer uses climate rainfall, not the obsolete PrecIntensity field.
        int observations = 0;
        await Session.WaitStepUntilAsync(() => Client.RunOnClientThread(() =>
        {
            ExpectedWeather? actual = ActualWeather();
            if (observations++ % 60 == 0)
                output.WriteLine($"Wet observation {observations}: {actual}, windX={actual?.Wind[0]}");
            return actual is { Intensity: > 0.8f } && actual.Wind[0] > 0.5f;
        }), maxFrames: 600, conditionDescription: "client weather received rain and storm wind");

        WeatherSnapshot wet = ReadAndCompareToGame();
        Assert.True(wet.PrecipitationIntensity > 0.8f, $"Wet weather read {wet.PrecipitationIntensity}");
        Assert.True(wet.WindSpeed[0] > 0.5f, $"Storm wind read {wet.WindSpeed[0]}");

        Server.Weather.SetWind("still");
        Server.Weather.SetPrecipitation(-1);
        await Session.WaitStepUntilAsync(() => Client.RunOnClientThread(() =>
        {
            ExpectedWeather? actual = ActualWeather();
            return actual is { Intensity: < 0.01f } && Math.Abs(actual.Wind[0]) < 0.05f;
        }), maxFrames: 600, conditionDescription: "client weather received dry conditions and still wind");

        WeatherSnapshot dry = ReadAndCompareToGame();
        Assert.InRange(dry.PrecipitationIntensity, 0, 0.01f);
        Assert.InRange(Math.Abs(dry.WindSpeed[0]), 0, 0.05f);
        Assert.True(wet.PrecipitationIntensity - dry.PrecipitationIntensity > 0.79f);
    }

    [ClientServerScenario(TimeoutMs = 600_000)]
    public async Task Snapshot_RejectsUninitializedWeather_WithoutInventingZeros()
    {
        await Session.WaitStepUntilAsync(() => Client.RunOnClientThread(() => ActualWeather() != null),
            maxFrames: 600, conditionDescription: "client weather initialized");
        Client.RunOnClientThread(() =>
        {
            WeatherSystemClient weather = Client.Client.api.ModLoader.GetModSystem<WeatherSystemClient>();
            var climate = weather.clientClimateCond;
            bool finalized = weather.haveLevelFinalize;
            bool chunkLoaded = weather.playerChunkLoaded;
            try
            {
                weather.clientClimateCond = null!;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
                weather.clientClimateCond = climate;
                weather.haveLevelFinalize = false;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
                weather.haveLevelFinalize = finalized;
                weather.playerChunkLoaded = false;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
            }
            finally
            {
                weather.clientClimateCond = climate;
                weather.haveLevelFinalize = finalized;
                weather.playerChunkLoaded = chunkLoaded;
            }
        });
    }

    [ClientServerScenario(TimeoutMs = 600_000)]
    public async Task Snapshot_ResolvesSnowBoundaryAndHail_AppliesAltitude_AndRejectsInvalidInputs()
    {
        await Session.WaitStepUntilAsync(() => Client.RunOnClientThread(() => ActualWeather() != null),
            maxFrames: 600, conditionDescription: "client weather initialized");
        Client.RunOnClientThread(() =>
        {
            WeatherSystemClient weather = Client.Client.api.ModLoader.GetModSystem<WeatherSystemClient>();
            WeatherDataSnapshot data = weather.BlendedWeatherData;
            var climate = weather.clientClimateCond;
            var position = Client.Client.EntityPlayer.Pos;
            float oldRain = climate.Rainfall, oldTemperature = climate.Temperature, oldThreshold = data.snowThresholdTemp;
            EnumPrecipitationType oldType = data.BlendedPrecType;
            double oldY = position.Y;
            try
            {
                climate.Rainfall = 1;
                data.snowThresholdTemp = 0;
                data.BlendedPrecType = EnumPrecipitationType.Auto;
                climate.Temperature = -1;
                Assert.Equal((int)EnumPrecipitationType.Snow, ReadAndCompareToGame().PrecipitationType);
                climate.Temperature = 0;
                Assert.Equal((int)EnumPrecipitationType.Rain, ReadAndCompareToGame().PrecipitationType);
                climate.Temperature = 1;
                Assert.Equal((int)EnumPrecipitationType.Rain, ReadAndCompareToGame().PrecipitationType);
                data.BlendedPrecType = EnumPrecipitationType.Hail;
                Assert.Equal((int)EnumPrecipitationType.Hail, ReadAndCompareToGame().PrecipitationType);

                position.Y = Client.Client.World.SeaLevel + 10000;
                Assert.Equal(0.5f, ReadAndCompareToGame().PrecipitationIntensity);

                climate.Rainfall = float.NaN;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
                climate.Rainfall = 1;
                climate.Temperature = float.NaN;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
                climate.Temperature = 1;
                data.snowThresholdTemp = float.NaN;
                Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
                data.snowThresholdTemp = 0;
                data.BlendedPrecType = (EnumPrecipitationType)int.MaxValue;
                Assert.Throws<NotSupportedException>(() => Client.Weather.Snapshot());
            }
            finally
            {
                climate.Rainfall = oldRain;
                climate.Temperature = oldTemperature;
                data.snowThresholdTemp = oldThreshold;
                data.BlendedPrecType = oldType;
                position.Y = oldY;
            }
        });
    }

    // All observations happen inside a single client-thread callback between stepped frames.
    private ExpectedWeather? ActualWeather()
    {
        WeatherSystemClient weather = Client.Client.api.ModLoader.GetModSystem<WeatherSystemClient>();
        if (!weather.haveLevelFinalize || !weather.playerChunkLoaded || weather.clientClimateCond is not { } climate)
            return null;

        WeatherDataSnapshot data = weather.BlendedWeatherData;
        var player = Client.Client.EntityPlayer.Pos;
        Vec3d wind = Client.Client.World.BlockAccessor.GetWindSpeedAt(player.XYZ);
        float intensity = Math.Max(0, climate.Rainfall - (float)Math.Max(0, (player.Y - Client.Client.World.SeaLevel - 5000) / 10000f));
        EnumPrecipitationType effectiveType = data.BlendedPrecType;
        if (effectiveType == EnumPrecipitationType.Auto)
            effectiveType = climate.Temperature < data.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;

        return new ExpectedWeather(intensity, (int)effectiveType, (int)data.BlendedPrecType,
            [(float)wind.X, (float)wind.Y, (float)wind.Z], climate.Rainfall, player.Y);
    }

    private WeatherSnapshot ReadAndCompareToGame() => Client.RunOnClientThread(() =>
    {
        ExpectedWeather actual = Assert.IsType<ExpectedWeather>(ActualWeather());
        output.WriteLine($"Reader comparison: {actual}, windX={actual.Wind[0]}");
        WeatherSnapshot snapshot = Client.Weather.Snapshot();
        Assert.Equal(actual.Intensity, snapshot.PrecipitationIntensity);
        Assert.Equal(actual.Type, snapshot.PrecipitationType);
        Assert.Equal(actual.BlendedType, snapshot.BlendedPrecipitationType);
        Assert.Equal(actual.Wind, snapshot.WindSpeed);

        WeatherSnapshot another = Client.Weather.Snapshot();
        another.WindSpeed[0] = -999;
        Assert.Equal(actual.Wind[0], ActualWeather()!.Wind[0]);
        Assert.Equal(actual.Wind[0], snapshot.WindSpeed[0]);
        Assert.Equal(actual.Wind[0], Client.Weather.Snapshot().WindSpeed[0]);
        return snapshot;
    });

    private sealed record ExpectedWeather(float Intensity, int Type, int BlendedType, float[] Wind, float RawRainfall, double PlayerY);
}
