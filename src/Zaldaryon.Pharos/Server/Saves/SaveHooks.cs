using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using Vintagestory.Common.Database;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The database write hooks <see cref="SaveMeter"/> reads. They are installed while a save
/// measurement is open and removed when it closes, and they do nothing without an open measurement.
/// </summary>
internal static class SaveHooks
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.save-profile");
    private static readonly List<MethodBase> s_patched = [];
    private static bool s_installed;

    [ThreadStatic] private static long t_chunks;
    [ThreadStatic] private static long t_mapChunks;
    [ThreadStatic] private static long t_mapRegions;
    [ThreadStatic] private static long t_player;
    [ThreadStatic] private static long t_saveGame;

    private static readonly MethodInfo? s_setChunks = AccessTools.Method(typeof(GameDatabase),
        nameof(GameDatabase.SetChunks), [typeof(IEnumerable<DbChunk>)]);
    private static readonly MethodInfo? s_setMapChunks = AccessTools.Method(typeof(GameDatabase),
        nameof(GameDatabase.SetMapChunks), [typeof(IEnumerable<DbChunk>)]);
    private static readonly MethodInfo? s_setMapRegions = AccessTools.Method(typeof(GameDatabase),
        nameof(GameDatabase.SetMapRegions), [typeof(IEnumerable<DbChunk>)]);
    private static readonly MethodInfo? s_setPlayerData = AccessTools.Method(typeof(GameDatabase),
        nameof(GameDatabase.SetPlayerData), [typeof(string), typeof(byte[])]);
    private static readonly MethodInfo? s_storeSaveGame = AccessTools.Method(typeof(GameDatabase),
        nameof(GameDatabase.StoreSaveGame), [typeof(global::SaveGame), typeof(FastMemoryStream)]);

    internal static void Install(GameDatabase database)
    {
        lock (s_lock)
        {
            if (s_installed) throw new InvalidOperationException("The save hooks are already installed.");

            try
            {
                Patch(s_setChunks, nameof(SetChunksPrefix), nameof(SetChunksPostfix), required: true);
                Patch(s_setMapChunks, nameof(SetMapChunksPrefix), nameof(SetMapChunksPostfix), required: true);
                Patch(s_setMapRegions, nameof(SetMapRegionsPrefix), nameof(SetMapRegionsPostfix), required: true);
                Patch(s_setPlayerData, nameof(SetPlayerDataPrefix), nameof(SetPlayerDataPostfix), required: true);
                Patch(s_storeSaveGame, nameof(StoreSaveGamePrefix), nameof(StoreSaveGamePostfix), required: true);

                object? connection = AccessTools.Field(typeof(GameDatabase), "conn")?.GetValue(database);
                MethodInfo? storeGameData = connection == null
                    ? null
                    : AccessTools.Method(connection.GetType(), "StoreGameData", [typeof(byte[])]);
                Patch(storeGameData, null, nameof(StoreGameDataPostfix), required: false);
            }
            catch
            {
                Remove();
                throw;
            }

            s_installed = true;
        }
    }

    internal static void Remove()
    {
        lock (s_lock)
        {
            foreach (MethodBase method in s_patched)
            {
                s_harmony.Unpatch(method, HarmonyPatchType.All, s_harmony.Id);
            }

            s_patched.Clear();
            s_installed = false;
        }
    }

    private static void Patch(MethodInfo? original, string? prefix, string postfix, bool required)
    {
        if (original == null)
        {
            if (required) throw new MissingMethodException("The game's save path changed; the save measurement cannot hook it.");
            return;
        }

        s_harmony.Patch(original,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(SaveHooks), prefix),
            postfix: new HarmonyMethod(typeof(SaveHooks), postfix));
        s_patched.Add(original);
    }

    // ---- patches ----------------------------------------------------------------------------

    private static void SetChunksPrefix() => t_chunks = Stopwatch.GetTimestamp();

    private static void SetChunksPostfix(IEnumerable<DbChunk>? __0) =>
        Add("chunks", t_chunks, __0);

    private static void SetMapChunksPrefix() => t_mapChunks = Stopwatch.GetTimestamp();

    private static void SetMapChunksPostfix(IEnumerable<DbChunk>? __0) =>
        Add("mapchunks", t_mapChunks, __0);

    private static void SetMapRegionsPrefix() => t_mapRegions = Stopwatch.GetTimestamp();

    private static void SetMapRegionsPostfix(IEnumerable<DbChunk>? __0) =>
        Add("mapregions", t_mapRegions, __0);

    private static void SetPlayerDataPrefix() => t_player = Stopwatch.GetTimestamp();

    private static void SetPlayerDataPostfix(string? __0, byte[]? __1) =>
        SaveMeter.Active?.Add("players", t_player, 1, __1?.LongLength ?? 0);

    private static void StoreSaveGamePrefix() => t_saveGame = Stopwatch.GetTimestamp();

    private static void StoreSaveGamePostfix() =>
        SaveMeter.Active?.Add("savegame", t_saveGame, 1, 0);

    private static void StoreGameDataPostfix(byte[]? __0) =>
        SaveMeter.Active?.AddSaveGameBytes(__0?.LongLength ?? 0);

    private static void Add(string category, long startTicks, IEnumerable<DbChunk>? chunks)
    {
        SaveMeter? meter = SaveMeter.Active;
        if (meter == null || chunks == null) return;

        int count = 0;
        long bytes = 0;
        foreach (DbChunk chunk in chunks)
        {
            count++;
            bytes += chunk.Data?.LongLength ?? 0;
        }

        meter.Add(category, startTicks, count, bytes);
    }
}
