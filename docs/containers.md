# Containers and item flow

Storage mods and automation mods move items between inventories, and the bugs they cause are lost or
duplicated items. Pharos reads a block entity's inventory as data, counts what a region holds, and
reports what the item flow moved.

## Reading and filling a container

`Server.Inventories.At` returns the inventory of the block entity at a position, with the slots a
test can fill and count.

```csharp
BlockPos chest = new BlockPos(10, 64, 10);
ContainerInventory container = Server.Inventories.At(chest);

Assert.True(container.Fill(0, "game:ingot-copper", 64));
Assert.Equal(64, container.Count("game:ingot-copper"));
```

`Fill` puts the stack in the slot the way the game does, so the change is marked dirty and the
players are told about it. `Clear` empties every slot, `ItemAt` reads one, and `Count` sums the
items of a code. `Server.Inventories.HasInventory` answers whether a block has an inventory at all,
and `GroundStorage` reads a ground storage the same way as any other container.

## Counting a region

`Server.Inventories.Totals` sums every item in a region: the block entities' inventories, the items
on the ground and the inventories of the players who stand in it.

```csharp
BlockPos chest = new BlockPos(10, 64, 10);
ItemTotals before = Server.Inventories.Totals(chest, radius: 8);

Assert.Equal(64, before.Count("game:ingot-copper"));
```

`PharosAssert.ItemsConserved` compares two totals and names what appeared or vanished.

```csharp
BlockPos chest = new BlockPos(10, 64, 10);
ItemTotals before = Server.Inventories.Totals(chest, radius: 8);

PharosAssert.ItemsConserved(before, Server.Inventories.Totals(chest, radius: 8));
```

## The item flow

`Server.Inventories.RunFlowAsync` steps the server's ticks and reports what the flow moved, read
from the inventories before and after.

```csharp
BlockPos region = new BlockPos(10, 64, 10);
ItemFlowReport report = await Server.Inventories.RunFlowAsync(region, ticks: 600);

Assert.Contains(report.Moves, move => move.Code == "game:ingot-copper" && move.Quantity > 0);
```

Each move names where the items came from, where they went, the code and how many. A hopper above a
chest moves the items down; the moves are read from the containers, so they hold whatever the flow
did with them.

## The client's copy

`PharosAssert.InventorySynced` compares the client's copy of an open container with the server's.

```csharp
BlockPos chest = new BlockPos(10, 64, 10);
await Session.Blocks.UseAsync(chest);

PharosAssert.InventorySynced(Session, chest);
```

## Limits

- The totals count the block entities, the items on the ground and the players' inventories. What a
  mod keeps in a field of its own is not counted.
- The moves are read from the inventories before and after the ticks, so a move that takes the items
  out of the region is not reported. The items that left are visible in the totals.
- The items on the ground are counted per entity. When the game merges two piles into one, the
  counts move with them.
- The client's copy is the inventory the client has open. A container the client has not opened has
  no copy to compare.
