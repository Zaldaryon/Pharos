using System.IO;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Content;

/// <summary>Sweeps blocks, items and entity types from a mod through a fixed server-side script.</summary>
/// <remarks>
/// The sweep uses a joined test player for placement, breaking, held items and item pickup. It
/// reports exceptions and logged errors per step instead of stopping at the first content failure.
/// Run it from a server scenario with rollback isolation so the test world is restored afterward.
/// </remarks>
public static class ContentSweep
{
    private const string PlayerName = "SweepBot";

    /// <summary>Runs every live block, item and entity code in <paramref name="domain"/>.</summary>
    /// <param name="host">The running embedded server.</param>
    /// <param name="domain">The mod domain to sweep.</param>
    /// <param name="options">Steps and code filters; defaults to every applicable step and code.</param>
    /// <param name="ct">Cancellation token checked between server ticks and content codes.</param>
    /// <returns>Per-code step results, exceptions, logged errors and leaked tick listeners.</returns>
    public static async Task<SweepReport> RunAsync(
        EmbeddedServerHost host,
        string domain,
        ContentSweepOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        options ??= new ContentSweepOptions();
        Validate(options);

        ContentCode[] codes = host.RunOnGameThread(() => ContentSweepCatalog.FromWorld(host.Api.World, domain, options)
            .Select(code => new ContentCode(code.Kind, code.Code, options.Steps, code.Variants))
            .ToArray());
        return await RunCodesAsync(host, codes, options, ct).ConfigureAwait(false);
    }

    /// <summary>Runs the steps selected for one content code.</summary>
    /// <param name="host">The running embedded server.</param>
    /// <param name="code">The block, item, entity or whole-domain case.</param>
    /// <param name="options">Optional step and tick overrides. By default the case's own steps are used.</param>
    /// <param name="ct">Cancellation token checked between server ticks and content steps.</param>
    /// <returns>Per-step results, exceptions, logged errors and leaked tick listeners.</returns>
    public static Task<SweepReport> RunAsync(
        EmbeddedServerHost host,
        ContentCode code,
        ContentSweepOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(code);
        options ??= new ContentSweepOptions { Steps = code.Steps };
        Validate(options);

        if (code.Kind == ContentKind.Domain)
            return RunAsync(host, code.Domain, options, ct);

        return RunCodesAsync(host, [new ContentCode(code.Kind, code.Code, options.Steps, code.Variants)], options, ct);
    }

