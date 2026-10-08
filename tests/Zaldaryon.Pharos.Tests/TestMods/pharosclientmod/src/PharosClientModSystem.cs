using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PharosClientMod;

/// <summary>Registers <c>.pharoscmd</c>, a client command with one subcommand per outcome.</summary>
public sealed class PharosClientModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        IChatCommand command = api.ChatCommands.Create("pharoscmd").WithDescription("Pharos client command test");

        command.BeginSubCommand("status")
            .HandleWith(_ => TextCommandResult.Success("ready"))
            .EndSubCommand();

        command.BeginSubCommand("fail")
            .HandleWith(_ => TextCommandResult.Error("pharoscmd failed on purpose", "pharosfail"))
            .EndSubCommand();

        command.BeginSubCommand("say")
            .WithArgs(api.ChatCommands.Parsers.All("text"))
            .HandleWith(args =>
            {
                api.ShowChatMessage((string)args[0]);
                return TextCommandResult.Success();
            })
            .EndSubCommand();

        // A command completes later only when one of its arguments is parsed later.
        command.BeginSubCommand("later")
            .WithArgs(new LaterParser(api))
            .HandleWith(args => TextCommandResult.Success("done later: " + args[0]))
            .EndSubCommand();

        command.BeginSubCommand("boom")
            .HandleWith(_ => throw new InvalidOperationException("pharoscmd boom"))
            .EndSubCommand();
    }
}

/// <summary>
/// Takes one word and hands it over three frames later, as a lookup would. Main-thread tasks run
/// once per frame; the client's delayed callbacks run on wall-clock time.
/// </summary>
internal sealed class LaterParser : ArgumentParserBase
{
    private readonly ICoreClientAPI _api;
    private string _value;

    public LaterParser(ICoreClientAPI api) : base("word", isMandatoryArg: true)
    {
        _api = api;
    }

    public override object GetValue() => _value;

    public override void SetValue(object data) => _value = (string)data;

    public override EnumParseResult TryProcess(TextCommandCallingArgs args, Action<AsyncParseResults> onReady = null)
    {
        string word = args.RawArgs.PopWord();
        After(3, () => onReady?.Invoke(new AsyncParseResults { Status = EnumParseResultStatus.Ready, Data = word }));
        return EnumParseResult.Deferred;
    }

    private void After(int frames, Action action) =>
        _api.Event.EnqueueMainThreadTask(() =>
        {
            if (frames <= 1) action();
            else After(frames - 1, action);
        }, "pharoscmd-later");
}
