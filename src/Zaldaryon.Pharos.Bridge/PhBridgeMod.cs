using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// The Pharos bridge mod that hooks into the Vintage Story client and publishes
/// game events to the <see cref="BridgeChannel"/> for scenario code consumption.
/// </summary>
public class PhBridgeMod : ModSystem, IRenderer
{
    private ICoreClientAPI? _api;
    private BridgeChannel? _channel;

    /// <inheritdoc />
    public double RenderOrder => 0;

    /// <inheritdoc />
    public int RenderRange => 0;

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
    }

    /// <inheritdoc />
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_channel is null) return;

        if (stage == EnumRenderStage.Before)
        {
            _channel.PublishFrameStart(deltaTime);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_api is not null)
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        }

        BridgeChannel.Deactivate();
        _channel = null;
        _api = null;

        base.Dispose();
    }
}
