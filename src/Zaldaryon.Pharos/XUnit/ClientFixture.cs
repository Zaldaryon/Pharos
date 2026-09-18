using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// xUnit class fixture for managing HeadlessClient lifecycle.
/// Use with IClassFixture&lt;ClientFixture&gt; to share a client across tests in a class.
/// </summary>
/// <remarks>
/// In unit tests without a live GL context, <see cref="EnsureInitialized"/> acts as a scaffold
/// that records options and sets <see cref="IsInitialized"/> to true without booting the client.
/// The actual client creation requires <see cref="HeadlessClientBootstrap.Create"/> which needs
/// a GL context; real client injection happens in integration test environments.
/// </remarks>
public sealed class ClientFixture : IDisposable
{
    /// <summary>
    /// The headless client instance. Null until the fixture is initialized in a live test environment.
    /// </summary>
    public HeadlessClient? Client { get; private set; }

    /// <summary>
    /// Whether <see cref="EnsureInitialized"/> has been called.
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// The options passed to <see cref="EnsureInitialized"/>, if any.
    /// </summary>
    public HeadlessClientOptions? Options { get; private set; }

    /// <summary>
    /// Ensures the fixture is initialized. Idempotent: does nothing if already initialized.
    /// </summary>
    /// <remarks>
    /// This is a scaffold method. It stores the options and sets <see cref="IsInitialized"/>
    /// to true, but does not boot a real client (which requires a GL context).
    /// </remarks>
    public void EnsureInitialized(HeadlessClientOptions? options = null)
    {
        if (IsInitialized) return;
        Options = options;
        IsInitialized = true;
    }

    /// <summary>
    /// Disposes the client if one was created.
    /// </summary>
    public void Dispose()
    {
        Client?.Dispose();
        Client = null;
    }
}
