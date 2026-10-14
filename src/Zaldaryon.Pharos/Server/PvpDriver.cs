namespace Zaldaryon.Pharos.Server;

/// <summary>Reads and sets whether players can damage each other. This is a server setting, not a world setting.</summary>
public sealed class PvpDriver
{
    private readonly EmbeddedServerHost _host;

    internal PvpDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Whether players can attack and damage each other (<c>IServerConfig.AllowPvP</c>).</summary>
    public bool Enabled
    {
        get => _host.RunOnGameThread(() => _host.Server.Config.AllowPvP);
        set => _host.RunOnGameThread(() => _host.Server.Config.AllowPvP = value);
    }

    /// <summary>Records the setting the server has now, to be put back by <see cref="Baseline.Restore"/>.</summary>
    internal Baseline Capture() => new(this, Enabled);

    /// <summary>The PvP setting the world started with.</summary>
    internal sealed class Baseline
    {
        private readonly PvpDriver _pvp;
        private readonly bool _enabled;

        internal Baseline(PvpDriver pvp, bool enabled)
        {
            _pvp = pvp;
            _enabled = enabled;
        }

        /// <summary>Puts the baseline setting back.</summary>
        public void Restore() => _pvp.Enabled = _enabled;
    }
}
