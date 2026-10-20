using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PharosLoadMod;

/// <summary>The first server system the mod loading tests look for.</summary>
public sealed class PharosLoadSystem : ModSystem
{
    public override double ExecuteOrder() => 0.20;

    public override void StartPre(ICoreAPI api) { }

    public override void Start(ICoreAPI api) { }

    public override void AssetsLoaded(ICoreAPI api) { }

    public override void AssetsFinalize(ICoreAPI api) { }

    public override void StartServerSide(ICoreServerAPI api) { }
}

/// <summary>A later server system, so the run log has an order to assert.</summary>
public sealed class PharosLateSystem : ModSystem
{
    public override double ExecuteOrder() => 0.30;

    public override void StartServerSide(ICoreServerAPI api) { }
}

/// <summary>A client-only system that a server host must report but never run.</summary>
public sealed class PharosClientOnlySystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override double ExecuteOrder() => 0.21;

    public override void StartClientSide(ICoreClientAPI api) { }
}
