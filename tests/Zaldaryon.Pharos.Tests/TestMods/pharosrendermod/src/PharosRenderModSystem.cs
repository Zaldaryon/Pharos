using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PharosRenderMod;

/// <summary>
/// Registers a renderer and a bare render action, a tick listener and a callback, all counting
/// their calls, and <c>.pharosfx</c>, which spawns particles, highlights blocks and reports the counts.
/// </summary>
public sealed class PharosRenderModSystem : ModSystem
{
    public const int HighlightSlot = 9;

    private ICoreClientAPI? _api;
    private CountingRenderer? _renderer;
    private int _ticks;
    private int _bareRenders;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;
        _renderer = new CountingRenderer();
        api.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque, "pharosrendermod");
        api.Event.RegisterRenderer(new DummyRenderer { action = _ => _bareRenders++, RenderOrder = 0.97 }, EnumRenderStage.Ortho, "pharosrendermod-bare");
        api.Event.RegisterGameTickListener(_ => _ticks++, 50);
        api.Event.RegisterCallback(_ => { }, 600_000);

        IChatCommand command = api.ChatCommands.Create("pharosfx").WithDescription("Pharos render test");

        command.BeginSubCommand("counts")
            .HandleWith(_ => TextCommandResult.Success($"renders={_renderer.Calls} bare={_bareRenders} ticks={_ticks}"))
            .EndSubCommand();

        command.BeginSubCommand("particles")
            .HandleWith(_ =>
            {
                Vec3d at = api.World.Player.Entity.Pos.XYZ.AddCopy(0, 1, 0);
                // Whatever the particle setting says.
                api.World.SpawnParticles(new SimpleParticleProperties(
                    7, 7, ColorUtil.ToRgba(255, 200, 40, 40), at, at.Clone(), new Vec3f(), new Vec3f(), 30f, 0f, 1f, 1f, EnumParticleModel.Quad)
                {
                    IgnoreUserConfig = true,
                });
                return TextCommandResult.Success();
            })
            .EndSubCommand();

        command.BeginSubCommand("highlight")
            .HandleWith(_ =>
            {
                BlockPos at = api.World.Player.Entity.Pos.AsBlockPos;
                Highlight(api, new List<BlockPos> { at.AddCopy(1, 0, 0), at.AddCopy(2, 0, 0) },
                    new List<int> { ColorUtil.ToRgba(128, 255, 0, 0), ColorUtil.ToRgba(128, 0, 255, 0) });
                return TextCommandResult.Success();
            })
            .EndSubCommand();

        command.BeginSubCommand("clear")
            .HandleWith(_ =>
            {
                Highlight(api, new List<BlockPos>(), new List<int>());
                return TextCommandResult.Success();
            })
            .EndSubCommand();
    }

    // The game compiles source mods without the reference assembly the API's List<T> comes from,
    // (nor System.Linq), so a direct call to HighlightBlocks does not compile; it is found by name.
    private static void Highlight(ICoreClientAPI api, List<BlockPos> blocks, List<int> colors)
    {
        System.Reflection.MethodInfo? method = null;
        foreach (System.Reflection.MethodInfo candidate in typeof(IWorldAccessor).GetMethods())
        {
            if (candidate.Name == "HighlightBlocks" && candidate.GetParameters().Length == 7) method = candidate;
        }

        method!.Invoke(api.World, new object[] { api.World.Player, HighlightSlot, blocks, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary, 1f });
    }

    private sealed class CountingRenderer : IRenderer
    {
        public int Calls { get; private set; }

        public double RenderOrder => 0.42;

        public int RenderRange => 24;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => Calls++;

        public void Dispose()
        {
        }
    }
}
