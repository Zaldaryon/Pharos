using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Abstract base class for client scenario tests.
/// Test classes inheriting from this should use <see cref="IClassFixture{ClientFixture}"/>
/// to receive the shared or per-class client instance.
/// </summary>
public abstract class ClientScenarioBase
{
    /// <summary>
    /// The headless client instance managed by the fixture.
    /// Null until set by the fixture via <see cref="SetClient"/>.
    /// </summary>
    protected HeadlessClient? Client { get; private set; }

    /// <summary>
    /// Test player abstraction for controlling and querying the player entity.
    /// </summary>
    protected IClientTestPlayer? Player => Client?.TestPlayer;

    /// <summary>
    /// Frame controller for deterministic frame stepping.
    /// </summary>
    protected DeterministicFrameController? FrameController => Client?.FrameController;

    /// <summary>
    /// The isolation mode used for tests in this class.
    /// Override in derived classes to change isolation behavior.
    /// </summary>
    protected virtual IsolationMode IsolationMode => IsolationMode.SharedClient;

    /// <summary>
    /// Sets the client instance. Called by the fixture during initialization.
    /// </summary>
    internal void SetClient(HeadlessClient client)
    {
        Client = client;
    }
}
