# Held items

Most item code runs while the player holds a mouse button: the item's `OnHeldInteractStart`,
`OnHeldInteractStep`, `OnHeldInteractStop` and `OnHeldInteractCancel`, the same four for attacks, and
the matching `CollectibleBehavior` hooks. Pharos records those calls, holds the buttons for a test,
and reads the mining times and the durability around them.

## The trace

`Server.Held.Trace` records every held-item hook call, until the scope is disposed.

```csharp
using Zaldaryon.Pharos.Items;

[ServerScenario]
public async Task TheItemIsUsedWhileTheButtonIsHeld()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    alice.GiveItem("game:bread-spelt-partbaked", 1);
    await alice.Bot.SelectSlotAsync(0);

    using HeldTraceScope scope = Server.Held.Trace();
    BotResult result = await alice.Bot.UseHeldAsync(40, alice.Entity!.Pos.AsBlockPos);

    Assert.True(result.Accepted);
    Assert.Contains(scope.Trace.Calls, call => call.Hook == HeldHook.InteractStart);
    Assert.Contains(scope.Trace.Calls, call => call.Hook == HeldHook.InteractStep);
}
```

Each call holds the hook, the side it happened on, the code of the item that was held, how long the
item had been held, and what the hook returned. `Last`, `LastOf`, `Of` and `CountOf` read the trace
back, and `On` narrows it to one side.

The client records the same hooks: `Client.Held.Trace()` opens a trace for the client's own calls.

## Holding the button

A bot holds the item in its active hotbar slot over a block, the way a player holds the right mouse
button: the server runs the item's held interaction once per tick while the button is down.

```csharp
BotResult result = await alice.Bot.UseHeldAsync(40, alice.Entity!.Pos.AsBlockPos);
```

An engine-mode client holds the button on its own side. In a client-server scenario the hold goes
through the session, so the client and the server step in lockstep while the button is down.

```csharp
using Zaldaryon.Pharos.Items;

using HeldTraceScope scope = Session.Held.Trace();
await Session.Held.UseAsync(TimeSpan.FromSeconds(1.5));
Assert.Contains(scope.Trace.Calls, call => call.Side == EnumAppSide.Client);
```

`Session.Held.AttackAsync` holds the attack button the same way, and the frames the hold takes are
counted from the frame time of a sixtieth of a second. A client on its own holds the button with
`Client.Held.UseAsync`, which steps the client's own frames.

## Mining and durability

`Server.Items` reads what the game would make of an item.

```csharp
[ServerScenario]
public async Task TheIronPickaxeMinesStoneQuickly()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    ItemStack pickaxe = new(alice.Player!.Entity.World.GetItem(new AssetLocation("game:pickaxe-iron"))!, 1);

    float time = Server.Items.MiningTime(pickaxe, new BlockPos(1, 64, 1), alice.Player);

    Assert.InRange(time, 0.1f, 60f);
}
```

`MiningTime` is the block's resistance divided by the stack's mining speed, the way the game
measures it. The game measures the mining speed against a player, so the first online player is used
when none is given. `MiningSpeed`, `ToolTier`, `CanMine`, `RemainingDurability`, `MaxDurability` and
`Durability` read the rest.

`PharosAssert.CannotMine` checks that a tool cannot mine a block, and `PharosAssert.DurabilityLost`
checks how much durability an action cost.

```csharp
PharosAssert.CannotMine(Server, "game:pickaxe-copper", "game:ore-medium-ilmenite-granite");

int before = Server.Items.Durability(0, alice.Player);
await alice.Bot.BreakBlockAsync(new BlockPos(1, 64, 1));
PharosAssert.DurabilityLost(Server, 0, 1, before, alice.Player);
```

## Limits

- The trace records the hooks of the game's `CollectibleObject`, the general ones that dispatch to
  the interact and the attack hooks. A mod that overrides only the general hook is recorded under
  the same names.
- A tool's mining speed is measured against a player. Without one online the measurement cannot be
  made, and the read says so.
- `CanMine` reads the tier and the material of the tool. A block whose tier depends on the world
  around it is measured with the tier the block declares.
- The client's own hold steps the client's frames. In a client-server scenario the session's hold
  steps the client and the server in lockstep instead, so that the server's ticks happen with it.
