# Commands

A mod's commands are more than their handlers: they have a tree, argument parsers, privileges and
callers, and the chat around them can be rewritten. Pharos reads all of it, so a test can assert on
the command itself and not only on the text it answers with.

```csharp
[ServerMods("mods/mymod")]
public class MyModCommandTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheCommandTreeIsComplete()
    {
        CommandNode root = Server.Commands.Tree("mycmd")["mycmd"];

        Assert.Equal("mymod:admin", root.Privilege);
        Assert.Equal("mymod:mycmd-desc", root.Description);
        Assert.Contains("reset", root.Subcommands);
    }
}
```

`Server.Commands.Tree` takes the name of a command, or the id of a mod, and returns the commands
with their aliases, root aliases, call syntax, privilege, description and subcommands. A node that
the game would call incomplete says so, and the privilege is the one of the command or of its
closest parent that has one.

`PharosAssert.CommandsValid` checks a mod's commands the way the game does, and more: the game's own
validation has to pass, no node may be incomplete, and every description has to resolve in the
language the server runs in and in every language the mod ships.

```csharp
[ServerScenario]
public void MyModsCommandsAreValid()
{
    PharosAssert.CommandsValid(Server, "mymod");
}
```

## Parsing without running

`ParseAsync` runs a command's parsers and stops there, so a test can check what a command makes of
its arguments without the handler running.

```csharp
[ServerScenario]
public async Task ThePositionIsRelativeToTheCaller()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    alice.GrantPrivilege("buildblocks");

    ParsedCommand parsed = await Server.Commands.ParseAsync("/mycmd tp ~1 ~ ~-2", CommandDriver.PlayerCaller(alice.Player!));

    Assert.True(parsed.Ok);
    Assert.Equal(alice.Entity!.Pos.XYZ.AddCopy(1, 0, -2), parsed.Arg<Vec3d>("pos"));
}
```

The result holds the command path, the parsed arguments with their names and values, and what the
parsers complained about. `Arg<T>("name")` reads one value. A parser that has to look its value up
later is waited for, with the server ticking, up to the tick budget.

## Callers

A command runs as somebody: the console, a player, an entity or a position. The caller decides what
the command may do, and what it is told about where it happened.

```csharp
[ServerScenario]
public async Task TheCommandRunsAsThePlayer()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    alice.GrantPrivilege("buildblocks");

    CommandResult result = await Server.Commands.ExecuteAsync("/mycmd whoami", CommandDriver.PlayerCaller(alice.Player!));

    Assert.Equal(EnumCommandStatus.Success, result.Status);
}
```

`CommandDriver.ConsoleCaller`, `PlayerCaller`, `EntityCaller` and `PositionCaller` build the four
callers the game knows. A console caller has every privilege, a player caller has the player's own,
and an entity or position caller has the privileges it was given.

## Privileges

`[CommandPrivilegeMatrix]` runs a command once per role and records whether each role may run it.

```csharp
[ServerTheory]
[CommandPrivilegeMatrix("/mycmd reset", "suvisitor", "suplayer", "admin", Expected = "denied,denied,allowed")]
public async Task ResetNeedsAdmin(CommandPrivilegeCase privilege)
{
    await privilege.AssertAsync(Server);
}
```

The method takes one `CommandPrivilegeCase` argument. The expected outcomes follow the order of the
roles: `allowed` or `denied` for each of them. A row without an expectation records the outcome and
leaves the judgement to the test.

## Chat

`Server.Chat.Record` records what the server's chat handlers did with the messages, until the
record is disposed.

```csharp
[ServerScenario]
public async Task TheChatHandlerRewritesTheMessage()
{
    using ChatLog chat = Server.Chat.Record();
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");

    await alice.SayAsync("hello world");

    ChatMessage message = Assert.Single(chat.Messages);
    Assert.Contains("hello world", message.OriginalText, StringComparison.Ordinal);
    Assert.Equal("hello world", message.FinalPlainText);
}
```

Each message holds the text as the player wrote it, the text after the handlers ran, the data that
came with it, the group it went to, and whether a handler consumed it. `FinalPlainText` is the
final text without the chat's markup.

The chat a player received is in `player.Received.Chat`, one line per message, with the markup in
`Message` and without it in `PlainMessage`.

## Limits

- The commands a mod registered are found through the call stack of the registration, so a mod that
  registers its commands from a helper of another assembly is not found by its mod id. The commands
  are still in the tree by name.
- A parser that waits for a lookup is waited for with the server ticking, up to the tick budget of
  `ParseAsync`. A lookup that never finishes leaves the parse unfinished.
- The chat record holds the handlers that were registered when the record started. A handler
  registered while the record is open runs after the record's own handler, so the final text of such
  a message is the text before that handler.
- The chat a player received is what the server sent, with the server's markup.
