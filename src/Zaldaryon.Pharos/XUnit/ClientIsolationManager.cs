using System;
using System.Diagnostics;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Manages client isolation between tests based on the configured isolation mode.
/// </summary>
public sealed class ClientIsolationManager
{
    private IsolationContext? _savedContext;
    private bool _requiresFreshClient;

    /// <summary>
    /// Initializes a new ClientIsolationManager with the specified isolation mode.
    /// </summary>
    public ClientIsolationManager(IsolationMode mode)
    {
        Mode = mode;
    }

    /// <summary>
    /// The isolation mode this manager operates under.
    /// </summary>
    public IsolationMode Mode { get; }

    /// <summary>
    /// The currently active client, if any.
    /// </summary>
    public HeadlessClient? ActiveClient { get; private set; }

    /// <summary>
    /// Whether a fresh client is required for the next test (FreshClient mode only).
    /// </summary>
    public bool RequiresFreshClient => _requiresFreshClient;

    /// <summary>
    /// Prepares the client for a test based on the isolation mode.
    /// </summary>
    /// <param name="client">The headless client instance, or null in unit test scaffolds.</param>
    public void PrepareForTest(HeadlessClient? client)
    {
        ActiveClient = client;

        switch (Mode)
        {
            case IsolationMode.SharedClient:
                // No-op: tests share state freely
                break;

            case IsolationMode.RollbackState:
                if (client is not null && _savedContext is not null)
                {
                    RestoreState(client, _savedContext);
                }
                break;

            case IsolationMode.FreshClient:
                // In unit tests, we record the need but do not actually reboot.
                // Integration tests check RequiresFreshClient and handle it.
                _requiresFreshClient = true;
                break;
        }
    }

    /// <summary>
    /// Captures the current player and camera state from the client.
    /// </summary>
    /// <param name="client">The headless client, or null.</param>
    /// <returns>An IsolationContext with captured state, or null if the client or player is unavailable.</returns>
    public IsolationContext? CaptureState(HeadlessClient? client)
    {
        if (client?.TestPlayer is null)
            return null;

        var context = IsolationContext.Capture(client.TestPlayer, client.FrameController);
        if (context is not null)
        {
            _savedContext = context;
        }
        return context;
    }

    /// <summary>
    /// Restores previously captured state to the client.
    /// </summary>
    /// <param name="client">The headless client. Must not be null.</param>
    /// <param name="context">The isolation context to restore. Must not be null.</param>
    public void RestoreState(HeadlessClient client, IsolationContext context)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(context);

        var player = client.TestPlayer;
        if (player is null || !player.IsAvailable)
            return;

        // Restore player position
        player.Position = context.SavedPlayerPosition.Clone();

        // Restore camera state
        player.Camera.Position = context.SavedCameraPosition.Clone();
        player.Camera.Yaw = context.SavedCameraYaw;
        player.Camera.Pitch = context.SavedCameraPitch;
    }

    /// <summary>
    /// Measures the overhead of an isolation action.
    /// </summary>
    /// <param name="isolationAction">The action to measure.</param>
    /// <returns>An IsolationOverhead containing the measured duration and current mode.</returns>
    public IsolationOverhead MeasureOverhead(Action isolationAction)
    {
        ArgumentNullException.ThrowIfNull(isolationAction);

        var sw = Stopwatch.StartNew();
        isolationAction();
        sw.Stop();

        return new IsolationOverhead
        {
            Duration = sw.Elapsed,
            Mode = Mode
        };
    }

    /// <summary>
    /// Clears the fresh client requirement flag after a fresh client has been provided.
    /// </summary>
    public void ClearFreshClientRequirement()
    {
        _requiresFreshClient = false;
    }
}
