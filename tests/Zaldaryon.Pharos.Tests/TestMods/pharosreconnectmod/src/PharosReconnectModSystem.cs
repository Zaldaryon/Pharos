using System;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace PharosReconnectMod;

[ProtoContract]
public class Hello
{
    [ProtoMember(1)]
    public int Session { get; set; }
}

/// <summary>
/// Counts what a reconnect must leave exactly once: the client side's starts and disposals, and
/// each kind of handler it registers (a channel, the event bus, a hotkey, a settings watcher).
/// <c>.pharosreconnect counts</c> reports them.
/// </summary>
/// <remarks>
/// The game compiles a source mod again for each game session, so its statics start from zero
/// each time: the counts live in a process-wide slot instead.
/// </remarks>
public sealed class PharosReconnectModSystem : ModSystem
{
    public const string Channel = "pharosreconnect";

    private const int Started = 0, Disposed = 1, Hellos = 2, Pings = 3, Hotkeys = 4, Watched = 5, HotkeyInstance = 6;
    private static readonly string[] Names = new[] { "started", "disposed", "hellos", "pings", "hotkeys", "watched", "hotkeyinstance" };

    private int _instance;
    private bool _client;
    private int _greeted;

    private static int[] Counts()
    {
        lock (Names)
        {
            int[] counts = AppDomain.CurrentDomain.GetData("pharosreconnect:counts") as int[];
            if (counts == null)
            {
                counts = new int[Names.Length];
                AppDomain.CurrentDomain.SetData("pharosreconnect:counts", counts);
            }

            return counts;
        }
    }

    private static int Bump(int which)
    {
        int[] counts = Counts();
        lock (counts) return ++counts[which];
    }

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(Channel).RegisterMessageType<Hello>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.PlayerNowPlaying += player =>
            api.Network.GetChannel(Channel).SendPacket(new Hello { Session = ++_greeted }, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _client = true;
        _instance = Bump(Started);

        api.Network.GetChannel(Channel).SetMessageHandler<Hello>(hello => Bump(Hellos));
        api.Event.RegisterEventBusListener((string eventName, ref EnumHandling handling, IAttribute data) => Bump(Pings), filterByEventName: "pharos:reconnect:ping");
        api.Input.RegisterHotKey("pharosreconnect", "Pharos reconnect test", GlKeys.F10, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("pharosreconnect", combination =>
        {
            Bump(Hotkeys);
            int[] counts = Counts();
            lock (counts) counts[HotkeyInstance] = _instance;
            return true;
        });
        api.Settings.Int.AddWatcher("musicFrequency", value => Bump(Watched));

        api.ChatCommands.Create("pharosreconnect").WithDescription("Pharos reconnect test")
            .BeginSubCommand("counts")
                .HandleWith(args =>
                {
                    int[] counts = Counts();
                    string text;
                    lock (counts)
                    {
                        text = $"instance={_instance} active={counts[Started] - counts[Disposed]}";
                        for (int i = 0; i < Names.Length; i++) text += $" {Names[i]}={counts[i]}";
                    }

                    return TextCommandResult.Success(text);
                })
            .EndSubCommand();
    }

    public override void Dispose()
    {
        if (_client) Bump(Disposed);
    }
}
