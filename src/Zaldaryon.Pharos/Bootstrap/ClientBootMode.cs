namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// How much of the vanilla client startup <see cref="HeadlessClientBootstrap"/> runs.
/// </summary>
public enum ClientBootMode
{
    /// <summary>
    /// Constructs <c>ClientMain</c> without starting the engine: no worker threads, no network
    /// processing, no mod load. Chunks are injected through fixtures and every frame is
    /// deterministic. This is the mode the chunk, mesh and culling inspectors were written for.
    /// </summary>
    Fixture,

    /// <summary>
    /// Runs the vanilla client startup (<c>ScreenManager</c>, platform frame buffers,
    /// <c>ClientMain.Start</c>) so the client can join a real server, load its mods and assets,
    /// spawn its player and render through the vanilla <c>ScreenManager</c> pipeline. The engine
    /// keeps its own worker threads, so timing is real rather than deterministic.
    /// </summary>
    Engine,
}
