using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Blocks a client highlights in one slot.</summary>
/// <param name="Slot">The slot mods and the server highlight in.</param>
/// <param name="Positions">The highlighted blocks: absolute, or relative to the selected block, as <paramref name="Mode"/> says.</param>
/// <param name="Colors">The color of each block, as RGBA, or empty when the game's default color is used.</param>
/// <param name="Mode">How the positions are placed.</param>
/// <param name="Shape">The shape drawn.</param>
/// <param name="Scale">The scale drawn at.</param>
public sealed record ClientHighlight(int Slot, IReadOnlyList<BlockPos> Positions, IReadOnlyList<int> Colors, EnumHighlightBlocksMode Mode, EnumHighlightShape Shape, float Scale);

/// <summary>What a client highlights: from the server's highlight packets and the client's own <c>HighlightBlocks</c>.</summary>
/// <remarks>
/// Clearing a slot, from either side, leaves it with no blocks: the game removes a slot only for a
/// server packet with no block data at all, which the server's own API never sends.
/// </remarks>
internal static class HighlightInspector
{
    private static readonly ConditionalWeakTable<BlockHighlight, Recorded> s_recorded = new();

    public static ClientHighlight? Of(HeadlessClient client, int slot) => client.RunOnClientThread(() =>
        Slots(ClientGame.Require(client)).TryGetValue(slot, out BlockHighlight? highlight) ? Read(slot, highlight) : null);

    public static IReadOnlyList<int> SlotsOf(HeadlessClient client) => client.RunOnClientThread(() =>
        Slots(ClientGame.Require(client)).Keys.Order().ToList());

    private static Dictionary<int, BlockHighlight> Slots(ClientMain game)
    {
        SystemHighlightBlocks system = game.clientSystems?.OfType<SystemHighlightBlocks>().FirstOrDefault()
            ?? throw new InvalidOperationException("The client has no highlight system yet: it has not started a game.");
        return new Dictionary<int, BlockHighlight>(HighlightsBySlot(system));
    }

    private static ClientHighlight Read(int slot, BlockHighlight highlight)
    {
        Recorded recorded = s_recorded.TryGetValue(highlight, out Recorded? r) ? r : new Recorded([], []);
        return new ClientHighlight(slot, recorded.Positions, recorded.Colors, highlight.mode, highlight.shape, highlight.Scale);
    }

    // Records what a highlight shows; the game keeps only the mesh. Installed by ClientInspectionPatches.
    internal static void AfterTesselate(BlockHighlight __instance, BlockPos[] positions, int[] colors)
    {
        s_recorded.AddOrUpdate(__instance, new Recorded(
            positions?.Select(p => p.Copy()).ToArray() ?? [],
            colors?.ToArray() ?? []));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "highlightsByslotId")]
    private static extern ref Dictionary<int, BlockHighlight> HighlightsBySlot(SystemHighlightBlocks system);

    private sealed record Recorded(IReadOnlyList<BlockPos> Positions, IReadOnlyList<int> Colors);
}
