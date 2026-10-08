using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PharosErrorMod;

/// <summary>Logs an error a second after the first player joins.</summary>
public sealed class PharosErrorModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.PlayerJoin += _ => api.Event.RegisterCallback(
            _ => Mod.Logger.Error("Pharos error mod broke during play"), 1000);
    }
}