    private static async Task<SweepReport> RunCodesAsync(
        EmbeddedServerHost host,
        IReadOnlyList<ContentCode> codes,
        ContentSweepOptions options,
        CancellationToken ct)
    {
        SweepReportBuilder report = new();
        if (codes.Count == 0) return report.Build();

        ServerTestPlayer player = await host.JoinPlayerAsync(NextPlayerName(host), ct: ct).ConfigureAwait(false);
        try
        {
            host.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);

            foreach (ContentCode code in codes)
            {
                ct.ThrowIfCancellationRequested();
                await RunOneAsync(host, player, code, options, report, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            player.Dispose();
        }

        return report.Build();
    }

    private static async Task RunOneAsync(
        EmbeddedServerHost host,
        ServerTestPlayer player,
        ContentCode code,
        ContentSweepOptions options,
        SweepReportBuilder report,
        CancellationToken ct)
    {
        report.Include(code);
        IReadOnlyList<ServerTickListenerInfo> beforeListeners = host.TickListeners.All();
        SweepContext context = new(code);

        try
        {
            switch (code.Kind)
            {
                case ContentKind.Block:
                    await SweepBlockAsync(host, player, context, options, report, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Item:
                    await SweepItemAsync(host, player, context, options, report, ct).ConfigureAwait(false);
                    break;
                case ContentKind.Entity:
                    await SweepEntityAsync(host, player, context, options, report, ct).ConfigureAwait(false);
                    break;
                default:
                    report.Fail(code, SweepStep.Cleanup, $"Content kind '{code.Kind}' is not a single-code sweep.", null, []);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            report.Fail(code, SweepStep.Resolve, exception.Message, exception, []);
        }
        finally
        {
            await CleanupAsync(host, context, report).ConfigureAwait(false);
            try
            {
                CaptureLeakedListeners(host, code, beforeListeners, report);
            }
            catch (Exception exception)
            {
                report.Fail(code, SweepStep.Cleanup, exception.Message, exception, []);
            }
        }
    }

    private static async Task SweepBlockAsync(
        EmbeddedServerHost host,
        ServerTestPlayer player,
        SweepContext context,
        ContentSweepOptions options,
        SweepReportBuilder report,
        CancellationToken ct)
    {
        const SweepSteps blockSteps = SweepSteps.BlockPlace | SweepSteps.BlockTick
            | SweepSteps.BlockNeighbourChange | SweepSteps.BlockEntityRoundTrip
            | SweepSteps.BlockDrops | SweepSteps.BlockBreak;
        if (!HasAnyStep(options.Steps, blockSteps)) return;

        Block? block = host.RunOnGameThread(() => host.Api.World.GetBlock(new AssetLocation(context.Code.Code)));
        if (block is null)
        {
            report.Fail(context.Code, HasStep(options, SweepSteps.BlockPlace) ? SweepStep.BlockPlace : SweepStep.Resolve,
                $"Block '{context.Code.Code}' was not loaded in the server world.", null, []);
            return;
        }

        bool placementRequested = HasStep(options, SweepSteps.BlockPlace);
        await CaptureStepAsync(host, report, context.Code,
            placementRequested ? SweepStep.BlockPlace : SweepStep.Resolve,
            async () =>
        {
            (context.Support, context.Position) = host.RunOnGameThread(() => FindBlockPosition(host.Api.World, player));
            if (context.Position is null || context.Support is null) return $"Could not find a loaded support block for '{context.Code.Code}'.";

            if (!placementRequested)
            {
                host.RunOnGameThread(() => host.Api.World.BlockAccessor.SetBlock(block.BlockId, context.Position));
                return null;
            }

            player.GiveItem(block.Code.ToString());
            int slot = FindHotbarSlot(host, player, block.Code.ToString());
            if (slot < 0) return $"The test player could not hold block '{context.Code.Code}'.";
            BotResult result = await player.Bot.PlaceBlockAsync(context.Position, hotbarSlot: slot, ct: ct).ConfigureAwait(false);
            bool placed = host.RunOnGameThread(() => host.Api.World.BlockAccessor.GetBlock(context.Position).BlockId == block.BlockId);
            if (!placed)
            {
                // Continue the remaining checks even when player placement fails. The report keeps
                // the placement failure, while this fallback gives each later block hook a target.
                host.RunOnGameThread(() => host.Api.World.BlockAccessor.SetBlock(block.BlockId, context.Position));
                return result.Reason ?? result.Error?.Code ?? $"The server did not place '{context.Code.Code}'.";
            }

            return result.Accepted ? null : result.Reason ?? result.Error?.Code ?? "The player placement was refused.";
        }, ct, recordPass: placementRequested).ConfigureAwait(false);

        if (context.Position is null || context.Support is null) return;

        if (HasStep(options, SweepSteps.BlockTick))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.BlockTick, () =>
            {
                host.Ticks(options.Ticks);
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.BlockNeighbourChange))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.BlockNeighbourChange, () =>
            {
                host.RunOnGameThread(() => host.Api.World.BlockAccessor.GetBlock(context.Position)
                    .OnNeighbourBlockChange(host.Api.World, context.Position, context.Support));
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.BlockEntityRoundTrip))
        {
            BlockEntity? blockEntity = host.RunOnGameThread(() => host.Api.World.BlockAccessor.GetBlockEntity(context.Position));
            if (blockEntity is null)
            {
                report.WithoutBlockEntity(context.Code);
            }
            else
            {
                await CaptureStepAsync(host, report, context.Code, SweepStep.BlockEntityRoundTrip, () =>
                {
                    host.RunOnGameThread(() =>
                    {
                        TreeAttribute tree = new();
                        blockEntity.ToTreeAttributes(tree);
                        blockEntity.FromTreeAttributes(tree, host.Api.World);
                    });
                    return Task.FromResult<string?>(null);
                }, ct).ConfigureAwait(false);
            }
        }

        if (HasStep(options, SweepSteps.BlockDrops))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.BlockDrops, async () =>
            {
                string? toolError = await HoldBestToolAsync(host, player, block, context.Position, ct).ConfigureAwait(false);
                if (toolError is not null) return toolError;

                List<string> unresolvedDrops = [];
                foreach (BlockDropItemStack drop in block.Drops ?? [])
                {
                    bool resolved = host.RunOnGameThread(() => drop.Resolve(host.Api.World, "ContentSweep", block.Code));
                    if (!resolved) unresolvedDrops.Add(drop.Code?.ToString() ?? "(missing code)");
                }

                ItemStack[]? drops = host.RunOnGameThread(() => block.GetDrops(host.Api.World, context.Position, player.Player!));
                if (drops is not null)
                {
                    foreach (ItemStack drop in drops)
                    {
                        if (!drop.ResolveBlockOrItem(host.Api.World))
                            unresolvedDrops.Add(drop.Collectible?.Code?.ToString() ?? "(unresolved stack)");
                    }
                }
                return unresolvedDrops.Count == 0
                    ? null
                    : $"Block '{context.Code.Code}' has unresolved drop code(s): {string.Join(", ", unresolvedDrops.Distinct(StringComparer.OrdinalIgnoreCase))}.";
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.BlockBreak))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.BlockBreak, async () =>
            {
                if (!host.RunOnGameThread(() => host.Api.World.BlockAccessor.GetBlock(context.Position).BlockId == block.BlockId))
                    return $"Block '{context.Code.Code}' was no longer present before the break step.";
                BotResult result = await player.Bot.BreakBlockAsync(context.Position, ct: ct).ConfigureAwait(false);
                return result.Accepted ? null : result.Reason ?? result.Error?.Code ?? "The player break was refused.";
            }, ct).ConfigureAwait(false);
        }
    }

    private static async Task SweepItemAsync(
        EmbeddedServerHost host,
        ServerTestPlayer player,
        SweepContext context,
        ContentSweepOptions options,
        SweepReportBuilder report,
        CancellationToken ct)
    {
        const SweepSteps itemSteps = SweepSteps.ItemGive | SweepSteps.ItemHeldIdle
            | SweepSteps.ItemGroundIdle | SweepSteps.ItemCollect | SweepSteps.ItemHeldItemInfo
            | SweepSteps.ItemTransitions | SweepSteps.ItemDurability;
        if (!HasAnyStep(options.Steps, itemSteps)) return;

        Item? item = host.RunOnGameThread(() => host.Api.World.GetItem(new AssetLocation(context.Code.Code)));
        if (item is null)
        {
            report.Fail(context.Code, SweepStep.ItemGive, $"Item '{context.Code.Code}' was not loaded in the server world.", null, []);
            return;
        }

        if (HasStep(options, SweepSteps.ItemGive))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemGive, async () =>
            {
                player.GiveItem(item.Code.ToString());
                context.ItemSlot = FindItemSlot(host, player, item.Code.ToString());
                if (context.ItemSlot is null) return $"The test player could not hold item '{context.Code.Code}'.";
                int slot = FindHotbarSlot(host, player, item.Code.ToString());
                if (slot < 0) return $"Item '{context.Code.Code}' was not placed in the player's hotbar.";
                BotResult selected = await player.Bot.SelectSlotAsync(slot, ct: ct).ConfigureAwait(false);
                return selected.Accepted ? null : selected.Reason ?? selected.Error?.Code ?? "The item hotbar slot was not selected.";
            }, ct).ConfigureAwait(false);
        }

        context.ItemSlot ??= FindItemSlot(host, player, item.Code.ToString());
        if (context.ItemSlot is null && !HasStep(options, SweepSteps.ItemGive) && HasAnyStep(options.Steps,
                SweepSteps.ItemHeldIdle | SweepSteps.ItemGroundIdle | SweepSteps.ItemCollect |
                SweepSteps.ItemHeldItemInfo | SweepSteps.ItemTransitions | SweepSteps.ItemDurability))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.Resolve, () =>
            {
                player.GiveItem(item.Code.ToString());
                context.ItemSlot = FindItemSlot(host, player, item.Code.ToString());
                return Task.FromResult<string?>(context.ItemSlot is null ? $"The test player could not hold item '{context.Code.Code}'." : null);
            }, ct, recordPass: false).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemHeldIdle))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemHeldIdle, () =>
            {
                if (context.ItemSlot?.Itemstack is null) return Task.FromResult<string?>("The held-item slot is empty.");
                for (int tick = 0; tick < options.Ticks; tick++)
                {
                    ct.ThrowIfCancellationRequested();
                    host.RunOnGameThread(() => item.OnHeldIdle(context.ItemSlot, player.Entity!));
                    host.Tick();
                }
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemGroundIdle))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemGroundIdle, () =>
            {
                ItemStack stack = new(item);
                EntityItem? groundItem = host.RunOnGameThread(() =>
                {
                    Vec3d position = player.Entity!.Pos.XYZ.AddCopy(6, 0, 0);
                    return host.Api.World.SpawnItemEntity(stack, position) as EntityItem;
                });
                if (groundItem is null) return Task.FromResult<string?>("The world did not create an entity item.");
                context.GroundItems.Add(groundItem);
                for (int tick = 0; tick < options.Ticks; tick++)
                {
                    ct.ThrowIfCancellationRequested();
                    host.RunOnGameThread(() => item.OnGroundIdle(groundItem));
                    host.Tick();
                }
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemCollect))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemCollect, () =>
            {
                ItemSlot? slot = FindItemSlot(host, player, item.Code.ToString());
                if (slot?.Itemstack is null) return Task.FromResult<string?>("The test player has no item stack to drop.");
                ItemStack? dropped = slot.TakeOut(1);
                slot.MarkDirty();
                if (dropped is null) return Task.FromResult<string?>("The item stack could not be removed from its slot.");

                int afterDrop = CountItem(host, player, item.Code.ToString());
                EntityItem? groundItem = host.RunOnGameThread(() =>
                {
                    Vec3d position = player.Entity!.Pos.XYZ.AddCopy(0.25, 0, 0.25);
                    return host.Api.World.SpawnItemEntity(dropped, position) as EntityItem;
                });
                if (groundItem is null) return Task.FromResult<string?>("The world did not create the dropped entity item.");
                context.GroundItems.Add(groundItem);
                host.Ticks(options.Ticks);
                int afterTicks = CountItem(host, player, item.Code.ToString());
                return Task.FromResult<string?>(afterTicks > afterDrop
                    ? null
                    : $"The player did not collect dropped item '{context.Code.Code}' after {options.Ticks} ticks.");
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemHeldItemInfo))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemHeldItemInfo, () =>
            {
                ItemSlot? slot = FindItemSlot(host, player, item.Code.ToString());
                if (slot?.Itemstack is null) return Task.FromResult<string?>("The test player's item slot is empty.");
                StringBuilder text = new();
                host.RunOnGameThread(() => item.GetHeldItemInfo(slot, text, host.Api.World, withDebugInfo: false));
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemTransitions))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemTransitions, () =>
            {
                ItemSlot? slot = FindItemSlot(host, player, item.Code.ToString());
                if (slot?.Itemstack is null) return Task.FromResult<string?>("The test player's item slot is empty.");
                host.RunOnGameThread(() => item.UpdateAndGetTransitionStates(host.Api.World, slot));
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.ItemDurability))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.ItemDurability, () =>
            {
                ItemStack stack = new(item);
                int durability = host.RunOnGameThread(() => item.GetMaxDurability(stack));
                if (durability <= 0) return Task.FromResult<string?>(null);
                DummySlot damaged = new(stack);
                host.RunOnGameThread(() => item.DamageItem(host.Api.World, player.Entity!, damaged, durability, destroyOnZeroDurability: true));
                return Task.FromResult<string?>(damaged.Itemstack is null || item.GetRemainingDurability(damaged.Itemstack) == 0
                    ? null
                    : $"Item '{context.Code.Code}' kept durability after damage by {durability}.");
            }, ct).ConfigureAwait(false);
        }
    }

    private static async Task SweepEntityAsync(
        EmbeddedServerHost host,
        ServerTestPlayer player,
        SweepContext context,
        ContentSweepOptions options,
        SweepReportBuilder report,
        CancellationToken ct)
    {
        const SweepSteps entitySteps = SweepSteps.EntitySpawn | SweepSteps.EntityTick
            | SweepSteps.EntityDamage | SweepSteps.EntityDie | SweepSteps.EntityDespawn
            | SweepSteps.EntityRoundTrip;
        if (!HasAnyStep(options.Steps, entitySteps)) return;

        EntityProperties? properties = host.RunOnGameThread(() => host.Api.World.GetEntityType(new AssetLocation(context.Code.Code)));
        if (properties is null)
        {
            report.Fail(context.Code, SweepStep.EntitySpawn, $"Entity type '{context.Code.Code}' was not loaded in the server world.", null, []);
            return;
        }

        if (HasStep(options, SweepSteps.EntitySpawn))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntitySpawn, async () =>
            {
                Vec3d position = host.RunOnGameThread(() => player.Entity!.Pos.XYZ.AddCopy(4, 0, 0));
                context.Entity = await host.Entities.SpawnAsync(context.Code.Code, position).ConfigureAwait(false);
                return null;
            }, ct).ConfigureAwait(false);
        }

        if (context.Entity is null)
        {
            if (HasStep(options, SweepSteps.EntitySpawn)) return;
            await CaptureStepAsync(host, report, context.Code, SweepStep.Resolve, async () =>
            {
                Vec3d position = host.RunOnGameThread(() => player.Entity!.Pos.XYZ.AddCopy(4, 0, 0));
                context.Entity = await host.Entities.SpawnAsync(context.Code.Code, position).ConfigureAwait(false);
                return null;
            }, ct, recordPass: false).ConfigureAwait(false);
            if (context.Entity is null) return;
        }

        if (HasStep(options, SweepSteps.EntityTick))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntityTick, () =>
            {
                host.Ticks(options.Ticks);
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        DamageSource damage = new()
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.BluntAttack,
            SourcePos = host.RunOnGameThread(() => context.Entity.Pos.XYZ.AddCopy(1, 0, 0)),
        };

        if (HasStep(options, SweepSteps.EntityRoundTrip))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntityRoundTrip, () =>
            {
                host.RunOnGameThread(() =>
                {
                    using MemoryStream stream = new();
                    using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) context.Entity.ToBytes(writer, forClient: false);
                    stream.Position = 0;
                    using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
                    context.Entity.FromBytes(reader, isSync: false);
                });
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.EntityDamage))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntityDamage, () =>
            {
                host.RunOnGameThread(() => context.Entity.ReceiveDamage(damage, 1f));
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.EntityDie))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntityDie, () =>
            {
                host.RunOnGameThread(() => context.Entity.Die(EnumDespawnReason.Death, damage));
                host.Ticks(1);
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
        }

        if (HasStep(options, SweepSteps.EntityDespawn))
        {
            await CaptureStepAsync(host, report, context.Code, SweepStep.EntityDespawn, () =>
            {
                DespawnEntity(host, context.Entity);
                return Task.FromResult<string?>(null);
            }, ct).ConfigureAwait(false);
            context.Entity = null;
        }
    }

    private static async Task<string?> HoldBestToolAsync(
        EmbeddedServerHost host,
        ServerTestPlayer player,
        Block block,
        BlockPos position,
        CancellationToken ct)
    {
        int requiredTier = host.RunOnGameThread(() => block.GetRequiredMiningTier(host.Api.World, position));
        if (requiredTier <= 0) return null;

        (CollectibleObject? Tool, int Tier) best = host.RunOnGameThread(() =>
        {
            HashSet<EnumTool> requiredTools = block.Drops?
                .Where(drop => drop.Tool.HasValue)
                .Select(drop => drop.Tool!.Value)
                .ToHashSet() ?? [];
            return host.Api.World.Items
                .Cast<CollectibleObject>()
                .Concat(host.Api.World.Blocks)
                .Select(collectible =>
                {
                    ItemStack stack = new(collectible);
                    DummySlot slot = new(stack);
                    return (Collectible: collectible, Slot: slot, Tier: collectible.GetToolTier(slot), Tool: collectible.GetTool(slot));
                })
                .Where(candidate => candidate.Tier >= requiredTier
                    && candidate.Tool.HasValue
                    && (requiredTools.Count == 0 || requiredTools.Contains(candidate.Tool.Value)))
                .OrderByDescending(candidate => candidate.Tier)
                .ThenBy(candidate => candidate.Collectible.Code.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(candidate => ((CollectibleObject?)candidate.Collectible, candidate.Tier))
                .FirstOrDefault();
        });

        if (best.Tool is null) return $"Block '{block.Code}' requires mining tier {requiredTier}, but no matching tool was loaded.";
        player.GiveItem(best.Tool.Code.ToString());
        int slotNumber = FindHotbarSlot(host, player, best.Tool.Code.ToString());
        if (slotNumber < 0) return $"The test player could not hold tool '{best.Tool.Code}'.";
        BotResult selected = await player.Bot.SelectSlotAsync(slotNumber, ct: ct).ConfigureAwait(false);
        return selected.Accepted ? null : selected.Reason ?? selected.Error?.Code ?? "The best tool was not selected.";
    }

    private static (BlockPos? Support, BlockPos? Target) FindBlockPosition(IWorldAccessor world, ServerTestPlayer player)
    {
        BlockPos origin = player.Entity!.Pos.AsBlockPos;
        for (int distance = 2; distance <= 4; distance++)
        {
            for (int z = -1; z <= 1; z++)
            {
                BlockPos support = origin.AddCopy(distance, -1, z);
                BlockPos target = support.UpCopy();
                Block supportBlock = world.BlockAccessor.GetBlock(support);
                Block targetBlock = world.BlockAccessor.GetBlock(target);
                if (supportBlock.BlockId != 0 && !supportBlock.IsLiquid() && targetBlock.BlockId == 0)
                    return (support, target);
            }
        }
        return (null, null);
    }

    private static int FindHotbarSlot(EmbeddedServerHost host, ServerTestPlayer player, string code) => host.RunOnGameThread(() =>
    {
        IInventory hotbar = player.Player!.InventoryManager.GetHotbarInventory();
        for (int index = 0; index < hotbar.Count; index++)
        {
            if (string.Equals(hotbar[index]?.Itemstack?.Collectible?.Code?.ToString(), code, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    });

    private static ItemSlot? FindItemSlot(EmbeddedServerHost host, ServerTestPlayer player, string code) => host.RunOnGameThread(() =>
    {
        foreach (IInventory inventory in player.Player!.InventoryManager.Inventories.Values)
        {
            if (inventory.ClassName == GlobalConstants.creativeInvClassName) continue;
            foreach (ItemSlot slot in inventory)
            {
                if (string.Equals(slot?.Itemstack?.Collectible?.Code?.ToString(), code, StringComparison.OrdinalIgnoreCase)) return slot;
            }
        }
        return null;
    });

    private static int CountItem(EmbeddedServerHost host, ServerTestPlayer player, string code) => host.RunOnGameThread(() =>
    {
        int count = 0;
        foreach (IInventory inventory in player.Player!.InventoryManager.Inventories.Values)
        {
            if (inventory.ClassName == GlobalConstants.creativeInvClassName) continue;
            foreach (ItemSlot slot in inventory)
            {
                ItemStack? stack = slot?.Itemstack;
                if (string.Equals(stack?.Collectible?.Code?.ToString(), code, StringComparison.OrdinalIgnoreCase) && stack is not null)
                    count += stack.StackSize;
            }
        }
        return count;
    });

    private static void DespawnItemEntity(EmbeddedServerHost host, EntityItem? entity)
    {
        if (entity is null) return;
        host.RunOnGameThread(() =>
        {
            if (host.Api.World.GetEntityById(entity.EntityId) is Entity current)
                host.Api.World.DespawnEntity(current, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
        });
    }

    private static void DespawnEntity(EmbeddedServerHost host, Entity? entity)
    {
        if (entity is null) return;
        host.RunOnGameThread(() =>
        {
            if (host.Api.World.GetEntityById(entity.EntityId) is Entity current)
                host.Api.World.DespawnEntity(current, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
        });
    }

    private static async Task CleanupAsync(EmbeddedServerHost host, SweepContext context, SweepReportBuilder report)
    {
        if (context.Entity is null && context.GroundItems.Count == 0 && context.Position is null) return;
        int before = host.Logs.Errors.Count;
        Exception? exception = null;
        try
        {
            if (context.Entity is not null) DespawnEntity(host, context.Entity);
            foreach (EntityItem groundItem in context.GroundItems) DespawnItemEntity(host, groundItem);
            if (context.Position is not null)
            {
                host.RunOnGameThread(() =>
                {
                    if (host.Api.World.BlockAccessor.GetBlock(context.Position).BlockId != 0)
                        host.Api.World.BlockAccessor.SetBlock(0, context.Position);
                });
            }
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        IReadOnlyList<LogEntry> errors = NewErrors(host.Logs, before);
        if (exception is not null || errors.Count > 0)
            report.Fail(context.Code, SweepStep.Cleanup, exception?.Message ?? "Cleanup logged an error.", exception, errors);
        else
            report.Pass(context.Code, SweepStep.Cleanup);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void CaptureLeakedListeners(
        EmbeddedServerHost host,
        ContentCode code,
        IReadOnlyList<ServerTickListenerInfo> before,
        SweepReportBuilder report)
    {
        HashSet<(ListenerManager Manager, long Id)> ids = before.Select(listener => (listener.Manager, listener.Id)).ToHashSet();
        foreach (ServerTickListenerInfo listener in host.TickListeners.All())
        {
            if (ids.Contains((listener.Manager, listener.Id))) continue;
            report.Leak(code, listener);
            report.Fail(code, SweepStep.Cleanup, $"Tick listener {listener.Id} from {listener.Handler} remained after cleanup.", null, []);
        }
    }

    private static async Task CaptureStepAsync(
        EmbeddedServerHost host,
        SweepReportBuilder report,
        ContentCode code,
        SweepStep step,
        Func<Task<string?>> action,
        CancellationToken ct,
        bool recordPass = true)
    {
        ct.ThrowIfCancellationRequested();
        int errorCount = host.Logs.Errors.Count;
        string? detail = null;
        Exception? exception = null;
        try
        {
            detail = await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception caught)
        {
            exception = caught is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                ? aggregate.InnerExceptions[0]
                : caught;
            detail ??= exception.Message;
        }

        IReadOnlyList<LogEntry> errors = NewErrors(host.Logs, errorCount);
        if (exception is not null || detail is not null || errors.Count > 0)
        {
            string message = detail ?? "The step logged an error.";
            if (errors.Count > 0) message += " " + string.Join(" | ", errors.Select(error => error.Message));
            report.Fail(code, step, message, exception, errors);
        }
        else if (recordPass)
        {
            report.Pass(code, step);
        }
    }

    private static IReadOnlyList<LogEntry> NewErrors(LogCapture logs, int previousCount)
    {
        IReadOnlyList<LogEntry> after = logs.Errors;
        if (previousCount <= 0) return after;
        return after.Count >= previousCount ? after.Skip(previousCount).ToArray() : after;
    }

    private static bool HasStep(ContentSweepOptions options, SweepSteps step) => (options.Steps & step) != 0;

    private static bool HasAnyStep(SweepSteps steps, SweepSteps any) => (steps & any) != 0;

    private static void Validate(ContentSweepOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Ticks);
        ArgumentNullException.ThrowIfNull(options.Skip);
        ArgumentNullException.ThrowIfNull(options.Variants);
    }

    private static string NextPlayerName(EmbeddedServerHost host)
    {
        HashSet<string> names = host.TestPlayers
            .Select(player => player.PlayerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        if (!names.Contains(PlayerName)) return PlayerName;
        for (int suffix = 1; ; suffix++)
        {
            string candidate = PlayerName + suffix;
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private sealed class SweepContext(ContentCode code)
    {
        public ContentCode Code { get; } = code;
        public BlockPos? Support { get; set; }
        public BlockPos? Position { get; set; }
        public ItemSlot? ItemSlot { get; set; }
        public Entity? Entity { get; set; }
        public List<EntityItem> GroundItems { get; } = [];
    }

    private sealed class SweepReportBuilder
    {
        private readonly List<ContentCode> _blocks = [];
        private readonly List<ContentCode> _items = [];
        private readonly List<ContentCode> _entities = [];
        private readonly List<ContentCode> _withoutBlockEntity = [];
        private readonly List<SweepStepResult> _passed = [];
        private readonly List<SweepFailure> _failures = [];
        private readonly List<LeakedTickListener> _leaks = [];

        public void Include(ContentCode code)
        {
            List<ContentCode> list = code.Kind switch
            {
                ContentKind.Block => _blocks,
                ContentKind.Item => _items,
                ContentKind.Entity => _entities,
                _ => [],
            };
            if (!list.Any(existing => existing.Kind == code.Kind && string.Equals(existing.Code, code.Code, StringComparison.OrdinalIgnoreCase)))
                list.Add(code);
        }

        public void WithoutBlockEntity(ContentCode code) => _withoutBlockEntity.Add(code);

        public void Pass(ContentCode code, SweepStep step) => _passed.Add(new SweepStepResult(code, step));

        public void Fail(ContentCode code, SweepStep step, string message, Exception? exception, IReadOnlyList<LogEntry> errors) =>
            _failures.Add(new SweepFailure(code, step, message, exception, errors));

        public void Leak(ContentCode code, ServerTickListenerInfo listener) => _leaks.Add(new LeakedTickListener(code, listener));

        public void Add(SweepReport report)
        {
            _blocks.AddRange(report.Blocks);
            _items.AddRange(report.Items);
            _entities.AddRange(report.Entities);
            _withoutBlockEntity.AddRange(report.WithoutBlockEntity);
            _passed.AddRange(report.PassedSteps);
            _failures.AddRange(report.Failures);
            _leaks.AddRange(report.LeakedTickListeners);
        }

        public SweepReport Build() => new(
            _blocks.ToArray(),
            _items.ToArray(),
            _entities.ToArray(),
            _withoutBlockEntity.ToArray(),
            _passed.ToArray(),
            _failures.ToArray(),
            _leaks.ToArray());
    }
}
