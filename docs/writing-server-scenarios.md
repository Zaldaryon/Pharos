# Writing Server Scenarios

This guide explains how to write server-side scenario tests using Pharos. A server scenario runs against a real Vintage Story server embedded in your test process, without any live game client.

## Basic Setup

### 1. Install the Package

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

### 2. Create a Server Scenario Class

Inherit from `ServerScenarioBase` and mark test methods with `[ServerScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

[ServerWorld(seed: 12345, playStyle: "creativebuilding", worldType: "superflat")]
public class BlockPlacementTests : ServerScenarioBase
{
    [ServerScenario]
    public void SetBlock_PlacesBlockAtPosition()
    {
        var pos = new BlockPos(10, 64, 10);
        this.SetBlock(pos, "game:soil-low-none");

        var block = this.GetBlock(pos);
        Assert.Contains("soil", block.Code.Path);
    }
}
```

## ServerScenarioBase

The base class provides access to the embedded server and its APIs.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Host` | `EmbeddedServerHost` | The embedded server host with tick control |
| `Server` | `ServerMain` | Direct access to the vanilla server instance |
| `Api` | `ICoreServerAPI` | Vintage Story server API |

### World Manipulation

```csharp
// Block access
void SetBlock(BlockPos pos, string blockCode);
void SetBlock(BlockPos pos, int blockId);
Block GetBlock(BlockPos pos);
BlockEntity? GetBlockEntity(BlockPos pos);

// Entity spawning
Entity SpawnEntity(string entityCode, Vec3d pos);
```

### Command Execution

```csharp
// Console caller
Task<CommandResult> ExecuteCommand(string command);
Task ExecuteSuccess(string command);  // asserts Ok == true

// Player caller
Task<CommandResult> ExecuteCommandAsPlayer(IServerTestPlayer player, string command);
Task ExecuteSuccessAsPlayer(IServerTestPlayer player, string command);
```

### Event Waiters

All waiters advance simulation ticks deterministically, not real-time delays:

```csharp
Task WaitForChunkLoadedAsync(int cx, int cy, int cz, int maxTicks = 300);
Task<Entity> WaitForEntitySpawnAsync(string entityCode, double radius, Vec3d around, int maxTicks = 300);
Task WaitForConditionAsync(Func<bool> condition, int maxTicks = 300);
```

`Host.Events.Record(filter)` records the server's game events, such as blocks placed, entities
dying and players respawning, as typed records to assert on and wait for. See
[Game events](inspection-api.md#game-events-record-and-assert).

## Lifecycle

`ServerScenarioBase` implements `IAsyncLifetime`. Before each test it boots an embedded server in a fresh sandbox from `WorldOptions`, which defaults to the class-level `[ServerWorld]`. It stages every `[ServerMods]` path from the class and the assembly into the sandbox's `Mods` folder first. After the test it stops the server, or keeps it for the next test (see below).

The server runs on its own game thread, as the real server does: boot, ticks and teardown all happen there. Test code that touches live server state should go through `Host.RunOnGameThread(...)`. The world helpers (`this.SetBlock`, `this.SpawnEntity`) and `ExecuteCommand` already do.

Scenarios run one at a time, whatever the test collection layout. The game keeps process-wide static state, so two servers or clients cannot boot side by side in one process.

## Isolation Modes

Control how world state is managed between test methods with `[ServerWorld(Isolation = ...)]` or by overriding `WorldIsolation`:

| Mode | Behavior |
|------|----------|
| `Rollback` (default) | The server keeps running for the next test of the same class, and the world is put back in place as it was after boot. See [Rollback](#rollback). |
| `Restart` | Every test gets a freshly booted world. |
| `Recycle` | The server keeps running for the next test of the same class. Tests share state, which suits read-only tests. |

```csharp
[ServerWorld(seed: 42, Isolation = WorldIsolation.Recycle)]
public class ReadOnlyTests : ServerScenarioBase { }
```

### Rollback

Right after boot, the scenario takes a `WorldSnapshot`. When a test ends, its test players leave and the snapshot is restored on the game thread, in place, without a restart.

What the snapshot holds:

- **Chunks:** every loaded chunk, in the game's own save format (blocks, fluids, light, decor, block entities, the entities stored with the chunk, and mod data), plus the height maps of their map chunks. Chunks that load later are recorded as they were when they loaded.
- **Players:** the world and server data of every known player. For each connected player, also the entity's `WatchedAttributes` and `Attributes` trees, its stats exactly (every value, persistent or not, and each category's blend type), whether it is alive, its spawn point, game mode, inventories, position, mod data and runtime privileges.
- **Mod data:** the save game's (`SaveGame.StoreData`) and each map region's. Map regions that load later are recorded as they were when they loaded.
- **Calendar:** the time, its speed, and a freeze.
- **Weather:** each loaded region's cloud pattern, wind, weather event and snow accumulation, the precipitation override, and whether the weather changes on its own.

What a restore does:

- **Unchanged chunks:** left alone.
- **Changed chunks:** swapped for their saved copy the way the game reloads a chunk from disk. Their entities are despawned, the saved ones are loaded with their original ids, and their block entities are initialized.
- **Players:** any player who joined after the snapshot is forgotten, so a later player of the same name starts fresh. A connected player is put back:
  - its health, satiety, body temperature, damage over time, character class, traits and every other key of its attribute trees, written into the live trees so the behaviours that hold a sub-tree keep holding the right one;
  - its stats and the health behaviour's max health modifiers;
  - its spawn point, role, permanent and runtime privileges, deaths, movement settings and mod data;
  - a player that died is revived the way the game respawns one, then given its saved state.

  Its client is sent the whole entity and its player data, and the rollback waits until the client sees the player alive or dead as the server does, with the server's health and no death dialog.
- **Mod data:** each key of the save game's and of each loaded map region's mod data is put back, removed if the snapshot did not have it. A region that changed is sent again to the clients that have it.
- **Calendar and weather:** put back first, then sent to connected clients. Time can go backwards: a mod that keeps the time in memory resets on `pharos:rollback:restored`.

Not restored: chunks first loaded after the snapshot and unloaded again, what mods keep in memory, and C# event handlers a test added (such as `Api.Event.PlayerJoin += ...`). A chunk that unloads during a test is saved with its changes. A restore that fails leaves the next test a freshly booted server.

Mod data comes back as bytes. Most mods, the game's included, read their save-game data once when the world loads and write it when the world saves, so a restore gives them the booted data to reload in a `pharos:rollback:restored` handler, but does not rewind what they hold in memory. The temporal storm and rift state is one such case (#339). Regions load and generate on the chunk thread, which may write a region's mod data while it is recorded; a region caught that way is left out. A key stored with null counts as absent. A world save during a test, such as a player leaving, rewrites the game's own keys, which then count in `ModDataKeysRestored`. The weather system writes a region's mod data whenever that region's weather changes, so while the weather changes on its own, regions may be counted in `MapRegionsRestored` and sent again after every rollback.

The counters that order packets always keep their live value: `positionVersionNumber` and `onHurtCounter` in `WatchedAttributes`, `tick` and `tickDiff` in `Attributes`. The server config's temporary privileges (`GrantTemporaryPrivilege`) are restored; a player's view distance is not. The server drops a stat category a test added, but the client keeps it, with the test's values, because a client only overwrites the categories the server sends: client code that reads such a stat still sees the test's modifier. Modifiers a test added to a category that existed at boot are put back on the client too. Counters and timers a behaviour keeps in fields of its own, outside the trees, are not restored either; a mod with such state resets it on `pharos:rollback:restored`. A respawn the client requested before the rollback, still in flight, can land after it.

### Opting keys out

A key a class wants to keep from test to test, such as lifetime statistics, is opted out by overriding a hook. A refused key keeps its live value, whether or not the snapshot had it:

```csharp
[ServerWorld(Isolation = WorldIsolation.Rollback)]
public class EconomyTests : ClientServerScenarioBase
{
    // Save-game, map region and player mod data keys.
    protected override bool RestoreModData(string key) => key != "mymod:lifetimeStats";

