using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Input;

[Collection("Sequential")]
public class EngineInputTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task HotkeyPress_OpensTheBoundDialog()
    {
        await StepUntilAsync(() => false, maxFrames: 10);

        Client!.Input.PressKey(GlKeys.C);
        bool opened = await StepUntilAsync(() => OpenDialogs().Contains("GuiDialogCharacter"), maxFrames: 120);

        Assert.True(opened, $"Character dialog did not open. Open dialogs: {string.Join(", ", OpenDialogs())}");
    }

    [ClientServerScenario]
    public async Task HoldingW_WalksThePlayerForwardOnBothSides()
    {
        // Let the player settle on the ground first.
        await StepUntilAsync(() => false, maxFrames: 60);
        Vec3d start = Client!.Client.EntityPlayer.Pos.XYZ.Clone();

        Client.Input.InjectKey(VirtualKey.W, pressed: true);
        await StepUntilAsync(() => false, maxFrames: 90);
        Client.Input.InjectKey(VirtualKey.W, pressed: false);
        await StepUntilAsync(() => false, maxFrames: 30);

        double clientMoved = Client.Client.EntityPlayer.Pos.XYZ.HorizontalSquareDistanceTo(start);
        Assert.True(clientMoved > 1, $"Player moved only {Math.Sqrt(clientMoved):F2} blocks on the client");

        // The client reports its movement to the server.
        var serverEntity = Server!.GetClientByPlayername(PlayerName).Entityplayer;
        double serverMoved = serverEntity.Pos.XYZ.HorizontalSquareDistanceTo(start);
        Assert.True(serverMoved > 1, $"Player moved only {Math.Sqrt(serverMoved):F2} blocks on the server");
    }

    [ClientServerScenario]
    public async Task MouseDelta_TurnsTheCamera()
    {
        await StepUntilAsync(() => Client!.Client.MouseGrabbed, maxFrames: 120);
        float yawBefore = Client!.Client.EntityPlayer.Pos.Yaw;

        for (int i = 0; i < 10; i++)
        {
            Client.Input.InjectMouseDelta(40, 0);
            await StepUntilAsync(() => false, maxFrames: 1);
        }

        await StepUntilAsync(() => false, maxFrames: 10);
        float yawAfter = Client.Client.EntityPlayer.Pos.Yaw;

        Assert.True(Client.Client.MouseGrabbed, "The mouse was never grabbed, so mouse look is off");
        Assert.NotEqual(yawBefore, yawAfter, 3);
    }

    [ClientServerScenario]
    public async Task ChatTypedOnTheClient_ReachesTheServer()
    {
        List<string> chat = [];
        ServerHost!.RunOnGameThread(() => ((ICoreServerAPI)Server!.Api).Event.PlayerChat +=
            (IServerPlayer byPlayer, int channelId, ref string message, ref string data, Vintagestory.API.Datastructures.BoolRef consumed) => chat.Add(message));
        await StepUntilAsync(() => false, maxFrames: 10);

        Client!.Input.PressKey(GlKeys.T);
        await StepUntilAsync(() => false, maxFrames: 10);
        Client.Input.TypeText("typed through the client");
        await StepUntilAsync(() => false, maxFrames: 5);
        string typed = Client.RunOnClientThread(() =>
        {
            var hud = Client.Client.api.Gui.LoadedGuis.First(g => g.GetType().Name == "HudDialogChat");
            return hud.Composers["chat"]?.GetChatInput("chatinput")?.GetText() ?? "<no chat input>";
        });
        Assert.Contains("typed through the client", typed);
        Client.Input.PressKey(GlKeys.Enter);

        bool received = await StepUntilAsync(() => chat.Any(m => m.Contains("typed through the client")), maxFrames: 300);
        Assert.True(received, "The chat line typed on the client never reached the server");
    }

    [ClientServerScenario]
    public async Task ScrollWheel_ChangesTheActiveHotbarSlot()
    {
        await StepUntilAsync(() => false, maxFrames: 20);
        int before = Client!.Client.player.InventoryManager.ActiveHotbarSlotNumber;

        Client.Input.InjectScroll(-1);
        bool changed = await StepUntilAsync(() => Client.Client.player.InventoryManager.ActiveHotbarSlotNumber != before, maxFrames: 30);

        Assert.True(changed, "The active hotbar slot did not change");
    }

    private List<string> OpenDialogs() =>
        Client!.RunOnClientThread(() => Client.Client.api.Gui.OpenedGuis.Select(g => g.GetType().Name).ToList());
}
