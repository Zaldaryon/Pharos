using Vintagestory.API.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Bridge;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Bridge;

/// <summary>
/// The bridge mod loaded into a real client: it is staged on its own because this project
/// references the bridge.
/// </summary>
[Collection("Bridge")]
public class BridgeModTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    private BridgeChannel Channel => BridgeChannel.Active ?? throw new InvalidOperationException("The bridge mod is not loaded");

    [ClientServerScenario]
    public void BridgeMod_IsLoadedByTheClient()
    {
        bool loaded = Client!.RunOnClientThread(() => Client.Client.Api.ModLoader.IsModEnabled("pharosbridge"));

        Assert.True(loaded);
        Assert.NotNull(BridgeChannel.Active);
    }

    [ClientServerScenario]
    public async Task EveryFrame_IsPublishedFromStartToEnd()
    {
        Channel.DrainAll();

        await Session!.StepFramesAsync(5);

        List<BridgeEventKind> frames = Channel.DrainAll()
            .Select(e => e.Kind)
            .Where(k => k is BridgeEventKind.FrameStart or BridgeEventKind.FrameEnd)
            .ToList();
        Assert.True(frames.Count(k => k == BridgeEventKind.FrameStart) >= 5, $"Frames: {string.Join(", ", frames)}");
        Assert.Equal(frames.Count(k => k == BridgeEventKind.FrameStart), frames.Count(k => k == BridgeEventKind.FrameEnd));
        Assert.Equal(BridgeEventKind.FrameStart, frames[0]);
        Assert.Equal(BridgeEventKind.FrameEnd, frames[^1]);
    }

    [ClientServerScenario]
    public async Task ChunksAroundThePlayer_AreReportedAsTessellated()
    {
        await Session!.StepFramesAsync(30);
        var pos = Client!.Client.EntityPlayer.Pos;
        int chunkX = (int)pos.X / 32;
        int chunkZ = (int)pos.Z / 32;

        List<BridgeEvent> chunks = Channel.DrainAll().Where(e => e.Kind == BridgeEventKind.ChunkTessellated).ToList();

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => Math.Abs(c.ChunkX - chunkX) <= 1 && Math.Abs(c.ChunkZ - chunkZ) <= 1);
    }

    [ClientServerScenario]
    public async Task OpeningAndClosingADialog_IsPublished()
    {
        await Session!.StepFramesAsync(10);
        Channel.DrainAll();

        Client!.Input.PressKey(GlKeys.Escape);
        await StepUntilAsync(() => Client.Ui!.IsOpen("GuiDialogEscapeMenu"), maxFrames: 60);
        await Session.StepFramesAsync(2);
        Client.Input.PressKey(GlKeys.Escape);
        await StepUntilAsync(() => !Client.Ui!.IsOpen("GuiDialogEscapeMenu"), maxFrames: 60);
        await Session.StepFramesAsync(2);

        List<BridgeEvent> gui = Channel.DrainAll()
            .Where(e => e.Kind == BridgeEventKind.GuiStateChanged && e.ScreenName == "GuiDialogEscapeMenu")
            .ToList();
        Assert.Equal([true, false], gui.Select(e => e.IsOpen));
    }
}
