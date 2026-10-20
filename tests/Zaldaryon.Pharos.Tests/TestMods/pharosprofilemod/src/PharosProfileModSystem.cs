using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PharosProfileMod;

/// <summary>The message the profile mod sends to every player, so a profile has channel traffic to show.</summary>
[ProtoContract]
public class ProfilePing
{
    [ProtoMember(1)]
    public int N { get; set; }
}

/// <summary>
/// Registers tick work a server profile can attribute: a mod listener, a delayed callback, and a
/// block entity that registers its own listener. Also sends on a channel so bandwidth has numbers.
/// </summary>
public sealed class PharosProfileModSystem : ModSystem
{
    /// <summary>The channel the mod sends its pings on.</summary>
    public const string Channel = "pharosprofile";

    private ICoreServerAPI? _api;
    private IServerNetworkChannel? _channel;
    private long _listenerId;
    private int _pings;

    private static double s_sink;

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockEntityClass("PharosProfileBlock", typeof(PharosProfileBlockEntity));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        _channel = api.Network.RegisterChannel(Channel).RegisterMessageType<ProfilePing>();
        _listenerId = api.Event.RegisterGameTickListener(OnTick, 1);
        api.Event.RegisterCallback(OnDelayed, 100);
    }

    public override void Dispose()
    {
        if (_api != null && _listenerId != 0)
        {
            _api.Event.UnregisterGameTickListener(_listenerId);
            _listenerId = 0;
        }
    }

    private void OnTick(float dt)
    {
        Spin(40_000);
        if (_api == null || _channel == null || _pings >= 200) return;
        IPlayer[] players = _api.World.AllOnlinePlayers;
        if (players.Length == 0 || _api.World.ElapsedMilliseconds % 100 >= 50) return;

        _pings++;
        _channel.BroadcastPacket(new ProfilePing { N = _pings }, []);
    }

    private void OnDelayed(float elapsed) => Spin(40_000);

    /// <summary>Work whose cost the profile should see, and which the JIT cannot remove.</summary>
    internal static void Spin(int iterations)
    {
        double sum = 0;
        for (int i = 0; i < iterations; i++) sum += System.Math.Sqrt(i);
        s_sink = sum;
    }
}

/// <summary>A block entity that runs a listener every tick, so the profile has a block entity to attribute.</summary>
public sealed class PharosProfileBlockEntity : BlockEntity
{
    private long _listenerId;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) _listenerId = RegisterGameTickListener(OnTick, 1);
    }

    private void OnTick(float dt) => PharosProfileModSystem.Spin(40_000);

    public override void OnBlockRemoved()
    {
        if (_listenerId != 0)
        {
            UnregisterGameTickListener(_listenerId);
            _listenerId = 0;
        }

        base.OnBlockRemoved();
    }
}
