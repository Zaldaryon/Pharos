using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PharosLangMod;

/// <summary>
/// Counts the client's reload events and registers <c>.pharoslang</c>, which reports the counts,
/// and makes the next shader reload fail.
/// </summary>
public sealed class PharosLangModSystem : ModSystem
{
    private int _shaderReloads, _shapeReloads, _textureReloads;
    private bool _failShaders;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += () =>
        {
            _shaderReloads++;
            if (!_failShaders) return true;
            _failShaders = false;
            return false;
        };
        api.Event.ReloadShapes += () => _shapeReloads++;
        api.Event.ReloadTextures += () => _textureReloads++;

        IChatCommand command = api.ChatCommands.Create("pharoslang").WithDescription("Pharos reload and translation test");
        command.BeginSubCommand("counts")
            .HandleWith(_ => TextCommandResult.Success($"shaders={_shaderReloads} shapes={_shapeReloads} textures={_textureReloads}"))
            .EndSubCommand();
        command.BeginSubCommand("failshaders")
            .HandleWith(_ =>
            {
                _failShaders = true;
                return TextCommandResult.Success();
            })
            .EndSubCommand();
    }
}
