using System.Collections;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// The watchers registered on the process-wide client settings, which a client adds while it
/// boots and the game never removes: it ends the process instead.
/// </summary>
/// <remarks>
/// A watcher left behind by a disposed client still runs when a later client changes that
/// setting, against a client that no longer has a world, and throws. A boot records the watchers
/// already there, and the dispose removes the ones the client added.
/// </remarks>
internal static class ClientSettingsWatchers
{
    /// <summary>The watchers registered right now.</summary>
    public static HashSet<object> Snapshot() =>
        new(Lists().SelectMany(list => list.Cast<object>()), ReferenceEqualityComparer.Instance);

    /// <summary>Removes every watcher that <paramref name="before"/> does not hold.</summary>
    public static void RemoveAddedSince(HashSet<object> before)
    {
        foreach (IList watchers in Lists())
        {
            for (int i = watchers.Count - 1; i >= 0; i--)
            {
                if (!before.Contains(watchers[i]!)) watchers.RemoveAt(i);
            }
        }
    }

    private static IEnumerable<IList> Lists()
    {
        ClientSettings? settings = ClientSettings.Inst;
        if (settings == null) yield break;

        if (settings.Bool is SettingsClass<bool> bools) yield return bools.Watchers;
        if (settings.Int is SettingsClass<int> ints) yield return ints.Watchers;
        if (settings.Float is SettingsClass<float> floats) yield return floats.Watchers;
        if (settings.String is SettingsClass<string> strings) yield return strings.Watchers;
        if (settings.Strings is SettingsClass<List<string>> stringLists) yield return stringLists.Watchers;
    }
}
