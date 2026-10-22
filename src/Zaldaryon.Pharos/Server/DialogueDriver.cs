using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>What a conversation says.</summary>
/// <param name="Lines">The lines the conversation shows.</param>
/// <param name="Answers">The answers the player may pick.</param>
public sealed record DialogueLines(IReadOnlyList<string> Lines, IReadOnlyList<string> Answers);

/// <summary>
/// The conversations of the server: what a villager says, and what the player may answer.
/// </summary>
public sealed class DialogueDriver
{
    private const string ConversableBehavior = "EntityBehaviorConversable";

    private readonly EmbeddedServerHost _host;
    private object? _controller;

    internal DialogueDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Starts a conversation with someone, the way the player does, and returns what is said.
    /// </summary>
    /// <param name="player">The player who talks.</param>
    /// <param name="npc">The one who talks back.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the conversation says.</returns>
    public Task<DialogueLines> StartAsync(ServerTestPlayer player, Entity npc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(npc);

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Start(player, npc));
    }

    /// <summary>
    /// Picks an answer and returns what is said next.
    /// </summary>
    /// <param name="answerId">The id of the answer, as the conversation numbers them.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the conversation says next.</returns>
    public Task<DialogueLines> ChooseAsync(int answerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_controller == null) throw new InvalidOperationException("The conversation has not started.");

        return Task.FromResult(_host.RunOnGameThread(() =>
        {
            GameMembers.Call(_controller!, "PlayerSelectAnswerById", answerId);
            return LinesOf(_controller!);
        }));
    }

    private DialogueLines Start(ServerTestPlayer player, Entity npc)
    {
        return _host.RunOnGameThread(() =>
        {
            object? conversable = GameMembers.FindBehavior(npc, ConversableBehavior);
            if (conversable == null) throw new InvalidOperationException("The one who talks does not talk.");

            EntityPlayer? playerEntity = player.Player?.Entity as EntityPlayer;
            if (playerEntity == null) throw new InvalidOperationException("The player has no entity to talk with.");

            object? controller = GameMembers.Call(conversable, "GetOrCreateController", playerEntity);
            if (controller == null) throw new InvalidOperationException("The conversation could not be started.");

            _controller = controller;
            GameMembers.Call(controller, "Init");
            return LinesOf(controller);
        });
    }

    private static DialogueLines LinesOf(object controller)
    {
        List<string> lines = [];
        List<string> answers = [];

        object? current = GameMembers.Get(controller, "currentDialogueCmp");
        if (current == null) return new DialogueLines(lines, answers);

        if (GameMembers.Get(current, "Text") is object[] texts)
        {
            foreach (object text in texts)
            {
                string? value = GameMembers.Get(text, "Value") as string;
                if (value == null) continue;
                lines.Add(value);

                int id = (int)(GameMembers.Get(text, "Id") ?? -1);
                if (id >= 0) answers.Add(value);
            }
        }

        return new DialogueLines(lines, answers);
    }
}
