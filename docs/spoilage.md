# Spoilage and item temperature

Food dries, ripens, cures and rots, and hot items cool. The game keeps that state in the stack's
attributes and the calendar, so a test can read it, age it and compare the rates.

## Reading the transitions

`Server.Items.Transitions` returns the transition states of a stack: how fresh it is, how far it has
transitioned and what it turns into.

```csharp
ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
TransitionState? perish = Server.Items.Transition(slot, EnumTransitionType.Perish);

Assert.NotNull(perish);
Assert.Equal(0, perish!.TransitionLevel, 3);
```

`Transition` returns the state of one kind of transition, and `Transitions` all of them. The state
holds the transition's own properties, the hours the item is still fresh, the hours it has already
transitioned and the level of the transition, from zero to one.

## Aging

`Server.Items.AgeAsync` advances the calendar and lets the inventories apply the transitions the
time passed, so a stack that spoiled now holds the stack it turns into.

```csharp
ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
await Server.Items.AgeAsync(TimeSpan.FromDays(6));

Assert.Equal("game:rot", slot.Itemstack?.Collectible.Code.ToString());
```

The game swaps a stack when its transitions are read, so read them after the time has passed.

## Rates

`Server.Items.TransitionRate` returns the rate multiplier of a transition, the way the game applies
it, and `ContainerTransitionRate` the container's part of it. The rest is the item's own and the
climate's.

```csharp
ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
float rate = Server.Items.TransitionRate(slot, EnumTransitionType.Perish);
float container = Server.Items.ContainerTransitionRate(slot, EnumTransitionType.Perish);
```

`PharosAssert.SlowerIn` checks that a transition runs slower in one container than in another, and
`PharosAssert.SpoilsWithin` that a stack spoils within the days the item itself says.

```csharp
ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
PharosAssert.SpoilsWithin(Server, slot, days: 5, tolerance: 0.5);
```

## Temperature

`Server.Items.Temperature` reads the temperature of a stack and `Server.Items.SetTemperature` sets
it, the way the game cools and heats an item.

```csharp
ItemSlot slot = player.Player!.InventoryManager.GetOwnInventory("backpack")![0];
Server.Items.SetTemperature(slot, 1100);

await Server.Items.AgeAsync(TimeSpan.FromHours(2));

Assert.True(Server.Items.Temperature(slot) < 450);
```

## Limits

- The transitions are the ones the item declares. An item without them has no state.
- A stack that has never been in an inventory has no transition time yet, so it is read as spoiled.
  Put it in an inventory and let the inventory tick before the first read.
- The rates depend on the container, the climate and the room the item is in. A test that compares
  two containers pins them.
- The temperature is the game's own, in the game's units.
