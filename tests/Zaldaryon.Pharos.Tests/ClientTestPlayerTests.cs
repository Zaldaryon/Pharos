using System;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class ClientTestPlayerTests
{
    static ClientTestPlayerTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void TestPlayer_WhenClientUnconnected_ExposesDefaultStateGracefully()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        IClientTestPlayer player = client.TestPlayer;
        Assert.NotNull(player);
        Assert.False(player.IsAvailable);
        Assert.Null(player.RawPlayer);
        Assert.Null(player.RawEntity);

        // Position and motion should fall back without throwing
        Vec3d pos = player.Position;
        Assert.NotNull(pos);
        Vec3d motion = player.Motion;
        Assert.NotNull(motion);
        Vec3d eyePos = player.EyePosition;
        Assert.NotNull(eyePos);

        // Teleport and motion setting should execute gracefully
        player.Teleport(100.0, 50.0, -100.0);
        Assert.Equal(100.0, player.Position.X);
        Assert.Equal(50.0, player.Position.Y);
        Assert.Equal(-100.0, player.Position.Z);

        player.Teleport(new Vec3d(200.0, 60.0, -200.0));
        Assert.Equal(200.0, player.Position.X);
        Assert.Equal(60.0, player.Position.Y);
        Assert.Equal(-200.0, player.Position.Z);

        player.Motion = new Vec3d(0.5, 0.0, -0.5);
    }

    [Fact]
    public void PlayerCameraController_ControlsAnglesAndFrustum()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);
        IPlayerCameraController camera = client.TestPlayer.Camera;
        Assert.NotNull(camera);

        // Orientation manipulation
        camera.SetOrientation(90.0, -15.0);
        Assert.Equal(90.0, camera.Yaw);
        Assert.Equal(-15.0, camera.Pitch);

        camera.Yaw = 180.0;
        camera.Pitch = 45.0;
        Assert.Equal(180.0, camera.Yaw);
        Assert.Equal(45.0, camera.Pitch);

        // LookAt manipulation
        camera.Position = new Vec3d(0, 0, 0);
        camera.LookAt(new Vec3d(10, 0, 0));
        Assert.Equal(-Math.PI / 2.0, camera.Yaw, precision: 4);
        Assert.Equal(0.0, camera.Pitch, precision: 4);

        camera.LookAt(0.0, 10.0, 0.0);
        Assert.Equal(Math.PI / 2.0, camera.Pitch, precision: 4);

        camera.LookAt(0.0, 0.0, -10.0);
        Assert.Equal(0.0, camera.Yaw, precision: 4);
        Assert.Equal(0.0, camera.Pitch, precision: 4);

        // Matrices
        Assert.NotNull(camera.ViewMatrix);
        Assert.NotNull(camera.ProjectionMatrix);
        Assert.NotNull(camera.Frustum);

        // Frustum spatial testing: camera at (0, 0, 0) looking along -Z (Yaw = 0, Pitch = 0)
        // Point in front (0, 0, -5) is inside the frustum
        Assert.True(camera.IsInFrustum(new Vec3d(0, 0, -5), 0.5));
        Assert.True(camera.IsInFrustum(0.0, 0.0, -5.0, 0.5));
        Assert.True(camera.IsInFrustum(new BlockPos(0, 0, -5)));

        // Point directly behind (0, 0, 10) is outside the frustum
        Assert.False(camera.IsInFrustum(new Vec3d(0, 0, 10), 0.5));
        Assert.False(camera.IsInFrustum(0.0, 0.0, 10.0, 0.5));
    }

    [Fact]
    public void PlayerInventoryAccessor_SlotSelectionAndQueries()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);
        IPlayerInventoryAccessor inventory = client.TestPlayer.Inventory;
        Assert.NotNull(inventory);

        // Active slot selection
        bool selected = inventory.SelectHotbarSlot(3);
        Assert.True(selected);
        Assert.Equal(3, inventory.ActiveHotbarSlotIndex);

        // Out-of-bounds selection
        Assert.False(inventory.SelectHotbarSlot(-1));
        Assert.False(inventory.SelectHotbarSlot(10));
        Assert.Equal(3, inventory.ActiveHotbarSlotIndex);

        // Slot queries when player entity is not spawned
        Assert.Null(inventory.Hotbar);
        Assert.Null(inventory.Backpack);
        Assert.Null(inventory.Crafting);
        Assert.Null(inventory.ActiveHotbarSlot);
        Assert.Null(inventory.ActiveHotbarItem);
        Assert.Null(inventory.GetSlot("hotbar", 0));

        inventory.ClearHotbar();
        inventory.ClearAll();
    }

    [Fact]
    public void PlayerGuiController_QueriesAndControlsDialogsGracefully()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);
        IPlayerGuiController gui = client.TestPlayer.Gui;
        Assert.NotNull(gui);

        Assert.False(gui.IsInventoryOpen);
        Assert.NotNull(gui.LoadedDialogs);
        Assert.NotNull(gui.OpenedDialogs);

        // Open/close inventory gracefully
        gui.OpenInventory();
        gui.CloseInventory();

        // Dialog queries and closes
        bool isCharOpen = gui.IsDialogOpen<GuiDialogCharacter>();
        Assert.False(isCharOpen);

        gui.CloseDialog<GuiDialogCharacter>();
        gui.CloseAllDialogs();
    }

    [Fact]
    public void ClientServerLoopbackSession_ExposesPlayerShortcut()
    {
        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using AtlasServerHost server = AtlasServerHost.Boot();
        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "PlayerPilot");

        Assert.NotNull(session.Player);
        Assert.Same(client.TestPlayer, session.Player);

        session.Player.Camera.SetOrientation(45.0, 10.0);
        Assert.Equal(45.0, session.Player.Camera.Yaw);
        Assert.Equal(10.0, session.Player.Camera.Pitch);

        session.StepFrames(5);
        Assert.Same(client.TestPlayer, session.Player);
    }
}
