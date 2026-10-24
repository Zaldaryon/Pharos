namespace Zaldaryon.Pharos.UI;

/// <summary>Visible HUD layouts, with the text and bounds the client actually displays.</summary>
/// <param name="Dialogs">HUD dialogs in draw order, captured on the client thread.</param>
public sealed record HudReadout(IReadOnlyList<DialogLayout> Dialogs);
