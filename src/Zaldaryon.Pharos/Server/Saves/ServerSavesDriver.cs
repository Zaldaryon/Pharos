namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Runs a save and reports what each part of it wrote and cost. See
/// <see cref="EmbeddedServerHost.Saves"/>.
/// </summary>
/// <remarks>
/// The measurement runs the game's full save path synchronously on the game thread, not its
/// off-thread autosave, so the profile is complete when the call returns. Only dirty chunks, map
/// chunks and map regions are written, as in a normal save; change the world first if the test
/// needs a category to have work in it.
/// </remarks>
public sealed class ServerSavesDriver
{
    private readonly EmbeddedServerHost _host;

    internal ServerSavesDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Runs one save and returns the profile.</summary>
    /// <param name="ct">Cancels before the save starts; a save that started runs to the end.</param>
    /// <exception cref="InvalidOperationException">Another save measurement is open, or the game's save path changed.</exception>
    public Task<SaveProfile> MeasureAsync(CancellationToken ct = default) =>
        Task.Run(() => Measure(ct), ct);

    /// <summary>Runs one save on the calling thread and returns the profile.</summary>
    /// <param name="ct">Cancels before the save starts; a save that started runs to the end.</param>
    /// <exception cref="InvalidOperationException">Another save measurement is open, or the game's save path changed.</exception>
    public SaveProfile Measure(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SaveMeter meter = _host.RunOnGameThread(() => SaveMeter.Open(_host));
        try
        {
            _host.RunOnGameThread(() => meter.Save(ct));
            return _host.RunOnGameThread(meter.Close);
        }
        catch
        {
            _host.RunOnGameThread(meter.Abort);
            throw;
        }
    }
}
