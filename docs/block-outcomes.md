# Block outcomes

A block drops what it is worth, an explosion breaks what stands in the blast, and a block calls
the game back when an entity or a neighbour touches it. Those hooks live in the game's own code,
so Pharos drives them and records what they did.

## The drops of a block

`Server.Blocks.Drops` rolls the drops of a block many times and counts what came out.

```csharp
DropDistribution drops = Server.Blocks.Drops(pos, player.Player, rolls: 200);

Assert.True(drops.Total("game:plantdebris-leafy") > 0);
```

The distribution holds one entry per code, with how many of the rolls dropped it and how much of it
dropped. `Mean` gives the quantity per roll, `Total` the quantity over all the rolls and `Hits`
the number of rolls that dropped it.

The drops are the game's own, so they follow the player: the held tool and the player's state
decide what falls. A block that needs a tool drops nothing when the hand is empty.

## Breaking a block

`Server.Blocks.BreakAndCollectAsync` breaks a block for real, the way a player does, and returns
the items that fell.

```csharp
IReadOnlyList<EntityItem> dropped = await Server.Blocks.BreakAndCollectAsync(player, pos);

Assert.NotEmpty(dropped);
```

## Explosions

`Server.World.ExplodeAsync` sets off an explosion the way the game does and reports what it
destroyed, what dropped and who was hurt.

```csharp
BlockPos center = new(10, 64, 10);
ExplosionReport report = await Server.World.ExplodeAsync(center, EnumBlastType.RockBlast, destructionRadius: 4, injureRadius: 6);

Assert.NotEmpty(report.Destroyed);
```

The report holds the blocks the blast destroyed, the items they dropped and the entities they
hurt, with the health each one lost.

## Contacts

`Server.Blocks.TraceContacts` records the calls a block makes when an entity touches it: the
entity that collided, the entity that is inside and the entity that fell onto it.

```csharp
using ContactTrace trace = Server.Blocks.TraceContacts(pos, radius: 8);
ContactEvent contact = await trace.WaitForAsync(ContactHook.EntityInside, pos, maxTicks: 200);

Assert.True(contact.EntityId > 0);
```

The trace keeps the calls while it is open, and a test disposes it when it ends.

## Neighbour updates

`Server.Blocks.TraceNeighbourUpdates` records the calls a block makes when a neighbour changes.

```csharp
using NeighbourUpdateTrace trace = Server.Blocks.TraceNeighbourUpdates(pos, radius: 8);
NeighbourUpdate update = await trace.WaitForAsync(pos, maxTicks: 100);

Assert.True(update.Neighbour.Equals(pos));
```

## Limits

- The hooks are the ones the block calls. A block that does its own thing without calling the
  game's hook is not seen.
- The traces watch a region. A call outside it is not recorded.
- The explosion's drops follow the game's own chance, so a blast at the edge of a blast may drop
  nothing.
