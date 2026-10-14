using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using System.Collections.Concurrent;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class HeadlessPlayerBotTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task SetControlAndSelectSlot_UseClientPackets()
    {
        Zaldaryon.Pharos.XUnit.ServerTestPlayer player = await CreateTestPlayerAsync("PacketBot");

        Assert.True((await player.Bot.SetControlAsync(EnumEntityAction.Forward, true)).Accepted);
        Assert.True(Host!.RunOnGameThread(() => player.Entity!.Controls.Forward));
        Assert.True((await player.Bot.SetControlAsync(EnumEntityAction.Forward, false)).Accepted);
        Assert.False(Host.RunOnGameThread(() => player.Entity!.Controls.Forward));

        Assert.True((await player.Bot.SelectSlotAsync(1)).Accepted);
        Assert.Equal(1, Host.RunOnGameThread(() => player.Player!.InventoryManager.ActiveHotbarSlotNumber));
    }

    [ServerScenario]
    public async Task WalkToAsync_SendsPositionPacketsThroughTheServerUdpHandler()
    {
        Zaldaryon.Pharos.XUnit.ServerTestPlayer player = await CreateTestPlayerAsync("WalkerBot");
        Vec3d start = Host!.RunOnGameThread(() => new Vec3d(player.Entity!.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z));
        using (CancellationTokenSource alreadyCancelled = new())
        {
            alreadyCancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => player.Bot.WalkToAsync(start, ct: alreadyCancelled.Token));
        }

        BlockPos obstacle = new((int)Math.Floor(start.X + 1), (int)Math.Floor(start.Y), (int)Math.Floor(start.Z));
        Host.RunOnGameThread(() =>
        {
            Block stone = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(stone.BlockId, obstacle);
        });
        Vec3d destination = start.AddCopy(3, 0, 0);
        double maxY = start.Y;
        long listener = Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ =>
        {
            double currentY = player.Entity?.Pos.Y ?? start.Y;
            if (currentY > maxY) maxY = currentY;
        }, 0));

        Zaldaryon.Pharos.XUnit.BotResult result;
        try
        {
            result = await player.Bot.WalkToAsync(destination, maxTicks: 100);
        }
        finally
        {
            Host.RunOnGameThread(() => Api!.Event.UnregisterGameTickListener(listener));
        }

        Assert.True(result.Accepted, result.Reason ?? result.Error?.Code);
        Vec3d actual = Host.RunOnGameThread(() => new Vec3d(player.Entity!.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z));
        Assert.True(actual.DistanceTo(destination) <= 0.2, $"The bot stopped at {actual}, not {destination}");
        Assert.True(maxY >= start.Y + 0.8, $"The bot never stepped up to clear the block. Highest Y was {maxY}.");

        BlockPos tallWall = new((int)Math.Floor(start.X + 4), (int)Math.Floor(start.Y), (int)Math.Floor(start.Z));
        Host.RunOnGameThread(() =>
        {
            Block stone = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(stone.BlockId, tallWall);
            Api.World.BlockAccessor.SetBlock(stone.BlockId, tallWall.AddCopy(0, 1, 0));
        });
        Zaldaryon.Pharos.XUnit.BotResult refused = await player.Bot.WalkToAsync(start.AddCopy(7, 0, 0), maxTicks: 100);
        Assert.False(refused.Accepted);
        Assert.Contains("blocked", refused.Reason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => player.Bot.WalkToAsync(start.AddCopy(7, 0, 0), maxTicks: 100, speed: 40));
    }

    [ServerScenario]
    public async Task WalkToAsync_ReleasesForwardWhenCancelled()
    {
        Zaldaryon.Pharos.XUnit.ServerTestPlayer player = await CreateTestPlayerAsync("CancelledWalker");
        using CancellationTokenSource cancellation = new();
        long listener = Host!.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ =>
        {
            if (player.Entity?.Controls.Forward == true) cancellation.Cancel();
        }, 0));

        try
        {
            Vec3d destination = Host.RunOnGameThread(() => player.Entity!.Pos.XYZ.AddCopy(5, 0, 0));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.Bot.WalkToAsync(destination, ct: cancellation.Token));
        }
        finally
        {
            Host.RunOnGameThread(() => Api!.Event.UnregisterGameTickListener(listener));
        }

        Assert.True(cancellation.IsCancellationRequested, "The test did not cancel after the server processed Forward");
        Assert.False(Host.RunOnGameThread(() => player.Entity!.Controls.Forward), "Forward stayed pressed after WalkToAsync was cancelled");
    }

    [ServerScenario]
    public async Task BreakPlaceAndUseBlock_SendRealInteractionPackets()
    {
        Zaldaryon.Pharos.XUnit.ServerTestPlayer player = await CreateTestPlayerAsync("BlockBot");
        Host!.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);
        BlockPos origin = Host.RunOnGameThread(() => player.Entity!.Pos.AsBlockPos);
        BlockPos breakPos = origin.AddCopy(2, 1, 0);
        BlockPos placePos = origin.AddCopy(2, 1, 1);
        BlockPos chestPos = origin.AddCopy(1, 1, 0);
        BlockPos secondChestPos = origin.AddCopy(1, 1, 1);
        int graniteId = Host.RunOnGameThread(() => Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!.BlockId);
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(graniteId, breakPos));

        Zaldaryon.Pharos.XUnit.BotResult broken = await player.Bot.BreakBlockAsync(breakPos);
        Assert.True(broken.Accepted, broken.Reason ?? broken.Error?.Code);
        Assert.Equal(0, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(breakPos).BlockId));

        player.GiveItem("game:rock-granite");
        Zaldaryon.Pharos.XUnit.BotResult placed = await player.Bot.PlaceBlockAsync(placePos);
        Assert.True(placed.Accepted, placed.Reason ?? placed.Error?.Code);
        Assert.Equal(graniteId, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(placePos).BlockId));

        int chestId = Host.RunOnGameThread(() => Api!.World.GetBlock(new AssetLocation("game:chest-east"))!.BlockId);
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(chestId, chestPos));
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(chestId, secondChestPos));
        BlockPos seatPos = origin.AddCopy(0, 0, 2);
        Host.RunOnGameThread(() =>
        {
            int bedId = Api!.World.GetBlock(new AssetLocation("game:bed-wood-head-north"))!.BlockId;
            Api.World.BlockAccessor.SetBlock(bedId, seatPos);
        });
        Assert.True(await player.MountAsync(seatPos), "The bot player did not mount the nearby bed before using a block");
        Assert.NotNull(player.MountedOn);
        Zaldaryon.Pharos.XUnit.BotResult[] uses = await Task.WhenAll(
            player.Bot.UseBlockAsync(chestPos),
            player.Bot.UseBlockAsync(secondChestPos));
        Assert.All(uses, used => Assert.True(used.Accepted, used.Reason ?? used.Error?.Code));
        Assert.Equal(EnumHandInteract.None, Host.RunOnGameThread(() => player.Entity!.Controls.HandUse));
        Assert.True(await player.UnmountAsync(), "The bot player did not unmount after using a block");
        player.GiveItem("game:rock-granite", 3);
        (string sourceId, int sourceSlot, string chestInventoryId, int sourceCount) = Host.RunOnGameThread(() =>
        {
            IInventory hotbar = player.Player!.InventoryManager.GetHotbarInventory();
            IInventory chest = Assert.IsAssignableFrom<IBlockEntityContainer>(Api!.World.BlockAccessor.GetBlockEntity(chestPos)).Inventory;
            int slot = Enumerable.Range(0, hotbar.Count).First(index => hotbar[index]?.Itemstack?.Collectible?.Code.ToString() == "game:rock-granite");
            return (hotbar.InventoryID, slot, chest.InventoryID, hotbar[slot]!.StackSize);
        });
        Zaldaryon.Pharos.XUnit.BotResult moved = await player.Bot.MoveSlotAsync(sourceId, sourceSlot, chestInventoryId, 0, quantity: 1);
        Assert.True(moved.Accepted, moved.Reason ?? moved.Error?.Code);
        Assert.Equal(sourceCount - 1, Host.RunOnGameThread(() => player.Player!.InventoryManager.GetHotbarInventory()[sourceSlot]!.StackSize));
        Assert.Equal(1, Host.RunOnGameThread(() => Assert.IsAssignableFrom<IBlockEntityContainer>(
            Api!.World.BlockAccessor.GetBlockEntity(chestPos)).Inventory[0]!.StackSize));
    }

    [ServerScenario]
    public async Task EntityAttackAndUse_SendTheEntityInteractionPacket()
    {
        Zaldaryon.Pharos.XUnit.ServerTestPlayer player = await CreateTestPlayerAsync("EntityBot");
        string henCode = Host!.RunOnGameThread(() => Api!.World.EntityTypes
            .Select(type => type.Code.ToString())
            .First(code => code.EndsWith(":chicken-hen", StringComparison.Ordinal)));
        Entity useTarget = this.SpawnEntity(henCode, player.Entity!.Pos.XYZ.AddCopy(3, 0, 0));
        Entity attackTarget = this.SpawnEntity(henCode, player.Entity!.Pos.XYZ.AddCopy(-3, 0, 0));
        Entity blockedObserverTarget = this.SpawnEntity(henCode, player.Entity!.Pos.XYZ.AddCopy(0, 0, 4));
        ConcurrentQueue<(long EntityId, int Mode, Vec3d Hit)> interactions = new();
        int blockedObserverInvoked = 0;
        IServerEventAPI events = (IServerEventAPI)Api!.Event;
        OnInteractDelegate stopFurtherObservers = (Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hit, int mode, ref EnumHandling handling) =>
        {
            if (byPlayer.PlayerUID == player.PlayerUID && entity.EntityId == blockedObserverTarget.EntityId)
            {
                Interlocked.Exchange(ref blockedObserverInvoked, 1);
                handling = EnumHandling.PreventSubsequent;
            }
        };
        OnInteractDelegate handler = (Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hit, int mode, ref EnumHandling handling) =>
        {
            if (byPlayer.PlayerUID == player.PlayerUID) interactions.Enqueue((entity.EntityId, mode, hit.Clone()));
        };
        Host.RunOnGameThread(() =>
        {
            events.OnPlayerInteractEntity += stopFurtherObservers;
            events.OnPlayerInteractEntity += handler;
        });
        try
        {
            Zaldaryon.Pharos.XUnit.BotResult used = await player.Bot.UseEntityAsync(useTarget);
            Zaldaryon.Pharos.XUnit.BotResult attacked = await player.Bot.AttackEntityAsync(attackTarget);
            Zaldaryon.Pharos.XUnit.BotResult prevented = await player.Bot.UseEntityAsync(blockedObserverTarget);

            Assert.True(used.Accepted, used.Reason ?? used.Error?.Code);
            Assert.True(attacked.Accepted, attacked.Reason ?? attacked.Error?.Code);
            Assert.False(prevented.Accepted, "The bot reported acceptance even though an earlier event handler suppressed its observer");
            Assert.Contains("did not observe", prevented.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, Volatile.Read(ref blockedObserverInvoked));
            Assert.Contains(interactions, item => item.EntityId == useTarget.EntityId && item.Mode == (int)EnumInteractMode.Interact);
            Assert.Contains(interactions, item => item.EntityId == attackTarget.EntityId && item.Mode == (int)EnumInteractMode.Attack);
            Vec3d expectedHit = Host.RunOnGameThread(() => new Vec3d(
                useTarget.SelectionBox.MidX,
                useTarget.SelectionBox.MidY,
                useTarget.SelectionBox.MidZ));
            Vec3d actualHit = interactions.Single(item => item.EntityId == useTarget.EntityId && item.Mode == (int)EnumInteractMode.Interact).Hit;
            Assert.InRange(actualHit.DistanceTo(expectedHit), 0, 0.002);
        }
        finally
        {
            Host.RunOnGameThread(() =>
            {
                events.OnPlayerInteractEntity -= handler;
                events.OnPlayerInteractEntity -= stopFurtherObservers;
            });
        }
    }

}
