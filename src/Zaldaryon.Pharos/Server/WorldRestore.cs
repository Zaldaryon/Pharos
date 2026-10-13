namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Which keys a <see cref="WorldSnapshot"/> restore puts back. A key the filter refuses keeps its
/// live value, whether or not the snapshot had it.
/// </summary>
/// <param name="ModData">
/// Whether to put back the mod data stored under a key: in the save game
/// (<c>SaveGame.StoreData</c>), in a map region, or in a connected player's data
/// (<c>IServerPlayer.SetModdata</c>). Null puts back every key.
/// </param>
/// <param name="PlayerAttribute">
/// Whether to put back a top-level key of a connected player's entity <c>WatchedAttributes</c> or
/// <c>Attributes</c>, such as <c>health</c> or <c>hunger</c>. Null puts back every key.
/// </param>
/// <remarks>The filters run on the server's game thread, during the restore.</remarks>
public sealed record WorldRestoreFilter(Func<string, bool>? ModData = null, Func<string, bool>? PlayerAttribute = null)
{
    /// <summary>A filter that puts back every key.</summary>
    public static WorldRestoreFilter All { get; } = new();

    internal bool RestoresModData(string key) => ModData?.Invoke(key) ?? true;

    internal bool RestoresPlayerAttribute(string key) => PlayerAttribute?.Invoke(key) ?? true;
}

/// <summary>What a <see cref="WorldSnapshot"/> restore put back.</summary>
/// <param name="ChunksRestored">How many loaded chunks had changed and were restored.</param>
/// <param name="PlayersRestored">
/// How many connected players, connected when the snapshot was taken too, had their entity state,
/// player data and inventories put back, whether or not any of it had changed.
/// </param>
/// <param name="ModDataKeysRestored">How many save-game mod data keys were added back, changed back or removed.</param>
/// <param name="MapRegionsRestored">How many loaded map regions had their mod data put back.</param>
public sealed record WorldRestoreResult(int ChunksRestored, int PlayersRestored, int ModDataKeysRestored, int MapRegionsRestored);
