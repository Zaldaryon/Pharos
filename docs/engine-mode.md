# Engine Mode: Joining a Real Server

By default `HeadlessClientBootstrap.Boot` builds the client in **fixture mode**. It constructs `ClientMain` but never starts the engine. Chunks are injected from fixtures and every frame is deterministic. That mode is what the chunk, mesh and culling inspectors are built on, and it stays the default.

**Engine mode** runs the vanilla client startup, so the client can join a real server. It loads the server's mods and assets, goes through character creation, receives chunks and renders through the vanilla pipeline, as in the game.

```csharp
using var server = EmbeddedServerHost.Boot(new ServerWorldOptions { WorldType = "superflat" });
using var client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions
{
    BootMode = ClientBootMode.Engine,
});
using var session = client.ConnectLoopback(server, "Pilot");

Assert.True(session.WaitForPlayerJoined(TimeSpan.FromSeconds(120)));
await session.WaitForChunkMeshedAsync(ChunkPos.FromBlockPos(client.Client.EntityPlayer.Pos.AsBlockPos), maxFrames: 3000);

FramebufferSnapshot frame = client.CaptureFrame();
```

## What runs

`EngineClientStartup` follows `ClientProgram.Start`, `ScreenManager.Start` and the game's init stages:

1. The `ScreenManager`, the platform frame buffers and the minimal GUI shader are set up.
2. Base assets, the shader registry, the default hotkeys and the client mods are loaded.
3. `ClientMain.Start` runs, which starts the engine's own worker threads (network, tessellation, relighting, chunk visibility, particles).

Each step runs synchronously on the test thread, with no background loading screen.

## Offline by design

No step needs outbound network access, which is what makes engine mode usable in CI and in sandboxed cloud runners:

- No session key is validated and no newest-version request is made. The client is marked offline, the state vanilla falls back to when the auth server is unreachable.
- The client joins through the multiplayer handshake over the in-memory dummy sockets, the same transport singleplayer uses. Because it is offline, it answers the server's login token itself. The server treats every dummy-socket connection as local and skips player verification.
- `GuiScreen.OnScreenLoaded` would replace the running game screen with the login screen, because a test client has no session key signed by the auth server. A narrow Harmony prefix skips that one check for `GuiScreenRunningGame` only.

## Character creation

A player the server has not seen before must create a character before the client reports ready. Until it does, the server never marks the player as playing and never sends chunks. An engine-mode client walks through the survival "create character" dialog the way a player does, one step per frame:

1. **Confirm Skin**, keeping the appearance the dialog opens with.
2. The class arrows, until `HeadlessClientOptions.CharacterClass` is selected (`commoner` by default).
3. **Confirm Class**. The dialog's own close handler then sends the selection to the server.

Set `CompleteCharacterSelection = false` to drive the dialog from the test instead. `WaitForPlayerJoined` then returns false until the selection is made.

## When a client counts as joined

`HeadlessClient.IsJoined` and `ClientServerLoopbackSession.WaitForPlayerJoined` require all three of the following:

- the own player entity exists,
- every block, item and entity type the server sent is loaded,
- the client has told the server it is ready to play.

## Limits

- Timing is real, not deterministic: the engine's worker threads run on their own schedule. Use fixture mode when a test needs bit-exact frame stepping.
- One engine-mode client at a time per process. The engine keeps static state (`ScreenManager.Platform`, `ClientSystemStartup.instance`, the shader registry). Clients booted one after another in the same process work. For multiplayer, join headless players (`EmbeddedServerHost.JoinPlayerAsync`) next to the one rendering client. The rendering client sees them as other players.

## Input

`HeadlessClient.Input` drives an engine-mode client the way a keyboard and mouse would. Each injected event becomes the same engine event the platform builds from a real window event, and goes to the same handlers. The `ScreenManager` and the running game process it, so it updates the keyboard state, runs hotkeys, reaches open dialogs and moves the player:

```csharp
client.Input.PressKey(GlKeys.C);                    // hotkey: opens the character dialog
client.Input.InjectKey(VirtualKey.W, pressed: true); // hold W: the player walks, the server sees it
client.Input.InjectMouseDelta(40, 0);                // mouse look: turns the camera right
client.Input.InjectScroll(-1);                       // next hotbar slot
client.Input.PressKey(GlKeys.T);                     // open chat
client.Input.TypeText("hello");                      // text input into the focused field
client.Input.PressKey(GlKeys.Enter);
client.Input.Click(320, 180);                        // move the cursor and click
```