    // Top-level keys of the joined player's WatchedAttributes and Attributes.
    protected override bool RestorePlayerAttribute(string key) => key != "mymod:reputation";
}
```

`ServerScenarioBase` has `RestoreModData` only: its test players leave before the rollback, so it never restores a connected player. Both hooks run on the server's game thread; a hook that throws fails the rollback. `EmbeddedServerHost.RestoreSnapshot(snapshot, new WorldRestoreFilter(modData, playerAttribute))` takes the same filters and returns a `WorldRestoreResult` with what it put back.

### Listeners a test leaves

Before the world is restored, the rollback removes what was added since the world was captured:

- tick listeners, delayed callbacks and event-bus listeners whose handler is the test's own: compiled into the test class's assembly or a base class's, lambdas and local functions included. Override `IsTestListener(Delegate)` to judge otherwise;
- every block-position delayed callback scheduled since, whoever scheduled it and in whatever chunk, such as water still spreading: it carries on a change the restore undoes.

Listeners the game or mods registered stay. A mod resets its own state on `pharos:rollback:restored`. Game event logs a test left open (`Host.Events.Record`) are closed before the rollback.

### Mods and rollbacks

Pharos fires two events on the server's event bus, on the game thread:

| Event | When |
|-------|------|
| `pharos:rollback:captured` (`RollbackEvents.Captured`) | Once, when the world is captured after boot. |
| `pharos:rollback:restored` (`RollbackEvents.Restored`) | After every rollback, before the next test starts. |

The data is a `TreeAttribute` with `test`, the test whose world was captured or rolled back, and, for `restored`, `chunks`, how many chunks were restored. A mod that caches world state listens for `restored` and reloads it:

```csharp
api.Event.RegisterEventBusListener((string name, ref EnumHandling handling, IAttribute data) =>
{
    if (name == "pharos:rollback:restored") myCache.Clear();
});
```

The scenario class gets the same through `OnRollbackCaptured()` and `OnRollbackRestored()`, which it can override. Listeners the class registers in `OnRollbackCaptured()` live as long as the server; the ones a test registers are removed by the next rollback, so a listener a test registers for `restored` is gone before the event fires. A handler or hook that throws fails the rollback, and the next test boots a fresh server. What the handlers log counts against the next test. Do not step the server from a handler.

### Isolation report

`Isolation` tells a test how its server was made ready:

| `Kind` | Meaning |
|--------|---------|
| `FirstBoot` | The class's first test booted it. |
| `RolledBack` | The previous test's server was kept and its world rolled back. `ChunksRestored`, `PlayersRestored`, `ModDataKeysRestored`, `MapRegionsRestored`, `ListenersRemoved`, `DataFilesRestored` and `Duration` say what that took. |
| `Recycled` | The previous test's server was kept as it was. |
| `Restarted` | It was booted again. `Reason` says why when a rollback was expected: the previous test stopped the server or timed out, its rollback failed, or another class used the pooled server in between. |

Each test's output ends with how it was made ready, how it left the server, and a summary of the class so far:

```
isolation: rolled back (12 chunks, 1 player, 2 mod data keys, 1 map region, 3 listeners removed, 340 ms)
after this test: rolled back (4 chunks, 0 listeners removed, 120 ms)
MyTests so far: 1 first boot, 6 rolled back
```

A test's output shows in test explorers, in trx files, and in the console with `--logger "console;verbosity=detailed"`. A failed test's `run.json` holds the report as `isolationReport`.

With `[ServerWorld(StrictIsolation = true)]` or an override of `StrictIsolation`, a test after which the world cannot be rolled back fails with an `IsolationException` naming why, instead of the next test quietly booting a fresh server. A test that timed out, or whose sandbox is kept for inspection, is not failed for it. In a client-server class that overrides `WaitForPlayerJoinOnInit` to false, nothing is ever rolled back, so `StrictIsolation` has no effect there.

The same works outside the scenario base:

```csharp
WorldSnapshot snapshot = host.TakeSnapshot();
// ... change the world ...
int restoredChunks = host.RestoreSnapshot(snapshot);
```

## Test Players

Join headless players into the server. Each one is a real multiplayer connection as far as the server can tell, with no rendering client behind it:

```csharp
[ServerScenario]
public async Task Player_CanReceiveItems()
{
    ServerTestPlayer player = await CreateTestPlayerAsync("TestPlayer");

    player.GiveItem("game:stick", 5);
    Assert.True(player.HasItem("game:stick", 5));

    player.GrantPrivilege("worldedit");
    await player.TeleportTo(100.0, 64.0, 100.0);
    await player.SayAsync("/time set day"); // runs as a command with the player as caller
}
```

A test player goes through the same join sequence as a real client, packet by packet:

1. Over an in-memory socket of its own, the player sends the login token query and its identification.
2. Once the server has spawned the player entity, the player sends the join request. The server answers it by setting up the inventories and streaming the world.
3. The player reports itself loaded and ready, which makes it a playing player.

So everything the server does for a real player also runs for a test player: the `PlayerJoin` and `PlayerNowPlaying` events, mods' join handlers, chunk sending, entity tracking and the playing-player count. A connection over an in-memory socket counts as local, so the server skips player verification and no auth server is involved.

Join as many players as a scenario needs. Each gets its own socket:

```csharp
var alice = await CreateTestPlayerAsync("Alice");
var bob = await CreateTestPlayerAsync("Bob");
Assert.Equal(2, Host.TestPlayers.Count);
```

`player.Disconnect()` sends the leave packet a quitting client sends. Players created in a test leave when the test ends, and the same name can join again. Outside a scenario, use `EmbeddedServerHost.JoinPlayerAsync`.

In a `ClientServerScenarioBase`, `CreateTestPlayerAsync` joins headless players next to the rendering client, which sees them as other players in the world.

A player counts as joined once it is playing and the server has sent it the chunk it stands in. The server only sends a client changes (blocks, particles, sounds) for chunks that client already has.

### What a player received

`player.Received` records everything the server has sent that player, which is what a real client would have received and acted on. Every packet is counted by id, and these kinds are decoded:

```csharp
await alice.SayAsync("hello");
Assert.Contains(bob.Received.ChatMessages, m => m.Contains("hello"));

