namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    private ServerFaults? _faults;

    /// <summary>
    /// The faults a test injects into the server, such as <see cref="ServerFaults.SlowTicks"/>. A scenario's faults end when it ends.
    /// </summary>
    public ServerFaults Faults
    {
        get
        {
            lock (_clockLock) return _faults ??= new ServerFaults(this);
        }
    }

    /// <summary>Adds <paramref name="ms"/> to the step the next tick takes on the virtual clock; false when no step is pending.</summary>
    internal bool TryStretchNextTick(long ms) => _clockState is { } state && state.TryAddPendingStep(ms);
}
