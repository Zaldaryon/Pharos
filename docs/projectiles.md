# Projectiles

Bows, slings and spears fire projectiles that fly on their own, hit something and leave their
stack behind. The game runs that flight in code the melee attacks do not touch, so Pharos gives a
test its own way in.

## Recording the flight

`Server.Projectiles.Track` opens a log that records the projectiles that fly in a region: where
they were, tick by tick, what they hit and what happened to their stack.

```csharp
ProjectileLog log = Server.Projectiles.Track(pos, radius: 24);

Assert.Equal(0, log.Projectiles.Count);
```

The log watches a region of blocks around a point. It keeps recording until it is disposed, and a
test disposes it when it ends.

## Firing

`Server.Projectiles.FireAsync` fires the weapon in a hotbar slot, the way a player does: the
shooter looks at the point, the button is held for the charge and released, and the weapon spawns
its projectile.

```csharp
Vec3d aim = new(pos.X + 10, pos.Y + 1, pos.Z);
ProjectileRecord? arrow = await Server.Projectiles.FireAsync(player, 0, aim, charge: 1.5f);

Assert.NotNull(arrow);
```

The charge is in seconds of game time, not in ticks, so the shot is the same however fast the test
runs. A weapon that needs no charge, such as a thrown item, fires with a short one.

## Waiting for the hit

`Server.Projectiles.WaitForImpactAsync` waits until the projectile hits something and returns what
it hit. It ticks the server while it waits.

```csharp
Vec3d aim = new(pos.X + 10, pos.Y + 1, pos.Z);
ProjectileRecord? arrow = await Server.Projectiles.FireAsync(player, 0, aim, charge: 1.5f);
ProjectileHit hit = await Server.Projectiles.WaitForImpactAsync(arrow!, maxTicks: 200);

Assert.NotNull(hit.Entity);
```

The hit holds the entity that was hit, or null when the projectile hit a block, the position, the
damage the projectile carries, the kind of damage it is and whether the stack can be picked up or
was broken.

## The path

Every projectile the log saw keeps its path, one sample per tick.

```csharp
ProjectileRecord arrow = new(1, "arrow-copper");

Assert.Empty(arrow.Path);
```

## Limits

- The charges and the spread are the game's own. A shot spreads while the aim is short, so a test
  that wants a sure hit holds the button long enough for the aim to settle.
- A projectile that hits a player deals no damage when the server has player against player turned
  off. A test that wants the damage uses a creature or a dummy.
- The game does not say which face of a block a projectile hit, so the hit carries the block and no
  face.
- The log watches a region. A projectile that flies out of it is not recorded any more.
