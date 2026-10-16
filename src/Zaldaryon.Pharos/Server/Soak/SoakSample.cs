namespace Zaldaryon.Pharos.Server;

/// <summary>
/// One reading of a soak run, taken on the game thread between two ticks.
/// </summary>
/// <param name="Tick">The ticks the run has processed when the reading was taken.</param>
/// <param name="TotalDays">The calendar's total days when the reading was taken.</param>
/// <param name="Simulated">The game time the run has covered so far, measured on the calendar.</param>
/// <param name="Wall">The wall-clock time the run has taken so far.</param>
/// <param name="TickP50">The median tick time over every tick so far, or null before the first tick.</param>
/// <param name="TickP99">The 99th percentile tick time over every tick so far, or null before the first tick.</param>
/// <param name="TickMax">The longest tick so far, or null before the first tick.</param>
/// <param name="ManagedBytes">The managed heap size, without a collection.</param>
/// <param name="ManagedBytesAfterGc">The managed heap size after a forced compacting collection, at checkpoints only.</param>
/// <param name="Entities">The loaded entities.</param>
/// <param name="LoadedChunks">The loaded chunks.</param>
/// <param name="TickListeners">The registered tick listeners, server side.</param>
/// <param name="Callbacks">The registered callbacks, server side.</param>
/// <param name="RandomTickPasses">The random block tick passes the background thread has finished.</param>
/// <param name="Checkpoint">Whether this reading is a checkpoint, where the run also compares stability and memory.</param>
public sealed record SoakSample(
    long Tick,
    double TotalDays,
    TimeSpan Simulated,
    TimeSpan Wall,
    TimeSpan? TickP50,
    TimeSpan? TickP99,
    TimeSpan? TickMax,
    long ManagedBytes,
    long? ManagedBytesAfterGc,
    int Entities,
    int LoadedChunks,
    int TickListeners,
    int Callbacks,
    long RandomTickPasses,
    bool Checkpoint);
