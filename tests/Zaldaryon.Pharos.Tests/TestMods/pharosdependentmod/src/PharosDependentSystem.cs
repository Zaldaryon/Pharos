using Vintagestory.API.Common;

namespace PharosDependentMod;

/// <summary>A system whose mod depends on another staged mod.</summary>
public sealed class PharosDependentSystem : ModSystem
{
    public override double ExecuteOrder() => 0.25;
}
