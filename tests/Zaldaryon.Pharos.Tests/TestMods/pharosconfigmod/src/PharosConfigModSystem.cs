using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PharosConfigMod;

/// <summary>What the mod reads from its config.</summary>
public sealed class PharosConfig
{
    public string Label { get; set; } = "none";

    public int Port { get; set; }
}

/// <summary>
/// Reads <c>ModConfig/pharosconfig.json</c> on each side when it starts. <c>/pharosconfig</c> and
/// <c>.pharosconfig</c> tell what it read; <c>reload</c> reads the file again and <c>set</c> writes it.
/// </summary>
public sealed class PharosConfigModSystem : ModSystem
{
    private const string File = "pharosconfig.json";

    private PharosConfig _started = new();

    public override void StartServerSide(ICoreServerAPI api)
    {
        _started = api.LoadModConfig<PharosConfig>(File) ?? new PharosConfig();
        IChatCommand command = api.ChatCommands.Create("pharosconfig").WithDescription("Pharos config test").RequiresPrivilege(Privilege.controlserver);
        Register(api, command);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _started = api.LoadModConfig<PharosConfig>(File) ?? new PharosConfig();
        IChatCommand command = api.ChatCommands.Create("pharosconfig").WithDescription("Pharos config test");
        Register(api, command);
    }

    private void Register(ICoreAPI api, IChatCommand command)
    {
        command.HandleWith(_ => TextCommandResult.Success($"label={_started.Label} port={_started.Port}"));

        command.BeginSubCommand("reload")
            .HandleWith(_ =>
            {
                PharosConfig now = api.LoadModConfig<PharosConfig>(File) ?? new PharosConfig();
                return TextCommandResult.Success($"label={now.Label} port={now.Port}");
            })
            .EndSubCommand();

        command.BeginSubCommand("set")
            .WithArgs(api.ChatCommands.Parsers.Word("label"))
            .HandleWith(args =>
            {
                api.StoreModConfig(new PharosConfig { Label = (string)args[0], Port = _started.Port }, File);
                return TextCommandResult.Success();
            })
            .EndSubCommand();
    }
}
