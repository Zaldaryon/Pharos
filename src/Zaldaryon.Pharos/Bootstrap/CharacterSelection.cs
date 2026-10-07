using System.Collections;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Walks an engine-mode client through the survival mod's "create character" dialog, the way a
/// player does: confirm the appearance, pick a class, confirm the class.
/// </summary>
/// <remarks>
/// <para>
/// A player the server has not seen before must create a character before the client tells the
/// server it is ready. The survival mod's <c>CharacterSystem</c> blocks <c>IsPlayerReady</c> and
/// opens the dialog instead, so an engine-mode client that leaves the dialog alone stays in the
/// connected state forever: the server never marks it as playing and never sends it chunks.
/// </para>
/// <para>
/// Each call does at most one step, through the dialog's own button handlers ("Confirm Skin",
/// the class arrows, "Confirm Class"), one step per frame. The dialog's close handler then sends
/// the selection, so the server receives the same packet and runs the same validation as for a
/// real player. The appearance keeps whatever the dialog shows when it opens.
/// </para>
/// <para>
/// Everything is looked up by name: worlds without the survival mod, or a mod that replaced the
/// dialog, are left alone.
/// </para>
/// </remarks>
internal static class CharacterSelection
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string CharacterSystemTypeName = "Vintagestory.GameContent.CharacterSystem";

    /// <summary>
    /// Advances the dialog by one step toward <paramref name="characterClass"/>. Returns whether a
    /// step was taken.
    /// </summary>
    /// <exception cref="InvalidOperationException">No character class has the requested code.</exception>
    public static bool Advance(ClientMain client, string characterClass)
    {
        if (client.clientPlayingFired || client.EntityPlayer == null) return false;

        ModSystem? system = client.api.ModLoader.GetModSystem(CharacterSystemTypeName);
        if (system == null) return false;

        Type systemType = system.GetType();
        if (systemType.GetField("didSelect", AnyInstance)?.GetValue(system) is true) return false;

        // The system creates and opens the dialog when the own player joins.
        if (systemType.GetField("createCharDlg", AnyInstance)?.GetValue(system) is not GuiDialog dialog || !dialog.IsOpened())
        {
            return false;
        }

        Type dialogType = dialog.GetType();
        int tab = dialogType.GetField("curTab", AnyInstance)?.GetValue(dialog) as int? ?? 0;

        if (tab == 0)
        {
            // "Confirm Skin". Moves to the class tab, or finishes when classes are disabled.
            return Invoke(dialog, "OnNext");
        }

        if (systemType.GetField("characterClasses", AnyInstance)?.GetValue(system) is not IList classes || classes.Count == 0)
        {
            return false;
        }

        int index = dialogType.GetField("currentClassIndex", AnyInstance)?.GetValue(dialog) as int? ?? 0;
        if (!string.Equals(ClassCode(classes[index]), characterClass, StringComparison.Ordinal))
        {
            if (!classes.Cast<object>().Any(c => string.Equals(ClassCode(c), characterClass, StringComparison.Ordinal)))
            {
                string known = string.Join(", ", classes.Cast<object>().Select(ClassCode));
                throw new InvalidOperationException($"No character class '{characterClass}'. Known classes: {known}.");
            }

            // The right arrow.
            return Invoke(dialog, "changeClass", 1);
        }

        // "Confirm Class".
        return Invoke(dialog, "OnConfirm");
    }

    private static string? ClassCode(object? characterClass) =>
        characterClass?.GetType().GetField("Code")?.GetValue(characterClass) as string
        ?? characterClass?.GetType().GetProperty("Code")?.GetValue(characterClass) as string;

    private static bool Invoke(object dialog, string method, params object[] args)
    {
        MethodInfo? handler = dialog.GetType().GetMethod(method, AnyInstance);
        if (handler == null) return false;

        handler.Invoke(dialog, args);
        return true;
    }
}
