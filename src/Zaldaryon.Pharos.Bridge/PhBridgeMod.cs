using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// The Pharos bridge mod that hooks into the Vintage Story client and publishes
/// game events to the <see cref="BridgeChannel"/> for scenario code consumption.
/// </summary>
/// <remarks>
/// A headless client in engine mode stages this mod automatically when the bridge assembly sits
/// next to the Pharos assembly, which it does whenever a test project references the bridge
/// package. It publishes the start and end of every rendered frame, every chunk the client
/// finishes tessellating and adds to its render pools, and every dialog that opens or closes.
/// </remarks>
public class PhBridgeMod : ModSystem, IRenderer
{
    private const string HarmonyId = "zaldaryon.pharos.bridge";

    private static readonly AccessTools.FieldRef<TesselatedChunk, int> s_positionX = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionX");
    private static readonly AccessTools.FieldRef<TesselatedChunk, int> s_positionYAndDimension = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionYAndDimension");
    private static readonly AccessTools.FieldRef<TesselatedChunk, int> s_positionZ = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionZ");

    private ICoreClientAPI? _api;
    private BridgeChannel? _channel;
    private Harmony? _harmony;
    private HashSet<string> _openDialogs = [];

    /// <inheritdoc />
    public double RenderOrder => 0;

    /// <inheritdoc />
    public int RenderRange => 0;

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>
    /// Called when the mod starts on the client side. Registers the renderer for frame events.
    /// </summary>
    /// <param name="api">The client API.</param>
    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;
        _channel = new BridgeChannel();
        BridgeChannel.Activate(_channel);

        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "pharos-bridge");
        api.Event.RegisterRenderer(this, EnumRenderStage.Done, "pharos-bridge");

        _harmony = new Harmony(HarmonyId);
        _harmony.Patch(
            AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.AddTesselatedChunk)),
            postfix: new HarmonyMethod(typeof(PhBridgeMod), nameof(OnChunkAdded)));
    }

    /// <inheritdoc />
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_channel is null) return;

        if (stage == EnumRenderStage.Before)
        {
            _channel.PublishFrameStart(deltaTime);
        }
        else if (stage == EnumRenderStage.Done)
        {
            PublishDialogChanges(_channel);
            _channel.PublishFrameEnd(deltaTime);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_api is not null)
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Done);
        }

        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;

        if (ReferenceEquals(BridgeChannel.Active, _channel))
        {
            BridgeChannel.Deactivate();
        }

        _channel = null;
        _api = null;

        base.Dispose();
    }

    private void PublishDialogChanges(BridgeChannel channel)
    {
        if (_api is null) return;

        // Dialogs are named by type, as GuiDriver names them.
        HashSet<string> open = [.. _api.Gui.OpenedGuis.Select(d => d.GetType().Name)];

        foreach (string closed in _openDialogs.Where(name => !open.Contains(name)))
        {
            channel.PublishGuiStateChanged(closed, isOpen: false);
        }

        foreach (string opened in open.Where(name => !_openDialogs.Contains(name)))
        {
            channel.PublishGuiStateChanged(opened, isOpen: true);
        }

        _openDialogs = open;
    }

    private static void OnChunkAdded(TesselatedChunk tesschunk)
    {
        int size = GlobalConstants.ChunkSize;
        // The Y position carries the dimension above one dimension's height in blocks.
        int y = s_positionYAndDimension(tesschunk) % (GlobalConstants.DimensionSizeInChunks * size);
        BridgeChannel.Active?.PublishChunkTessellated(s_positionX(tesschunk) / size, y / size, s_positionZ(tesschunk) / size);
    }
}
