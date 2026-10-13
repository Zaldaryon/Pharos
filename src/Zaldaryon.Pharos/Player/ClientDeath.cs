using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// The client's side of dying and coming back: whether the death screen is up, and the Respawn
/// button on it.
/// </summary>
/// <remarks>
/// The game opens <c>GuiDialogDead</c> itself, two and a half game seconds after the player stops
/// being alive, and closes it again as soon as the player is alive. <see cref="Respawn"/> clicks
/// Respawn on it with a real mouse click, which is what a player does, so the whole path runs:
/// the dialog, the button and the respawn packet. A client with no GUI driver, such as one booted
/// without an engine, sends the packet the button sends instead.
/// </remarks>
public sealed class ClientDeath
{
    private const string DialogName = nameof(GuiDialogDead);
    private const string RespawnButton = "respawnbtn";

    private readonly System.Func<ClientMain> _game;
    private System.Func<UI.GuiDriver?>? _ui;

    internal ClientDeath(System.Func<ClientMain> game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
    }

    /// <summary>Whether the death screen is open. It opens 2.5 game seconds after the player dies.</summary>
    public bool IsDialogOpen
    {
        get
        {
            ClientMain client = _game();
            return client.api?.Gui?.LoadedGuis?.OfType<GuiDialogDead>().Any(dialog => dialog.IsOpened()) == true;
        }
    }

    /// <summary>The death screen, whether or not it is open, or null when the world loads none.</summary>
    public GuiDialogDead? Dialog =>
        _game().api?.Gui?.LoadedGuis?.OfType<GuiDialogDead>().FirstOrDefault();

    /// <summary>
    /// Respawns, by clicking Respawn on the death screen when it is open.
    /// </summary>
    /// <returns>
    /// False when the player is not dead, or when the world leaves the button out, which is what a
    /// world with a limited number of lives does once they are spent.
    /// </returns>
    /// <exception cref="InvalidOperationException">The death screen is open but has no Respawn button.</exception>
    public bool Respawn()
    {
        ClientMain client = _game();
        if (client.EntityPlayer is not { } entity || entity.Alive) return false;

        if (IsDialogOpen)
        {
            if (_ui?.Invoke() is not { } ui) throw new InvalidOperationException("The death screen is open but this client has no GUI driver, so its Respawn button cannot be clicked. Boot with ClientBootMode.Engine, or call game.Respawn() by hand.");

            ui.Click(DialogName, RespawnButton);
            return true;
        }

        // No screen yet: send what the button sends, so a test that does not wait for the dialog can
        // still come back.
        client.Respawn();
        return true;
    }

    /// <summary>
    /// Points the death screen at the client's GUI driver, once the client has built one. A
    /// headless client never does, and sends the packet instead.
    /// </summary>
    internal ClientDeath UseGuiDriver(System.Func<UI.GuiDriver?> ui)
    {
        _ui = ui;
        return this;
    }
}