Entity hen = this.SpawnEntity("game:chicken-hen", nearAlice);
await Host.TickUntilAsync(() => alice.Received.KnowsEntity(hen.EntityId));

Assert.True(alice.Received.HasReceivedPlayerData(bob.PlayerUID));
Assert.NotEmpty(alice.Received.Particles);
Assert.Contains(alice.Received.Sounds, s => s.Name.Contains("planks"));
Assert.Equal(marked, alice.Received.HighlightedBlocks(slotId: 7));
Assert.Contains(alice.Received.BlockChanges, c => c.Position.Equals(pos));

// A mod's own network channel, deserialized by message type
var pings = Host.ModPackets<MyPing>(alice, "mymod");
```

| Member | Contents |
|--------|----------|
| `Chat`, `ChatMessages` | Chat lines, with group and chat type |
| `EntityArrivals`, `EntityDepartures`, `KnowsEntity`, `HasReceivedEntity` | Entities the server started or stopped tracking for the player |
| `PlayerData`, `HasReceivedPlayerData` | Player data about the player itself and about other players |
| `Particles`, `Sounds` | Particle spawns and sounds to play |
| `Highlights`, `HighlightedBlocks(slot)` | Block highlights per slot |
| `BlockChanges` | Block changes, single or batched |
| `ModPackets`, `ModPacketsOf<T>` | Mod channel packets |
| `IngameErrors`, `IngameDiscoveries` | In-game error and discovery messages |
| `CountsById`, `Total` | Every packet, counted by id |

`player.Received.Clear()` starts a fresh record.

### Headless player actions

`player.Bot` sends the same client packets as the game client. The embedded server runs its normal
movement, block, inventory and entity interaction handlers, so permissions and mod callbacks still
apply. Every action returns a `BotResult`: `Accepted` reports the observed server-side result; for
entity actions, it means the matching attack or use event reached the bot's observer. Check entity
state separately when the effect depends on the entity or a mod handler. `Error` contains a new
in-game error from the server, and `Reason` describes a refusal without an in-game message.

```csharp
ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
BotResult walk = await bob.Bot.WalkToAsync(new Vec3d(12, 4, 10), maxTicks: 600);
Assert.True(walk.Accepted, walk.Error?.Code ?? walk.Reason);

