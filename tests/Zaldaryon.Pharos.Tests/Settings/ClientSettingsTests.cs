using Vintagestory.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Settings;

public class ClientSettingsProfileTests
{
    [Fact]
    public void Presets_MirrorTheGamesGraphicsPresets()
    {
        ClientSettingsProfile high = ClientSettingsProfile.Preset("high");

        Assert.Equal(GraphicsPreset.High.ViewDistance, high.Values["viewDistance"]);
        Assert.Equal(GraphicsPreset.High.SSAO, high.Values["ssaoquality"]);
        Assert.Contains("minimum", ClientSettingsProfile.Presets.Keys);
        Assert.DoesNotContain("custom", ClientSettingsProfile.Presets.Keys);
    }

    [Fact]
    public void Preset_RejectsUnknownNames()
    {
        Assert.Throws<ArgumentException>(() => ClientSettingsProfile.Preset("potato"));
    }

    [Fact]
    public void With_OverridesSingleValues()
    {
        ClientSettingsProfile profile = ClientSettingsProfile.Preset("low").With(("viewDistance", 48));

        Assert.Equal(48, profile.Values["viewdistance"]);
        Assert.Equal("low", profile.Name);
    }

    [Fact]
    public void MatrixAttribute_YieldsOneProfilePerPreset()
    {
        var rows = new ClientSettingsMatrixAttribute("minimum", "high").GetData(null!).ToList();

        Assert.Equal(["minimum", "high"], rows.Select(r => ((ClientSettingsProfile)r[0]).Name));
    }
}

[Collection("Sequential")]
[ClientSetting("viewDistance", 96)]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveClientSettingsTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public void ClassSetting_IsAppliedOnceTheClientHasJoined()
    {
        Assert.Equal(96, Client!.Settings.Get("viewDistance"));
    }

    [ClientServerScenario]
    public async Task Apply_ChangesLiveAndRestoresOnDispose()
    {
        object? before = Client!.Settings.Get("particleLevel");

        using (Client.Settings.Apply(ClientSettingsProfile.Of("test", ("particleLevel", 7))))
        {
            Assert.Equal(7, Client.Settings.Get("particleLevel"));
            await Session!.StepFramesAsync(2);
        }

        Assert.Equal(before, Client.Settings.Get("particleLevel"));
    }

    [ClientServerScenario]
    public void Set_RejectsValuesOfTheWrongType()
    {
        Assert.Throws<ArgumentException>(() => Client!.Settings.Set("viewDistance", "far"));
    }

    [Theory]
    [ClientSettingsMatrix("minimum", "high")]
    public async Task EachPreset_RendersAfterTheShadersReload(ClientSettingsProfile profile)
    {
        using IDisposable _ = Client!.Settings.Apply(profile);
        await Session!.StepFramesAsync(10);

        Assert.Equal(profile.Values["ssaoquality"], Client.Settings.Get("ssaoquality"));
        Assert.NotNull(Client.CaptureFrame());
        Assert.True(Client.IsJoined);
    }
}
