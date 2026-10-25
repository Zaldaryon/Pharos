# HUD inspection

`Client.Ui.Hud` reads the engine-mode client's composed HUD. Use it in a `ClientServerScenarioBase` with `ClientBootMode.Engine`, after stepping the frame that applies a server sync or setting change. A server-only vitals assertion proves the model changed; a HUD assertion proves what the client composed.

All readers dispatch to the client thread and return detached snapshots. Reading does not advance frames or call a mod's text/applicability callbacks again.

## Stat bars, hotbar and coordinates

```csharp
var hud = Client!.Ui!.Hud;
await hud.WaitForAsync(h => h.Health.Max > 0, maxFrames: 120);
var health = hud.Health;
Assert.InRange(health.Value, 0, health.Max);
Assert.True(hud.Satiety.Visible);
Assert.False(hud.Oxygen.Visible); // A full oxygen bar is normally hidden.
```

`Health`, `Satiety` and `Oxygen` expose `Value`, `Max`, nullable `Future`, `Visible` and `Flashing` from the actual `GuiElementStatbar`. Values are not substituted from watched attributes. Visibility includes the full-bar rule, overlay hiding and spectator suppression.

`Hotbar` exposes the displayed `ActiveSlot`, detached `Slots`, the fading `ShownItemName`, `OffhandSlot`, `Visible` and `Layout`. The skill slot is index 10 and the offhand slot is 11; backpack highlights are mapped back to inventory indices. `ShownItemName` is empty after its label hides. `Coordinates` returns actual composed `Text`, `Visible` and the independent `HiddenBySetting` flag.

## Block/entity information and interaction help

```csharp
await Session!.Blocks.AimAtAsync(pos);
var hud = Client!.Ui!.Hud;
await hud.WaitForAsync(h => h.BlockInfo.Visible, maxFrames: 120);
var info = hud.BlockInfo;
Assert.NotEmpty(info.Title);
Assert.Equal(pos, info.BlockPosition);
```

`BlockInfo` contains the composed `Title` and plain-text `Lines`, plus either `BlockCode`/copied `BlockPosition` or a detached `Entity` reference with its ID, code, position and dimension. No selected target means an invisible snapshot with empty text and no target. The mod's formatting is read through the composed rich-text components rather than returned as markup.

`InteractionHelp.Entries` contains the rows the engine actually composed and rendered in the latest frame, after `ShouldApply` and `GetMatchingStacks` filters. Rows include `Text`, `HotkeyCode`/`HotkeyCodes`, resolved `Hotkeys` (code and label), mouse button, item codes and the free-hand requirement. Labels reflect the mapping used during composition; remap and let the game recompose before asserting the new label. Mouse mappings use the game's icon names where it draws an icon instead of text. Hidden help returns no entries. Reads never re-run the mod's predicates.

## Messages and boss bars

`Errors` and `Discoveries` contain currently displayed messages, not a historical log. Each message has `Text` and the `Frame` where that occurrence first rendered. A repeated error event or a repeated queued discovery receives a new first-frame stamp, even when its text is identical. Hidden/expired messages disappear from the snapshot; polling does not create occurrences or change their frame stamps.

`BossBars` returns the visible survival boss bars' `EntityId`, composed `Name`, health `Fraction` and `Layout`. No visible boss bars means an empty list. Fraction comes from the composed bar, not the entity's watched health.

## Generic mod HUDs and cross-HUD overlap

```csharp
var hud = Client!.Ui!.Hud;
var hotbar = hud.Element<HudHotbar>();
var stats = hud.Element<HudStatbar>();
PharosAssert.NoOverlappingElements(hotbar.Layout, stats.Layout);
```

Replace the generic type with your mod's `HudElement` subclass to inspect its composers, bounds and element tree. Class names need not begin with `Hud`. `Elements()` includes registered closed elements with `Visible == false`. `Element<T>()` requires exactly one matching registration; use `Elements()` when several match. Snapshots can be retained across subsequent frames.

The two-layout overlap assertion compares elements across HUDs, even when their composer names differ. Its default filter includes readable/interactive controls, text and stat bars, not backgrounds or empty bounds. Pass `include` to choose specific elements, and `tolerance` for allowable pixel overlap. Both snapshots must use the same window dimensions and GUI scale. The existing single-layout overload still checks pairs within each composer.

## Wait and compatibility contracts

`WaitForAsync(predicate, maxFrames, ct)` uses session-aware frame stepping. It tests the current state first: a zero budget can succeed without a step. A false predicate after the budget throws `TimeoutException`; negative budgets and null predicates are rejected. Cancellation is checked even when a zero-budget predicate would already be true.

A missing/ambiguous typed HUD or a survival stat bar that has not been composed throws an actionable `InvalidOperationException`. Private engine-field contracts are checked for type and nullability; incompatible game internals fail with a Pharos/game compatibility diagnostic rather than invented values. This behavior was exercised against Vintage Story 1.22.7; an unsupported mod replacement can still be inspected through the generic reader.