BotResult selected = await bob.Bot.SelectSlotAsync(0);
BotResult broken = await bob.Bot.BreakBlockAsync(pos);
Assert.True(selected.Accepted, selected.Error?.Code ?? selected.Reason);
Assert.True(broken.Accepted, broken.Error?.Code ?? broken.Reason);

BotResult used = await bob.Bot.UseBlockAsync(chestPos);
IInventory chest = ((IBlockEntityContainer)GetBlockEntity(chestPos)!).Inventory;
IInventory hotbar = bob.Player!.InventoryManager.GetHotbarInventory();
BotResult moved = await bob.Bot.MoveSlotAsync(hotbar.InventoryID, 0, chest.InventoryID, 3, quantity: 1);
Assert.True(used.Accepted && moved.Accepted);
```

The bot can also place a block with `PlaceBlockAsync`, attack or use an entity with
`AttackEntityAsync` and `UseEntityAsync`, and press or release a control with `SetControlAsync`.
`WalkToAsync` sends position updates at 20 ticks per second, follows one straight line, and can
step up one block. It does not find a route around a taller wall or other obstruction. A block
placement needs a block in the active hotbar slot, and a container inventory must be opened before
`MoveSlotAsync` can move items into it.

### Player vitals

`player.Vitals` reads and changes the survival state the game keeps on the player entity: health, satiety and the five nutrient levels, oxygen, and body temperature and wetness. Every value comes from the game's own behaviour (`EntityBehaviorHealth`, `EntityBehaviorHunger`, `EntityBehaviorBreathe`, `EntityBehaviorBodyTemperature`), found by the name the game gives them, so nothing depends on the shape of the watched attribute trees.

```csharp
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class HungerTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Eating_RaisesSatietyAndTheProteinLevel()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        Assert.Equal(15, vitals.MaxHealth);
        Assert.Equal(1500, vitals.Saturation);

        // Only what is named changes, through the behaviour, so what the game derives updates.
        vitals.Set(health: 5, saturation: 300);
        Assert.Equal(5, vitals.Health);

        // Food feeds one nutrient level. The multiplier is the one a mod's food passes.
        vitals.ReceiveSaturation(250, EnumFoodCategory.Protein, nutritionGainMultiplier: 2f);
        Assert.Equal(200, vitals.Nutrition!.Protein);

        // Spending it, the way an attack or a health regeneration does, without waiting ten seconds.
        vitals.ConsumeSaturation(100);
    }
}
```

| Member | What it does |
|--------|--------------|
| `Read()` | A `VitalsReading` of every value at once |
| `Health`, `MaxHealth`, `BaseMaxHealth`, `MaxHealthModifiers`, `FutureHealth`, `HealthChangeRate`, `DamageOverTime` | The health behaviour |
| `Saturation`, `MaxSaturation`, `Nutrition` | The hunger behaviour, and `Nutrition` the five levels |
| `Oxygen`, `MaxOxygen`, `HasAir` | The breathe behaviour |
| `BodyTemperature`, `NormalBodyTemperature`, `Wetness` | The body temperature behaviour |
| `Set(health:, maxHealth:, saturation:, maxSaturation:, nutrition:, oxygen:, hasAir:, bodyTemperature:, wetness:)` | Changes what is named, and only that |
| `SetMaxHealthModifier(key, value)`, `RemoveMaxHealthModifier(key)` | What a trait or an armour piece adds to max health |
| `ConsumeSaturation(amount)` | Spends satiety the way the game does, scaled by the player's `hungerrate` |
| `ReceiveSaturation(amount, category, saturationLossDelay, nutritionGainMultiplier)` | Feeds the player the way an item does |
| `WaitForAsync(predicate, maxTicks)` | Ticks until a reading matches, and returns it |

`Read()` costs one walk of the entity's behaviours. Take one reading and keep it rather than reading again per assertion:

```csharp
VitalsReading before = vitals.Read();
Assert.Equal(15, before.MaxHealth);
Assert.Equal(1500, before.Saturation);
```

A few things the game does, worth knowing before a test asserts an exact value:

- **Max health is derived.** `Set(maxHealth:)` sets the base and lets the behaviour recompute, so the modifiers and the `maxhealthExtraPoints` stat still add to it. `SetMaxHealthModifier` is the same path a mod takes.
- **Health is clamped.** `Set(health:)` will not put the player above `MaxHealth` or below 0.
- **Hunger is skipped in creative and spectator mode.** The game neither drains satiety nor regenerates health from it there. Changing or spending hunger for such a player throws, rather than quietly doing nothing, because the value would not stick.
- **Satiety and health regeneration accumulate over game time.** For an exact value, drive the server with a [virtual clock](#virtual-clock) and read after a known number of ticks. `ConsumeSaturation` and `ReceiveSaturation` reach the same changes without waiting.
- **Body temperature follows the calendar.** It moves over game hours, so `Host.Calendar.Advance` moves it; see also [Calendar and Weather](#calendar-and-weather).

A world without the survival mod gives the player none of these behaviours. Every value reads as `null`, `vitals.IsAvailable` is false, and `Set` throws saying which behaviour is missing.

The client's copy of the same state is `Player.Vitals` in a `ClientServerScenarioBase` (see [Writing Client-Server Scenarios](writing-client-server-scenarios.md)), and `ClientServerAssert.VitalsSynced(Session, playerName)` compares the two.

### Damage, death and respawn

```csharp
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class ArmorTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task AHit_GoesThroughTheModsArmor()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        await player.DamageAsync(10, DamageInfo.FromEntity(wolf, EnumDamageType.PiercingAttack));

        // The mod's armour took 5 off, and the health behaviour did the rest.
        Assert.Equal(10, player.Vitals.Health);
    }
}
```

`DamageAsync` goes through `Entity.ReceiveDamage`, so armour, resistances, invulnerability frames and every behaviour and mod hook run for a real hit. The `DamageInfo` says what happened:

| Member | What it decides |
|--------|-----------------|
| `Type` | Which armour resists it, such as `EnumDamageType.PiercingAttack` |
| `Kind` | What kind of thing caused it, such as `EnumDamageSource.Entity` |
| `Source`, `Cause` | The entity that hit, and the entity behind a projectile |
| `Tier` | The weapon tier, which decides how much armour gets through |
| `Knockback` | How hard the hit pushes the player |
| `OverTime(duration, ticks, effect)` | Spreads the damage out, the way a poison or a bleeding wound does |

Three things the game does, worth knowing before a test asserts an exact result:

- **A creative or spectator player takes no damage.** `EntityPlayer.ShouldReceiveDamage` refuses everything but healing in those modes, so the world's `playStyle` has to be `surviveandbuild`.
- **Damage over time is not instant.** With a `Duration` the health behaviour creates a damage-over-time effect and takes nothing off until its ticks come; `player.Vitals.DamageOverTime` lists what is running.
- **The game gives the player half a second of invulnerability after any hit**, so two hits in quick succession need a tick between them unless the `DamageInfo` asks to ignore invulnerability frames.

`KillAsync` ends the player the way `EntityPlayer.Die` does, with a death reason and `OnEntityDeath` fired, and without touching health. `RespawnAsync` sends the packet a real client sends when its player clicks Respawn on the death screen, and ticks until the player is alive again. A living player is refused rather than pretended: the server ignores the packet too.

In a `ClientServerScenarioBase`, kill the joined player with `ServerPlayer.DamageAsync(...)` and respawn it with `Player.Death.Respawn()`, which clicks the client's own button; see [Writing Client-Server Scenarios](writing-client-server-scenarios.md).

## Server Tick Control

`EmbeddedServerHost` exposes deterministic tick stepping:

```csharp
[ServerScenario]
public async Task EntityMovesToTarget()
{
    var entity = SpawnEntity("game:wolf-male", new Vec3d(0, 64, 0));

    await Host.TickUntilAsync(
        () => entity.Pos.DistanceTo(new Vec3d(10, 64, 10)) < 1.0,
        maxTicks: 600
    );

    Assert.True(entity.Pos.DistanceTo(new Vec3d(10, 64, 10)) < 1.0);
}
```

## Virtual Clock

The server's time follows the wall clock by default: one `Tick()` moves it by however much real time passed, so anything that integrates `dt` depends on the machine's speed. That includes hunger, AI, a firepit's burn tick, fire spread and entity physics. `Host.UseVirtualClock` makes ticks drive the time instead:

```csharp
using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
Host.Ticks(600);                              // exactly 30 simulated seconds
clock.Advance(TimeSpan.FromMinutes(2));       // ticks in steps of 250 ms or less
await Host.WaitForBackgroundWorkAsync();      // relight, random ticks, spawner, chunks, pathfinding idle
```

- **What follows the clock:**
  - which server systems run, and with what `dt`;
  - tick listeners and delayed callbacks;
  - `world.ElapsedMilliseconds`;
  - the server API's uptime;
  - the calendar, unless you pass `followCalendar: false`.

  Each `Tick()` moves the time by exactly `tickMs`. A tick in which the server does not run, because it is suspended, moves it by nothing.
- **Intervals:** a system or listener runs once *more* than its interval has passed, so with 50 ms ticks a 100 ms listener runs every 150 ms, each time with `dt = 0.15`. That is the same on every machine.
- **The first tick after the switch:** the simulated time starts where the server's time was. The first `dt` each system and listener gets still includes the real time since it last ran, which can be long, more than 0.4 s, after a slow setup. Every `dt` after that is exact.
- **`Advance`:** each step is one tick with that `dt`, 250 ms or less by default (`maxStep` goes up to 1 s). Entity physics discards what it has to catch up on once it passes 0.4 s, so long steps lose physics time:
  - a 1 s step keeps only 0.2 s of it, losing about 80%;
  - a step near a multiple of 0.4 s keeps almost nothing.

  Hunger works in 10-second buckets. A `tickMs` above about 360 drops physics time the same way.
- **Server time in simulated seconds:** the clock is the server's single time, so these count simulated time too:
  - pings: a real TCP client that does not answer for 15 simulated seconds is kicked; in-memory clients never are;
  - autosave: every 300 simulated seconds;
  - the unloading of map regions: after 120 simulated seconds unused;
  - the AFK kick, when configured.
- **The calendar:** it moves `SpeedOfTime × CalendarSpeedMul` game seconds per simulated second while it runs, that is, while players are connected. Clients keep projecting their calendar on real time between the calendar packets the server sends, so freeze the calendar for an exact check of client and server calendars.
- **Background threads:** these keep their own clocks and run on real time:
  - relight;
  - random block ticks;
  - the creature spawner;
  - chunk loading and generation;
  - async pathfinding.

  What they do per simulated second therefore depends on the machine. `WaitForBackgroundWorkAsync` waits, without ticking or moving the clock, until each has an empty queue and has finished a whole pass since it last held work. Each wait takes at least one pass of every thread, so about half a second for the spawner's cycle. It throws `TimeoutException` naming the systems still busy after 30 seconds, scaled by `PHAROS_TIMEOUT_SCALE`; the message also says when the server is suspended or a thread was asked to pause (`ShouldPause`). What a pass hands to the game thread is done on the following ticks: random ticks to run, creatures to spawn, chunk columns loaded. It cannot be called on the game thread.
- **Disposing:** the server goes back to the wall clock, carrying on from the simulated time, so its time never goes backwards. A clock a test leaves in use ends with the test, before a rollback or a recycle. The server's time is never rolled back.
- **Failure artifacts:** a failure's `run.json` records the clock under `serverClock`.
- **Cost:** Pharos patches the server's time reads once per process, before anything boots, whether or not a test uses the clock. That takes about 0.4 s, and longer in a process where nothing has used Harmony yet. Without a clock, the patched reads return what the game's stopwatch says.

## Calendar and Weather

`Host.Calendar` reads and sets the world's calendar. The game's clock follows the wall clock: while players are connected it moves 30 game seconds per real second, and with nobody connected it stands still. A test that needs exact times freezes it first:

```csharp
Host!.Calendar.Freeze();
Host.Calendar.SetTime(hourOfDay: 22);           // the next 22:00
Host.Calendar.SetDate(year: 1, month: 7, day: 4); // keeps the time of day
Host.Calendar.Advance(TimeSpan.FromHours(30));  // at once, as /time add does
EnumSeason season = Host.Calendar.Season;          // at the spawn point
```

- Every change lands on a whole second and is sent to clients at once.
- `Freeze()` stops time on the server and its clients, as `/time stop` does in effect: hunger and health regeneration stop with it, weather transitions do not. `Unfreeze()` lets it run again; it does not undo `/time stop`, which stops time another way. `IsFrozen` is also true after `/time stop`.
- `Advance` jumps. Game systems that work from the time passed, such as crops and snow, catch up on their next update.
- `Month` runs from 1 to 12, `Year` from 0. `Season` is the season at the spawn point; `GetSeason(pos)` depends on latitude.

`Host.Weather` sets the weather, through the game's essentials mod, which every world loads:

```csharp
Host!.Weather.SetPattern("overcast");               // every loaded region
Host.Weather.SetWind("still", at: player.Entity!.Pos.AsBlockPos);
Host.Weather.SetPrecipitation(1);                  // world-wide, -1 to 1; null hands it back
Host.Weather.StopChanges();                        // no new patterns, winds or events
```

- The game simulates weather per map region and blends the four regions around a position, so `at` sets those four. Without it, every loaded region is set. Regions that load later start with weather of their own.
- `PatternAt(pos)` and `WindSpeedAt(pos)` read it back at once. `Patterns` and `Winds` list the codes; an unknown code throws, listing them.
- `StopChanges()` finishes any change under way and stops patterns, winds and weather events from changing on their own. Precipitation still follows the calendar: freeze the calendar or override precipitation to hold it too.

A rollback puts the calendar and the weather back as they were after boot.

## Tick Listeners and Fast-Forward

`Host.TickListeners` lists the server's game tick listeners and delayed callbacks, the ones block entities do their work in, and runs the ones at a block position on simulated time:

```csharp
BlockPos pos = new(512000, 4, 512000);
var listeners = Host!.TickListeners.At(pos);
Assert.Contains(listeners, l => l.Mod == "mymod" && l.IntervalMs == 100);