As on a real keyboard, a printable key reports both the key going down and the character it types. Hotkeys use the key and text fields use the character. The window of an engine-mode client counts as focused, because the game ignores the mouse in an unfocused window. Use `GlKeys` for any key; `VirtualKey` covers the common ones.

## Moving and interacting with blocks

The engine's player control system writes the player's movement from the keyboard state every frame. So movement goes through the controls, by whatever key each action is bound to:

```csharp
await session.HoldAsync(PlayerAction.Forward, frames: 60); // walk
client.Controls.Press(PlayerAction.Jump);                   // hold until released
client.Controls.Release(PlayerAction.Jump);
```

`session.Blocks` breaks, places and uses blocks the way a player does. It aims the camera at the block, waits for the client's own block selection to land on it, and presses attack or use. Block and item behaviors, mods' interaction handlers, reach and permission checks all run as in the game. Each call steps until the result is visible on both the client and the server:

```csharp
bool broken = await session.Blocks.BreakAsync(pos);
bool placed = await session.Blocks.PlaceAsync(against: ground, BlockFacing.UP, "game:rock-granite");
await session.Blocks.UseAsync(chestPos);                 // opens the chest dialog
await session.Blocks.AimAtAsync(pos, BlockFacing.NORTH); // just aim
```

`PlaceAsync` first puts a stack of the block in the active hotbar slot through the server. On an engine-mode client, `Player.Camera` reads and writes the engine's own view angles (pitch 0 looks ahead and positive looks up, as for a fixture-mode client), and `Camera.Position` is the eye position the engine casts its selection ray from.

## GUI and inventory

`HeadlessClient.Ui` reads and drives the GUI of an engine-mode client. Clicks are real mouse clicks at an element's centre, so they go through the game's own hit testing and handlers:

```csharp
client.Input.PressKey(GlKeys.Escape);
var back = client.Ui.Elements("GuiDialogEscapeMenu")
    .Single(e => e.Type == "GuiElementTextButton" && e.Text == "Back to Game");
client.Ui.Click("GuiDialogEscapeMenu", back.Key);

client.Ui.TypeInto("GuiDialogInventory", "searchbox", "granite");
client.Ui.ClickSlot("HudHotbar", "hotbargrid", slotIndex: 2);
bool open = client.Ui.IsOpen("GuiDialogCharacter");
```

`Elements(dialog)` lists every element of every composer of an open dialog, with its key, type, screen bounds and text. Dialogs are matched by class name or debug name. `client.Gui` (`GuiInspector`) answers from the same live dialogs, and its `SimulateButtonClick` becomes a real click.

`client.Inventory` works through the GUI too. `SelectSlot(i)` presses the slot's number key. `DragSlot(from, to)` opens the inventory, clicks the stack in the hotbar and clicks the target slot, so the server applies the move by its own rules:

```csharp
client.Inventory.SelectSlot(4);
client.Inventory.DragSlot(0, 3);
await session.StepUntilAsync(() => /* server sees the stack in slot 3 */ true);
```

## Sound

There is no audio device. The null device hands the game a silent sound object for each sound it asks for, and `HeadlessClient.Sounds` records every one of them. The game still decides which sounds to play, where, how loud and how often: footsteps, block and item interactions, entities, ambience, music, and sounds the server tells it to play.

```csharp
client.Sounds.Clear();
await session.HoldAsync(PlayerAction.Forward, 120);
Assert.True(client.Sounds.WasPlayed("walk"));

RecordedSound planks = client.Sounds.Played("block/planks")[0];
Assert.NotNull(planks.Position);
```

`Started` holds the sounds the game started, and `Created` every sound it created. Each record carries the asset location, position, volume, range, sound type and loop flag. The engine skips every sound while the sound level is 0, so an engine-mode client keeps normal sound levels. Nothing reaches a speaker either way.

## Threads

The engine-mode client runs on a dedicated main thread, and an embedded server on a dedicated game thread, as in the game. Pharos marshals boot, frames, captures and teardown there, whichever thread the test runs on. Code that touches GL or queues engine main-thread work should use `HeadlessClient.RunOnClientThread(...)`, and code that touches live server state should use `EmbeddedServerHost.RunOnGameThread(...)`.
- Rendering uses whatever OpenGL the process gets. On Linux CI that is Mesa llvmpipe under Xvfb (see `scripts/run-headless-linux.sh`).
