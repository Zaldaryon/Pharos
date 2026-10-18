namespace Zaldaryon.Pharos.Scenarios;

/// <summary>
/// What the scenario pipeline asks of a scenario class: implemented by the scenario base classes.
/// </summary>
public interface IScenarioLifecycle
{
    /// <summary>Called to initialize the scenario host and state before each test.</summary>
    Task InitializeAsync();

    /// <summary>Called just before the test body runs, after the class is initialized.</summary>
    void BeforeBody();

    /// <summary>Applies per-test environments under the scenario watchdog.</summary>
    Task ApplyEnvironmentsAsync(CancellationToken cancellationToken);

    /// <summary>Runs registered environment cleanup on the scenario's owning thread.</summary>
    Task CleanupEnvironmentsAsync(CancellationToken cancellationToken);

    /// <summary>Throws when the scenario logged errors it does not allow; called when the body passed.</summary>
    void CheckLoggedErrors();

    /// <summary>
    /// Called when the body ran out of time. <paramref name="stillRunning"/> when it did not stop
    /// after it was aborted: its host must be abandoned, never disposed or reused.
    /// </summary>
    void BodyTimedOut(bool stillRunning);

    /// <summary>
    /// Saves the failure artifacts while the client and server are still up. Returns the text to
    /// add to the failure message, or null to leave the failure as it is.
    /// </summary>
    string? CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut);

    /// <summary>The scenario's embedded server, which fixture commands save the world of, or null.</summary>
    Server.EmbeddedServerHost? FixtureServer { get; }

    /// <summary>Disposes or pools the scenario host after each test.</summary>
    Task DisposeAsync();
}