FastForwardResult run = await Host.TickListeners.FastForwardAsync(pos, TimeSpan.FromMinutes(2), step: TimeSpan.FromMilliseconds(100));

Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(0, pos));
PharosAssert.NoTickListenersAt(Host, pos);
```

### Listing

- `All()`, `Of(modId)`, `At(pos)`, `OfEntity(entityId)` and `OfType(type)` list listeners. `Callbacks()` and `CallbacksAt(pos)` list delayed callbacks.
- Each `ServerTickListenerInfo` gives:
  - `Id` and `IntervalMs`;
  - `Kind`: `Block` when it was registered with a position, as `BlockEntity.RegisterGameTickListener` does;
  - `Pos`: that position, or, for a plain listener, the position of the block entity its handler belongs to;
  - `Handler`, the type and method;
  - `Mod`, by the assembly of the object the handler is bound to;
  - `OwnerType`, that object's type: the block entity, a behavior, an entity;
  - `EntityId`, for an entity or one of its behaviors;
  - `SinceLastRunMs`, which is negative while an initial delay is pending;
  - `Manager`: the server has two event managers, and ids count up separately in each.
- Callbacks give `DueInMs` instead of the interval and the time since the last run.
- A vanilla firepit has four listeners:
  - `OnBurnTick` every 100 ms and `On500msTick` every 500 ms;
  - the 10 s inventory tick of `BlockEntityContainer`;
  - `onTick` of its `BEBehaviorTemperatureSensitive`, every 1900 to 2099 ms.

  `OfType` matches the first three.
- A lambda's target is a compiler-made closure, so its `Mod` comes from the closure's assembly. Its `OwnerType` is the closure, and `Pos` and `EntityId` stay null unless it captured nothing but `this`.

### Fast-forward

`FastForwardAsync(pos, span, step)` runs everything tied to `pos` (listeners and callbacks, block and plain, in both managers) for `span` of simulated time, at once, on the server's thread. A firepit cooks and a quern grinds as far as they would in that time, without waiting real seconds.

- **Steps.** `span` is cut to whole milliseconds. Simulated time moves `step` at a time. The last step is shorter if `span` is not a multiple of `step`. `step` is whole milliseconds, at least 1. By default it is the smallest interval at `pos`, or 50 ms when there are only callbacks.
- **When a listener runs.** Each step, a listener runs once if at least its interval has passed since it last ran, with `dt` the simulated time since then. When `step` divides the interval, `dt` is exactly the interval. The game itself waits for strictly more than the interval: on a 50 ms tick it runs a 100 ms listener every 150 ms. A fast-forward runs it every 100 ms.
- **Order.** Within a step, listeners run by interval, then id, across both kinds and both managers; the game runs plain listeners first, then block listeners, in list order. Due callbacks run after the listeners, by due time. Each callback is taken out before it runs, with `dt` the time it is late.
- **Errors.** As in the game, an error goes to the listener's error handler: a block entity's logs it, so read `Host.Logs`. A block listener whose handler throws keeps its last run time, so it is called again every step. A listener or callback with no error handler stops the run with its exception.
- **Changes during a run.** Handlers may register and unregister listeners and callbacks, or remove their own block:
  - a listener or callback registered at `pos` joins the run, with its initial delay;
  - one that is removed is not called again;
  - the run stops early once nothing is left at `pos`, and `Simulated` says how far it got.
- **Nesting.** A fast-forward inside a handler throws `InvalidOperationException`.
- **The result.** `Calls` counts calls per listener; `CallsTo(listener)` reads it. `CallbacksRun` counts callbacks.

#### How it fits with real time

A run starts every listener afresh, from now. The real time a listener was owed (less than one interval) is dropped, so a run is exact and repeatable.

The exception is a listener that has not run since the previous fast-forward: it carries on from where that run left it. So 60 s in one run and 30 s twice end in the same state, as long as each part is a whole number of steps.

Afterwards, every listener and callback is moved back to real time. The next tick owes a listener only what the run left over, not the span, and a callback with 4 s left still has 4 s left.

#### What it does not move

Only the listeners and callbacks at `pos` run. These stay where they were:
- `world.ElapsedMilliseconds`;
- the calendar;
- weather;
- every other block entity and system.

Handlers that measure time with them see none pass. Two examples:
- A quern drops a player who stopped turning it 1 s after their last turn, by `ElapsedMilliseconds`. Under a fast-forward it never drops them, and keeps grinding.
- A firepit turns from extinct to cold after 2 game hours, by the calendar. Move `Host.Calendar` for that.

#### Clients

In a client-server test, the client's copy of the block entity catches up when the server sends it, on the next `Host.Tick()`.

#### Randomness

Some block entities use the world's random numbers. A firepit's temperature behavior cools it in the rain, at random. Call `Host.Weather.SetPrecipitation(0)` and `Host.Calendar.Freeze()` when two runs must match.

### Leftover listeners

`PharosAssert.NoTickListenersAt(host, pos)` fails, listing them, while any listener or callback is still tied to `pos`. Breaking a block removes its block entity's listeners at once, so it can run right after.

Blocks also schedule callbacks of their own, such as liquid flow. Pass `includeCallbacks: false` to check listeners only.

## Seeded randomness

Almost all of the game's random numbers come from unseeded generators, such as `World.Rand`, the static generators behind drops and crop growth, and those that systems, blocks and entities hold. `[Seeded]` seeds them before each test it covers, so a test can assert exact outcomes and a failure can be replayed:

```csharp
[ServerScenario, Seeded(1234)]          // a fixed seed
public void Grass_DropsSeeds_Sometimes() { ... }

