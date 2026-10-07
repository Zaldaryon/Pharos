using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Audio;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Audio;

[Collection("Sequential")]
public class SoundRecorderTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task SoundPlayedByTheServer_IsPlayedByTheClient()
    {
        await Session!.StepFramesAsync(30);
        Client!.Sounds.Clear();
        Vec3d at = Client.Client.EntityPlayer.Pos.XYZ;

        ServerHost!.RunOnGameThread(() =>
            ((ICoreServerAPI)Server!.Api).World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), at.X, at.Y, at.Z, null, false, 32f, 1f));

        bool played = await StepUntilAsync(() => Client.Sounds.WasPlayed("block/planks"), maxFrames: 120);

        Assert.True(played, $"The client never played the sound. Started: {string.Join(", ", Client.Sounds.Started.Select(s => s.Location))}");
        RecordedSound sound = Client.Sounds.Played("block/planks")[0];
        Assert.NotNull(sound.Position);
        Assert.True(Math.Abs(sound.Position!.X - at.X) < 1 && Math.Abs(sound.Position.Z - at.Z) < 1);
    }

    [ClientServerScenario]
    public async Task Walking_PlaysFootsteps()
    {
        await Session!.StepFramesAsync(30);
        Client!.Sounds.Clear();

        await Session.HoldAsync(PlayerAction.Forward, 120);

        Assert.True(Client.Sounds.WasPlayed("walk"),
            $"Walking played no footstep. Started: {string.Join(", ", Client.Sounds.Started.Select(s => s.Location).Distinct())}");
    }
}
