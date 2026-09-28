using Vintagestory.Client;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Running-game screen for headless clients. It preserves native scene handover/rendering,
/// while omitting the launcher's cached-session login navigation.
/// </summary>
internal sealed class HeadlessRunningGameScreen : GuiScreenRunningGame
{
    public HeadlessRunningGameScreen(ScreenManager screenManager)
        : base(screenManager, null)
    {
    }

    public override void OnScreenLoaded()
    {
        // The harness authenticates only against its in-process test server. The base screen
        // checks the separate launcher account cache and would open an uninitialized login UI.
    }
}