[Seeded]                                 // a stable hash of each test's name
public class DropTests : ServerScenarioBase { ... }

[assembly: Seeded]                       // every scenario test in the assembly
```

- Seeding is opt-in. A test that no `[Seeded]` covers keeps the game's own generators. `[assembly: Seeded]` gives every test the hash of its name by default.
- The nearest attribute wins: the test's, then the class's, then the assembly's.
- The seed of a hash is the same on every machine and every run. It is taken from the class's full name, the method and the theory arguments.
- The test's output starts with the seed, after the versions: `Random seed: 1234 ([Seeded] on the method). Replay it with PHAROS_RANDOM_SEED=1234.`
- A failure's `run.json` has `randomSeed` and `randomSeedSource`; see `docs/failure-artifacts.md`.
- `PHAROS_RANDOM_SEED=1234` replaces the seed of every test that `[Seeded]` covers. Run the one test with it to replay its failure.
- Only the scenario attributes read `[Seeded]`. Under a plain `[Fact]`, call `Host.Random.Seed(n)` yourself. It seeds the statics of the calling assembly too; `Seed(n, assemblies)` names more.

`Host.Random` does it by hand, at any time in a test:

```csharp
Host!.Random.Seed(42);                   // re-seeds, undoing the earlier seeding
int? seed = Host.Random.CurrentSeed;     // null while the game's own generators run
Host.Random.Restore();                   // the game's own generators again
IReadOnlyList<SeededRandom> seeded = Host.Random.Seeded;
Assert.Empty(Host.Random.Unseeded.Where(u => u.Assembly == typeof(MyMod).Assembly));
```

A seeding happens when the body starts, after the boot or the rollback, and is undone when the test ends, in every isolation mode. A reseed during the test adds a line to the test's output. In a client-server scenario, `SeedRandom(n)` reseeds both sides; `ServerHost.Random.Seed(n)` reseeds only the server and the statics.

What is seeded:

- every generator gets a stream of its own, derived from the seed and the generator's name, so one system drawing more does not shift another;
- `World.Rand` on each side (`ServerMain.rand`, `ClientMain.rand`): one stream per thread, named after the thread;
- the static generators of the game, of the side's mods and of the test assembly, such as `BlockDropItemStack.random` and `BlockEntityFastForwardGrowth.rand`. They are seeded in place, so every holder sees the seeded stream;
- the generators held by the side's systems, mod systems, blocks, items and their behaviours, loaded entities, block entities and their behaviours;
- the entities and block entities the side makes during the test. Each is seeded once, right after the game's call to its `AfterInitialized` or `Initialize` returns, so fields an override sets after calling the base method are covered too. Each is keyed by its code, where it is, and how many of it came before in the test, never by its entity id;
- `[ThreadStatic]` generators, such as `NatFloat`'s and the AI tasks', on the server's game thread and an engine-mode client's thread.

`Host.Random.Unseeded` lists what Pharos cannot seed, each entry with its `Assembly`:

- generators created inside methods with no seed, or with a seed taken from a clock (`Environment.TickCount`, `DateTime.Now`, a `Stopwatch`, `Guid.NewGuid`, `ElapsedMilliseconds`, `string.GetHashCode`);
- instance fields of objects Pharos does not reach, and fields set to a new generator after Pharos seeds them;
- `[ThreadStatic]` fields, which are seeded only on the game threads;
- anything a seeding failed on.

Pharos reads the code of the game and the mods once per process, when the first host boots. That takes about a second for the game. It patches, before the world ticks, the methods that initialize entities and block entities. Code it meets later, such as a mod loaded on a reconnect, is read at the next seeding.

Limits:

- **The draws become replayable, not the whole simulation.** Unless a virtual clock drives the server (see "Virtual Clock"), the server's tick length follows the wall clock, so how many draws a tick makes can change from run to run. Assert on what a seeded call produces, rather than on what many ticks add up to.
- **Work on other threads is reproducible in what it draws, but not in when it runs.** Examples are the random block ticks, chunk loading, and the client's particles. A static generator that several threads share, such as `BlockDropItemStack.random`, which the client and the server share in one process, interleaves in timing order.
- **Thread-pool threads and a fixture-mode client are not replayable.** Thread-pool threads have no name and share one stream per generator. A fixture-mode client runs on the test's own thread, so its `[ThreadStatic]` generators are listed in `Unseeded` rather than seeded.
- **Entities and block entities that a mod initializes through a non-virtual call, or by calling the base method directly, are not seeded when they are made.**
- **Pharos seeds the fields declared on the objects it reaches, not objects nested inside them.** It does not cover a generator that a mod copied somewhere else from a `ThreadLocal` that Pharos replaced.
- **A `Random` subclass that draws in methods of its own may keep state the seed does not reach.** Such generators carry a note in `Seeded`.

## Staging Server Mods

Use `[ServerMods]` to load mods before server boot:

```csharp
[ServerWorld(seed: 42)]
[ServerMods("path/to/mymod.zip")]
public class ModIntegrationTests : ServerScenarioBase
{
    [ServerScenario]
    public void ModRegistersItsBlocks()
    {
        var block = Server.Api.World.GetBlock(new AssetLocation("mymod:myblock"));
        Assert.NotNull(block);
    }
}
```

## Data Files

Use `[DataFiles]` to put a file, such as a mod's config, into the server's data folder before it boots. A `{{pharos:port:NAME}}` placeholder in it becomes a free port, which `DataFilePort("NAME")` reads back:

```csharp
[ServerMods("mods/mymod.zip")]
[DataFiles("Fixtures/mymod.json", To = "ModConfig/mymod.json", Side = EnumAppSide.Server)]
public class MyModConfigTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheModListensOnItsPort()
    {
        using TcpClient tcp = new();
        tcp.Connect("127.0.0.1", DataFilePort("metrics"));
    }
}
```

A rollback puts changed data files back. See [Data files](data-files.md).

## Watchdog

Tests that wedge or run too long fail automatically. The default watchdog is 120 seconds. Override per class:

```csharp
[ServerWorld(seed: 1)]
[ServerScenario(TimeoutMs = 30_000)]
public class QuickTests : ServerScenarioBase { }
```
