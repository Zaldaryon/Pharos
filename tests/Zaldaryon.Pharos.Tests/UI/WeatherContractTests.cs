using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

public sealed class WeatherContractTests
{
    [Fact]
    public void RequiredFields_ReadActualGameFields_AndRejectMissingWrongTypeOrUninitializedMembers()
    {
        var data = new WeatherDataSnapshot { BlendedPrecType = EnumPrecipitationType.Hail, snowThresholdTemp = -4 };
        Assert.Equal(EnumPrecipitationType.Hail, ClientWeatherDriver.RequiredField<EnumPrecipitationType>(data, nameof(data.BlendedPrecType)));
        Assert.Equal(-4f, ClientWeatherDriver.RequiredField<float>(data, nameof(data.snowThresholdTemp)));
        Assert.Equal(0f, ClientWeatherDriver.RequiredField<float>(new ClimateCondition(), nameof(ClimateCondition.Rainfall)));

        var absent = Assert.Throws<NotSupportedException>(() => ClientWeatherDriver.RequiredField<float>(data, "MissingWeatherMember"));
        Assert.Contains("MissingWeatherMember", absent.Message);
        Assert.Contains("compatibility", absent.Message);
        Assert.Throws<NotSupportedException>(() => ClientWeatherDriver.RequiredField<int>(data, nameof(data.snowThresholdTemp)));
        var uninitialized = Assert.Throws<InvalidOperationException>(() =>
            ClientWeatherDriver.RequiredField<ClimateCondition>(new WeatherSystemClient(), "clientClimateCond"));
        Assert.Contains("clientClimateCond", uninitialized.Message);
    }
}

[Collection("Sequential")]
public sealed class FixtureWeatherContractTests : ClientScenarioBase
{
    protected override Isolation Isolation => Isolation.Restart;

    [ClientScenario]
    public void Snapshot_RejectsFixtureMode()
    {
        Assert.False(Client.IsEngineMode);
        Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
    }

    [ClientScenario]
    public void Snapshot_RejectsDisposedClient()
    {
        var weather = Client.Weather;
        Client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => weather.Snapshot());
    }
}

[Collection("Sequential")]
public sealed class UnjoinedWeatherContractTests : ClientScenarioBase
{
    protected override Isolation Isolation => Isolation.Restart;
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientScenario]
    public void Snapshot_RejectsUnjoinedEngine()
    {
        Assert.False(Client.IsJoined);
        Assert.Throws<InvalidOperationException>(() => Client.Weather.Snapshot());
    }
}
