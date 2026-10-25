using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class SkyWeatherTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 900,
    };

    [ClientServerScenario]
    public async Task Ambient_ReadsBlendedValuesAndModifiers()
    {
        await Session!.StepFramesAsync(30);
        AmbientSnapshot a = Client!.Ambient.Snapshot();
        Assert.True(a.Modifiers.Count > 0);
        Assert.Contains("night", a.Modifiers.Keys);
        Assert.True(a.FogDensity >= 0f);
        Assert.True(a.SceneBrightness >= 0f);
    }

    [ClientServerScenario]
    public async Task Weather_ReadsPrecipitationWindAndParticles()
    {
        await Session!.StepFramesAsync(30);
        WeatherSnapshot w = Client!.Weather.Snapshot();
        Assert.NotNull(w.WindSpeed);
        Assert.Equal(3, w.WindSpeed.Length);
    }

    [ClientServerScenario]
    public async Task Sky_ReadsSunMoonAndColors()
    {
        await Session!.StepFramesAsync(30);
        SkySnapshot s = Client!.Sky.Snapshot();
        Assert.NotNull(s.Sun);
        Assert.NotNull(s.Moon);
        Assert.True(s.ZenithColor.Length >= 3);
        Assert.True(s.DayLightStrength >= 0f);
    }
}