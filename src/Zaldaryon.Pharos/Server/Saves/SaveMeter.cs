using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The measurement behind <see cref="ServerSavesDriver"/>: it hooks the game's database writes,
/// runs one full save, and reads the mod data the save carries.
/// </summary>
internal sealed class SaveMeter
{
    private static readonly object s_openLock = new();
    private static SaveMeter? s_active;
    private static bool s_open;

    private static readonly FieldInfo? s_chunkThread = typeof(ServerMain).GetField("chunkThread",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_loadSaveGame = typeof(ChunkServerThread).GetField("loadsavegame",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private readonly EmbeddedServerHost _host;
    private readonly Dictionary<string, Category> _categories = new(StringComparer.Ordinal);
    private readonly List<SaveModDataEntry> _modData = [];

    private long _startTicks;
    private long _wallTicks;
    private bool _saved;

    private SaveMeter(EmbeddedServerHost host) => _host = host;

    /// <summary>The measurement that is open, or null. The database hooks read it.</summary>
    internal static SaveMeter? Active => Volatile.Read(ref s_active);

    /// <summary>Opens a measurement and installs the database hooks; call on the game thread.</summary>
    internal static SaveMeter Open(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (s_openLock)
        {
            if (s_open) throw new InvalidOperationException("A save measurement is already open.");

            ServerMain server = host.Server;
            object? chunkThread = s_chunkThread?.GetValue(server)
                ?? throw new InvalidOperationException("The server has no chunk thread to measure.");
            GameDatabase database = DatabaseOf(chunkThread);
            SaveMeter meter = new(host);
            SaveHooks.Install(database);
            s_open = true;
            Volatile.Write(ref s_active, meter);
            return meter;
        }
    }

    /// <summary>Runs the save and reads the mod data; call on the game thread.</summary>
    internal void Save(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _startTicks = Stopwatch.GetTimestamp();
        InvokeSaveGameWorld();
        _wallTicks = Stopwatch.GetTimestamp() - _startTicks;
        _saved = true;
        ScanModData();
    }

    /// <summary>Removes the hooks and builds the profile; call on the game thread.</summary>
    internal SaveProfile Close()
    {
        if (!_saved) throw new InvalidOperationException("The save measurement has not run a save.");
        SaveHooks.Remove();
        Clear();
        return Build();
    }

    /// <summary>Removes the hooks without building a profile, for a measurement the test abandoned.</summary>
    internal void Abort()
    {
        SaveHooks.Remove();
        Clear();
    }

    internal void Add(string category, long startTicks, int count, long bytes)
    {
        Category values = _categories.TryGetValue(category, out Category? existing)
            ? existing
            : _categories[category] = new Category();
        long elapsed = Stopwatch.GetTimestamp() - startTicks;
        if (elapsed > 0) values.Ticks += elapsed;
        values.Bytes += bytes;
        values.Count += count;
    }

    internal void AddSaveGameBytes(long bytes)
    {
        if (!_categories.TryGetValue("savegame", out Category? values))
        {
            values = _categories["savegame"] = new Category();
        }

        values.Bytes += bytes;
    }

    private void Clear()
    {
        if (ReferenceEquals(Volatile.Read(ref s_active), this)) Volatile.Write(ref s_active, null);
        lock (s_openLock) s_open = false;
    }

    private SaveProfile Build()
    {
        string[] order = ["players", "mapregions", "mapchunks", "chunks", "savegame"];
        List<SaveCategoryProfile> categories = [];
        foreach (string name in order)
        {
            Category values = _categories.TryGetValue(name, out Category? existing) ? existing : new Category();
            categories.Add(new SaveCategoryProfile(name, values.Ticks * 1000.0 / Stopwatch.Frequency,
                values.Bytes, values.Count));
        }

        _modData.Sort((left, right) =>
        {
            int byBytes = right.Bytes.CompareTo(left.Bytes);
            return byBytes != 0 ? byBytes : string.CompareOrdinal(left.Key, right.Key);
        });
        return new SaveProfile(_wallTicks * 1000.0 / Stopwatch.Frequency, categories, [.. _modData]);
    }

    // ---- the save and its data --------------------------------------------------------------

    private void InvokeSaveGameWorld()
    {
        object? chunkThread = s_chunkThread?.GetValue(_host.Server)
            ?? throw new InvalidOperationException("The server has no chunk thread to save.");
        object? system = s_loadSaveGame?.GetValue(chunkThread)
            ?? throw new InvalidOperationException("The server has no save system to measure.");
        MethodInfo? save = system.GetType().GetMethod("SaveGameWorld",
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null);
        if (save == null)
        {
            throw new InvalidOperationException(
                $"The game's save system '{system.GetType().FullName}' has no SaveGameWorld(bool) method.");
        }

        try
        {
            save.Invoke(system, [false]);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            throw new InvalidOperationException("The game's save threw an exception.", e.InnerException);
        }
    }

    private void ScanModData()
    {
        ICoreServerAPI api = _host.Api;
        Dictionary<string, long> byKey = new(StringComparer.Ordinal);

        global::SaveGame saveGame = (global::SaveGame)api.WorldManager.SaveGame;
        AddAll(byKey, saveGame.ModData);
        foreach (IMapRegion region in api.WorldManager.AllLoadedMapRegions.Values)
        {
#pragma warning disable CS0618 // The obsolete dictionary is the only way to enumerate region mod data.
            AddAll(byKey, region.ModData);
#pragma warning restore CS0618
        }

        foreach (IServerChunk chunk in api.WorldManager.AllLoadedChunks.Values)
        {
            if (chunk is WorldChunk worldChunk && worldChunk.ModData != null)
            {
                AddAll(byKey, worldChunk.ModData);
            }
        }

        HashSet<string> mods = new(StringComparer.Ordinal);
        foreach (Mod mod in api.ModLoader.Mods) mods.Add(mod.Info.ModID);

        foreach ((string key, long bytes) in byKey)
        {
            int separator = key.IndexOf(':');
            string? owner = separator > 0 && mods.Contains(key[..separator]) ? key[..separator] : null;
            _modData.Add(new SaveModDataEntry(key, owner, bytes));
        }
    }

    private static void AddAll(Dictionary<string, long> byKey, IReadOnlyDictionary<string, byte[]>? data)
    {
        if (data == null) return;
        foreach ((string key, byte[] bytes) in data)
        {
            byKey[key] = byKey.GetValueOrDefault(key) + (bytes?.LongLength ?? 0);
        }
    }

    private static GameDatabase DatabaseOf(object chunkThread)
    {
        FieldInfo? field = typeof(ChunkServerThread).GetField("gameDatabase",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(chunkThread) as GameDatabase
            ?? throw new InvalidOperationException("The server's chunk thread has no game database.");
    }

    private sealed class Category
    {
        public long Ticks;
        public long Bytes;
        public int Count;
    }
}
