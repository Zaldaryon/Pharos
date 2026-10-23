using Vintagestory.API.Common;

namespace ExampleMod;

/// <summary>Logs a line on each side, so a test can see the mod ran.</summary>
public sealed class ExampleModSystem : ModSystem
{
    public override void Start(ICoreAPI api) => api.Logger.Notification("Pharos example mod started on the {0}.", api.Side);
}
