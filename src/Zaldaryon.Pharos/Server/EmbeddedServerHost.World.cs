using System.Reflection;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    private static readonly FieldInfo? s_chunkThread = typeof(ServerMain).GetField("chunkThread", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_gameDatabase = typeof(ChunkServerThread).GetField("gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_backupInProgress = typeof(ChunkServerThread).GetField("BackupInProgress", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_loadSaveGame = typeof(ChunkServerThread).GetField("loadsavegame", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private ICoreServerAPI ServerApi => (ICoreServerAPI)Server.Api;

    /// <summary>
    /// Stamps a WorldEdit schematic (a <c>.json</c> export) into the world, as the WorldEdit
    /// import does, ticking the server until the chunks under it are loaded.
    /// </summary>
    /// <param name="path">The schematic, relative to the working folder or to the test assembly's folder.</param>
    /// <param name="origin">Where the schematic goes; see <see cref="SchematicOptions.Origin"/>.</param>
    /// <param name="options">How it is placed; the defaults place it as exported, with its lowest corner at <paramref name="origin"/>.</param>
    /// <param name="ct">Cancels the wait for the chunks.</param>
    /// <remarks>
    /// The chunk columns under the box are kept loaded for the life of the server, so they are not
    /// unloaded mid-test, out of the world rollback's reach.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The schematic does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a schematic, or is too large.</exception>
    /// <exception cref="InvalidOperationException">The schematic uses blocks the game does not know.</exception>
    /// <exception cref="TimeoutException">The chunks under the box did not load in time.</exception>
    public async Task<SchematicPlacement> PlaceSchematicAsync(string path, BlockPos origin, SchematicOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        SchematicPlacer placer = SchematicPlacer.Load(path, options);
        return await PlaceSchematicCoreAsync(placer, origin, options?.MaxTicks ?? new SchematicOptions().MaxTicks, readBack: false, (done, max) => TickUntilAsync(done, max, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every chunk between <paramref name="min"/> and <paramref name="max"/> and keeps their
    /// columns loaded for the life of the server, ticking it until they are there.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The box leaves the world, or covers more than 1024 chunk columns.</exception>
    /// <exception cref="TimeoutException">The chunks did not load within <paramref name="maxTicks"/> ticks.</exception>
    public async Task<IReadOnlyList<ChunkPos>> EnsureChunksLoadedAsync(BlockPos min, BlockPos max, int maxTicks = 3000, CancellationToken ct = default)
    {
        IReadOnlyList<ChunkPos> chunks = CheckedRegion(min, max);
        await LoadChunksCoreAsync(chunks, maxTicks, (done, limit) => TickUntilAsync(done, limit, ct)).ConfigureAwait(false);
        return chunks;
    }

    // The chunks of a box inside the world.
    internal IReadOnlyList<ChunkPos> CheckedRegion(BlockPos min, BlockPos max)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        RunOnGameThread(() => ChunkRegion.CheckInWorld(ServerApi, min, max));
        return ChunkRegion.Covering(min, max);
    }

    // Prepares on the game thread, lets whoever ticks the server load the chunks, then places.
    internal async Task<SchematicPlacement> PlaceSchematicCoreAsync(SchematicPlacer placer, BlockPos origin, int maxTicks, bool readBack, Func<Func<bool>, int, Task<bool>> advanceUntil)
    {
        RunOnGameThread(() => placer.Prepare(ServerApi, origin));
        await LoadChunksCoreAsync(placer.Chunks, maxTicks, advanceUntil).ConfigureAwait(false);
        return RunOnGameThread(() => placer.Place(ServerApi, readBack));
    }

    internal async Task LoadChunksCoreAsync(IReadOnlyList<ChunkPos> chunks, int maxTicks, Func<Func<bool>, int, Task<bool>> advanceUntil)
    {
        if (maxTicks <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");
        RunOnGameThread(() => ChunkRegion.Request(ServerApi, chunks));
        bool loaded = await advanceUntil(() => RunOnGameThread(() => ChunkRegion.Missing(ServerApi, chunks).Count == 0), maxTicks).ConfigureAwait(false);
        if (!loaded) throw ChunkRegion.Timeout(RunOnGameThread(() => ChunkRegion.Missing(ServerApi, chunks)), maxTicks);
    }

    /// <summary>
    /// Saves the world as it is now and writes a copy of the save to <paramref name="destination"/>,
    /// ready to boot from with <see cref="ServerWorldOptions.SaveFile"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the game's autosave does, made synchronous: the server is paused, mods and
    /// systems are told the world is being saved, the chunks are written, and then the save is
    /// copied with SQLite's online backup. The copy is written next to <paramref name="destination"/>
    /// and moved over it, so a failed save never leaves half a file there.
    /// </para>
    /// <para>
    /// As with an autosave, pausing drops the queue of chunks still being generated; they are
    /// queued again when something asks for them. The save runs to the end on the game thread
    /// before the returned task completes; <paramref name="ct"/> is only checked before it starts.
    /// Like an autosave, it stops the server when the disk has less free space than the server's
    /// <c>DieBelowDiskSpaceMb</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The server is not running, could not be paused, or is writing a backup.</exception>
    public Task SaveWorldAsync(string destination, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ct.ThrowIfCancellationRequested();
        if (!IsRunning) throw new InvalidOperationException("Cannot save the world of a stopped server.");

        string full = Path.GetFullPath(destination);
        RunOnGameThread(() => SaveWorldCore(full));
        return Task.CompletedTask;
    }

    private void SaveWorldCore(string destination)
    {
        object chunkThread = s_chunkThread?.GetValue(Server) ?? throw new InvalidOperationException("The server has no chunk thread to save with.");
        GameDatabase database = s_gameDatabase?.GetValue(chunkThread) as GameDatabase ?? throw new InvalidOperationException("The server has no open world database.");
        ServerSystem saver = s_loadSaveGame?.GetValue(chunkThread) as ServerSystem ?? throw new InvalidOperationException("The server has no save system.");
        if (s_backupInProgress?.GetValue(chunkThread) is true)
        {
            throw new InvalidOperationException("The server is writing a backup (/genbackup); save the world once it is done.");
        }

        if (string.Equals(Path.GetFullPath(database.DatabaseFilename), destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("The destination is the running server's own save.", nameof(destination));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".pharos-" + Guid.NewGuid().ToString("N")[..8];

        // As the autosave does: the chunk thread must be still while the save and the copy use its connection.
        if (!Server.Suspend(true, 10000)) throw new InvalidOperationException("The server could not be paused to save the world.");
        try
        {
            if (Server.Saving) throw new InvalidOperationException("The server is already saving.");
            Server.Saving = true;
            try
            {
                // An off-thread save already queued would make the game skip this one; finish it first.
                saver.OnSeparateThreadTick();
                Server.EventManager.TriggerGameWorldBeingSaved();

                // The chunks the save left to the chunk thread, written here while it is paused.
                saver.OnSeparateThreadTick();
            }
            finally
            {
                Server.Saving = false;
            }

            database.CreateBackup(temp);
            if (!File.Exists(temp)) throw new InvalidOperationException($"The game did not write the copy of the save; see the server log.");
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            foreach (string leftover in new[] { temp, temp + "-wal", temp + "-shm", temp + "-journal" })
            {
                if (File.Exists(leftover)) File.Delete(leftover);
            }

            Server.Suspend(false);
        }
    }

    /// <summary>
    /// Checks <see cref="ServerWorldOptions.SaveFile"/> before anything boots, and returns its full
    /// path, or null when there is none.
    /// </summary>
    internal static string? CheckSaveFile(ServerWorldOptions? options)
    {
        if (options?.SaveFile == null) return null;
        if (options.SaveFileLocation != null)
        {
            throw new ArgumentException("Set either SaveFile, a save to start from, or SaveFileLocation, where the server keeps its save, not both.", nameof(options));
        }

        string source = DataFileSet.ResolveFixture(options.SaveFile, "save file");
        if (File.Exists(source + "-wal"))
        {
            throw new InvalidDataException(
                $"The save {source} has a write-ahead log next to it ({source}-wal): it was copied out of a running game and misses what the log holds. " +
                "Save it again with the game closed, or with EmbeddedServerHost.SaveWorldAsync.");
        }

        return source;
    }

    /// <summary>
    /// Copies <see cref="ServerWorldOptions.SaveFile"/> to where the server opens its save, so the
    /// server boots into a copy and the fixture itself never changes.
    /// </summary>
    private static void StageSaveFile(ServerWorldOptions options, string saveLocation, bool mayReplace)
    {
        if (options.SaveFile == null) return;
        string source = CheckSaveFile(options)!;

        if (!mayReplace && File.Exists(saveLocation))
        {
            throw new InvalidOperationException($"Booting from {source} would replace the save already at {saveLocation}. Use a fresh data folder.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(saveLocation)!);
        File.Copy(source, saveLocation, overwrite: true);
        foreach (string stale in new[] { saveLocation + "-wal", saveLocation + "-shm" })
        {
            if (File.Exists(stale)) File.Delete(stale);
        }
    }
}
