# Bags, wearables and liquids

Three item families have storage rules of their own: a bag keeps its contents in the stack's
attributes, a wearable goes into the character's gear slots, and a liquid container holds litres of
a liquid. Pharos reads and writes all three the way the game does.

## Bags

`Server.Inventories.Bag` returns the contents of a bag in a slot, and writes into them the way the
game writes them.

```csharp
using Zaldaryon.Pharos.Items;

ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
BagContents bag = Server.Inventories.Bag(slot);

Assert.True(bag.Put(0, "game:flint", 12));
Assert.Equal(12, bag.Count("game:flint"));
```

`Put` puts the stack in a slot of the bag and stores it in the bag's own attributes, so the bag
holds it the way it holds the items a player put in. `Contents` lists the stacks, `Count` sums the
items of a code, and `Clear` empties the bag.

`PharosAssert.BagContentsPreserved` checks that the items a bag held are still there after a move, a
drop or a death, whether they lie loose or inside the bag that fell.

```csharp
BlockPos deathPos = new BlockPos(10, 64, 10);
PharosAssert.BagContentsPreserved(Server, 12, deathPos, "game:flint");
```

## Wearables

`player.Gear.EquipAsync` puts an item into a gear slot on the server, the way the character dialog
does, and `player.Gear.Stats` reads what the worn items give.

```csharp
await alice.Gear.EquipAsync("armorbody", "game:armor-body-jerkin-leather");

Assert.True(alice.Gear.Stats.Protection!.ProtectionTier >= 0);
Assert.True(alice.Gear.Stats.Warmth >= 0);
```

The slot codes are the ones the character dialog uses: `boots`, `gloves`, `cape`, `hat`, `trousers`,
`shirt`, `pullover`, `necklace`, `bracers`, `belt`, `medal`, `mask`, `armorhead`, `armorbody` and
`armorlegs`.

The stats hold the warmth the worn items give, the stat modifiers and the protection of the first
armor that is worn.

## Liquids

`Server.Liquids` reads what a liquid container holds and pours between them.

```csharp
ItemStack bucket = Server.RunOnGameThread(() => new ItemStack(Server.Api.World.GetBlock(new AssetLocation("game:woodbucket"))!, 1));
ItemStack jug = Server.RunOnGameThread(() => new ItemStack(Server.Api.World.GetBlock(new AssetLocation("game:woodbucket"))!, 1));

float moved = Server.Liquids.Pour(bucket, jug, 3);

Assert.Equal(3, moved, 2);
Assert.Equal(7, Server.Liquids.In(bucket).Litres, 1);
```

`In` returns the liquid's code, the litres the container holds and how many it can hold. `Pour`
takes the liquid out of one container and puts it in the other, the way the game pours when a player
does, and returns the litres that moved. A container that is already full takes nothing.

## Limits

- The bags are the ones the game declares as bags. A bag's contents live in its attributes, so
  another tool that writes the attributes behind the game's back is not seen.
- The gear is equipped on the server. The character dialog is not driven, so the slot a player picks
  in the dialog is set by the test.
- The protection and the stat modifiers are the ones of the first worn armor. The warmth is the sum
  of the worn items' warmth.
- The liquids are the game's own containers: a bucket, a jug or a barrel. The interfaces live in the
  survival mod's assembly, so Pharos reaches them by name.
- The litres are the game's own: a container's capacity comes from its item's or block's properties.
