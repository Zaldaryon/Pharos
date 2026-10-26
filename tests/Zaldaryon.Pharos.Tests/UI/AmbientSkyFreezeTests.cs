using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild", Isolation = Isolation.Restart)]
public sealed class AmbientSkyFreezeTests(ITestOutputHelper output) : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario(TimeoutMs = 300_000)]
    public async Task Ambient_SnapshotMatchesActualManagerAndDetachesArrays()
    {
        await Session.StepFramesAsync(60);
        Client.RunOnClientThread(() =>
        {
            var actual = Client.Client.api.Ambient;
            var snapshot = Client.Ambient.Snapshot();
            Assert.Equal(actual.BlendedFogColor.X, snapshot.FogColor[0]);
            Assert.Equal(actual.BlendedFogColor.Y, snapshot.FogColor[1]);
            Assert.Equal(actual.BlendedFogColor.Z, snapshot.FogColor[2]);
            Assert.Equal(actual.BlendedFogColor.W, snapshot.FogColor[3]);
            Assert.Equal(actual.BlendedAmbientColor.X, snapshot.AmbientColor[0]);
            Assert.Equal(actual.BlendedAmbientColor.Y, snapshot.AmbientColor[1]);
            Assert.Equal(actual.BlendedAmbientColor.Z, snapshot.AmbientColor[2]);
            Assert.Equal(actual.BlendedFogDensity, snapshot.FogDensity);
            Assert.Equal(actual.BlendedFogMin, snapshot.FogMin);
            Assert.Equal(actual.BlendedFlatFogDensity, snapshot.FlatFogDensity);
            Assert.Equal(actual.BlendedFlatFogYOffset + Client.Client.World.SeaLevel, snapshot.FlatFogYOffset);
            Assert.Equal(actual.BlendedCloudBrightness, snapshot.CloudBrightness);
            Assert.Equal(actual.BlendedCloudDensity, snapshot.CloudDensity);
            Assert.Equal(actual.BlendedSceneBrightness, snapshot.SceneBrightness);
            Assert.Equal(actual.BlendedFogBrightness, snapshot.FogBrightness);
            Assert.Equal(actual.CurrentModifiers.Count, snapshot.Modifiers.Count);

            var detached = Client.Ambient.Snapshot();
            detached.FogColor[0] = -999;
            detached.AmbientColor[0] = -999;
            Assert.NotEqual(-999f, Client.Ambient.Snapshot().FogColor[0]);
            Assert.NotEqual(-999f, Client.Ambient.Snapshot().AmbientColor[0]);
            output.WriteLine($"Ambient modifiers: {string.Join(", ", snapshot.Modifiers.Keys)}");
        });
    }

    [ClientServerScenario(TimeoutMs = 300_000)]
    public async Task Sky_SnapshotMatchesActualCalendarUnitsAndColors()
    {
        await Session.StepFramesAsync(60);
        Client.RunOnClientThread(() =>
        {
            var calendar = Client.Client.Calendar;
            var snapshot = Client.Sky.Snapshot();
            Assert.Equal(calendar.SunPosition.X, snapshot.Sun.X);
            Assert.Equal(calendar.SunPosition.Y, snapshot.Sun.Y);
            Assert.Equal(calendar.SunPosition.Z, snapshot.Sun.Z);
            Assert.Equal(calendar.SunColor.X, snapshot.Sun.Color[0]);
            Assert.Equal(calendar.SunColor.Y, snapshot.Sun.Color[1]);
            Assert.Equal(calendar.SunColor.Z, snapshot.Sun.Color[2]);
            Assert.Equal(calendar.MoonPosition.X, snapshot.Moon.X);
            Assert.Equal(calendar.MoonPosition.Y, snapshot.Moon.Y);
            Assert.Equal(calendar.MoonPosition.Z, snapshot.Moon.Z);
            Assert.Equal(calendar.ReflectColor.X, snapshot.Moon.Color[0]);
            Assert.Equal(calendar.ReflectColor.Y, snapshot.Moon.Color[1]);
            Assert.Equal(calendar.ReflectColor.Z, snapshot.Moon.Color[2]);
            Assert.Equal(calendar.DayLightStrength, snapshot.DayLightStrength);
            Assert.Equal(calendar.MoonLightStrength, snapshot.MoonLightStrength);
            Assert.Equal(calendar.SunsetMod, snapshot.SunsetMod);
            Assert.NotEqual(1f, snapshot.Sun.Color[0] + snapshot.Sun.Color[1] + snapshot.Sun.Color[2]); // not hardcoded white
        });
    }

    [ClientServerScenario(TimeoutMs = 300_000)]
    public async Task FreezeSky_FreezeCalendarStillsWindAndWaitsForSyncAndBlend()
    {
        await Session.FreezeSkyAsync(240);
        Client.RunOnClientThread(() =>
        {
            Assert.Equal(0d, Client.Client.Calendar.SpeedOfTime * Client.Client.Calendar.CalendarSpeedMul);
        });
        Server.RunOnGameThread(() =>
        {
            Assert.True(Server.Calendar.IsFrozen);
            Assert.True(Server.Weather.ChangesStopped);
            Assert.Equal(0d, Server.Api.World.BlockAccessor.GetWindSpeedAt(new Vec3d(0, 0, 0)).Length());
        });
        await Session.WaitForCalendarSyncAsync(maxFrames: 120);
        Client.RunOnClientThread(() =>
        {
            var a = Client.Ambient.Snapshot();
            var b = Client.Ambient.Snapshot();
            Assert.True(Math.Abs(a.FogDensity - b.FogDensity) < 1e-6f);
        });
    }
}
