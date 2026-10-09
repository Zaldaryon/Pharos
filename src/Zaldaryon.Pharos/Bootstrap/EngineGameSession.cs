using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Ends an engine-mode client's game session and starts the next one in the same window, the way
/// the game's own reconnect does, without the menus Pharos never builds. The window, GL context,
/// frame buffers, shaders, screen manager and every driver stay.
/// </summary>
internal static class EngineGameSession
{
    /// <summary>
    /// Ends the client's game: leaves the world, stops and joins its threads, disposes it, and
    /// disposes the screens it leaves behind. Runs on the client thread.
    /// </summary>
    public static void End(HeadlessClient client)
    {
        ClientMain game = client.Client;
        GuiScreenRunningGame screen = client.RunningGameScreen;
        try
        {
            EndGame(client, game, screen);
        }
        finally
        {
            // Disposing the game cleared every watcher of the logger the next game shares.
            client.Logs.Reattach(client.Platform.Logger);
        }
    }

    private static void EndGame(HeadlessClient client, ClientMain game, GuiScreenRunningGame screen)
    {
        // What GuiScreenRunningGame.ExitOrRedirect does, minus the screen it loads next. It does
        // nothing more for a game that a kick or a lost connection already ended.
        if (!game.disposed)
        {
            game.MouseGrabbed = false;
            game.DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit);
        }

        // The game waits only 200 ms for its threads. Its network thread reads the socket the
        // next game's would, so it must be gone before anything is wired.
        HeadlessClient.StopClientThreads(game);
        if (!game.disposed) game.Dispose();

        client.Platform.ResetGamePauseAndUptimeState();
        EngineClientStartup.ReleaseGame(screen);

        // A frame that saw the kick already loaded the disconnected screen.
        if (EngineClientStartup.CurrentScreen(client.ScreenManager) is { } shown && !ReferenceEquals(shown, screen))
        {
            shown.Dispose();
        }

        screen.Dispose();
    }

    /// <summary>
    /// Starts a new game for the client and connects it as <paramref name="playerName"/> over
    /// <paramref name="tcpNetwork"/> and <paramref name="udpNetwork"/>. Runs on the client thread.
    /// </summary>
    public static (LoopbackLink Link, LinkedTcpNetClient Tcp, LinkedUdpNetClient Udp) Start(
        HeadlessClient client, string playerName, DummyNetwork tcpNetwork, DummyNetwork udpNetwork)
    {
        // Work the old game queued for the main thread belongs to it.
        lock (ScreenManager.MainThreadTasks)
        {
            ScreenManager.MainThreadTasks.Clear();
        }

        // Process-wide, so another client may have changed them since.
        ClientSettings.PlayerName = playerName;
        ClientSettings.PlayerUID = "pharos-" + playerName.ToLowerInvariant();

        (ClientMain game, GuiScreenRunningGame screen) = EngineClientStartup.StartGameSession(client.ScreenManager);
        client.BeginGameSession(game, screen);

        game.IsSingleplayer = false;
        game.Connectdata = new ServerConnectData { Host = "localhost", Port = 42424 };
        var wired = client.WireEngineLoopback(tcpNetwork, udpNetwork);
        game.Connect();
        return wired;
    }
}
