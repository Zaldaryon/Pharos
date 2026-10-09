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
bool clicked = await session.Blocks.UseAsync(chestPos);  // opens the chest dialog
await session.Blocks.AimAtAsync(pos, BlockFacing.NORTH); // just aim
```

`PlaceAsync` first puts a stack of the block in the active hotbar slot through the server.

A use or a placement is one click, whatever the frame rate. The client repeats an action while its control stays held past a quarter of a second of real time, so `UseAsync` and `PlaceAsync` hold the control only until the client has acted on it, and release it before the next frame. A chest used twice would open and close again. `BreakAsync` holds attack only until the block is gone on the client, so it does not go on to break the block behind it.

On an engine-mode client, `Player.Camera` reads and writes the engine's own view angles (pitch 0 looks ahead and positive looks up, as for a fixture-mode client), and `Camera.Position` is the eye position the engine casts its selection ray from.

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

## Network conditions and disconnects

An engine-mode client talks to the embedded server over in-memory sockets that pass every packet through a link. The client's `PacketRecorder` and `NetworkDegradation` act on that real traffic:

```csharp
client.PacketRecorder.Start();
await session.HoldAsync(PlayerAction.Forward, 30);
client.PacketRecorder.Stop();
client.PacketRecorder.SaveToJson("walk.json");   // both directions, with packet ids

client.NetworkDegradation.Configure(new DegradedNetworkProfile(LatencyMs: 200, JitterMs: 50, PacketDropRate: 0.05f));
```

| Condition | TCP | UDP |
|-----------|-----|-----|
| Latency, jitter | Delayed, order kept | Delayed, may reorder |
| Packet drop | Not dropped (TCP is reliable) | Dropped |
| Corruption | Not applied | Dropped (fails its checksum) |

Time on the link is the simulated time of the session's frames, so the same seed and profile deliver the same way every run.

`client.DisconnectSimulator` disconnects the client for real:

- `SimulateKick(message)` and `SimulateServerShutdown()`: the server disconnects the player, and the client receives the server's disconnect message.
- `SimulateNetworkError(message)`, `SimulateTimeout()` and `SimulateServerCrash()`: the link is cut (`session.IsLinkSevered`), the client's own socket error handler runs ("The connection closed unexpectedly: ..."), and the server drops the player.

A disconnected engine-mode client ends its game session, as in the game. `session.ReconnectAsync()` joins it again as the same player, the way the game's own reconnect does:

```csharp
Session.DisconnectSimulator.SimulateKick("bye");
await Session.ReconnectAsync();
Assert.True(Client!.IsJoined);
```

- **What it works after.** A kick, a lost connection or a shutdown, `session.Disconnect()`, or with the client still joined. A client that is still joined leaves, and the server drops it as after a lost connection.
- **What ends and starts again.** The old game session ends: its mods are disposed and the world unloads. A new game session then starts and joins over a fresh in-memory connection, and the client's mods start again.
- **What carries over.** The window, `Client` and every driver a test holds now act on the new session. The link's network conditions and packet recording, the mod message log, and the client's log capture carry over too. `Client.SessionNumber` counts the sessions and `session.Reconnects` counts the reconnects.
- **Mod statics.** The game compiles a source mod again for each session, so its statics start from zero. A DLL mod is loaded once, so its statics carry over, as they do in the game. A DLL mod that patches with Harmony in `StartClientSide` must unpatch in `Dispose`, or it patches twice. Each session's compiled source mods stay in memory.
- **The next test.** A pair that reconnected is not rolled back for the next test: the next test gets a freshly booted pair, and the isolation report says "the client reconnected".
- **What does not follow.** GUI dialogs, element handles and measurement captures a test took before the reconnect belong to the old session. Boot diagnostics stay those of the first session.
- **Until the new session has the server's assets**, the server's direct push to a client that connects over memory can reach it, as at the first join.
- **Not supported.** The game's own `.reconnect` command still goes through the menus Pharos does not build. Reconnecting is not supported in fixture mode or over a real TCP connection.

## Bridge events

When the test project references `Zaldaryon.Pharos.Bridge`, an engine-mode client loads the bridge as a client-only mod (turn it off with `HeadlessClientOptions.LoadBridge = false`). The mod publishes to `BridgeChannel.Active`:

| Event | When |
|-------|------|
| `FrameStart`, `FrameEnd` | At the start and end of every rendered frame |
| `ChunkTessellated` | When the client adds a tessellated chunk to its render pools, with the chunk coordinates |
| `GuiStateChanged` | When a dialog opens (`IsOpen` true) or closes (false), named by type, such as `GuiDialogEscapeMenu` |

```csharp
BridgeChannel.Active!.DrainAll();
client.Input.PressKey(GlKeys.Escape);
await session.StepFramesAsync(3);
Assert.Contains(BridgeChannel.Active.DrainAll(), e => e.ScreenName == "GuiDialogEscapeMenu" && e.IsOpen);
```

The game loads the mod into the same context as the test, so both see the same channel. Tests that read the channel should share the `Bridge` collection with other tests that activate channels.

## Logged errors

Mods report most failures by logging them and carrying on. `HeadlessClient.Logs` and `EmbeddedServerHost.Logs` collect everything the client and the server log from boot, apart from debug entries:

```csharp
Assert.Empty(client.Logs.Errors);
Assert.Empty(serverHost.Logs.UnexpectedErrors(["known vanilla noise"]));
```

A scenario class can fail its tests on unexpected errors instead:

```csharp
protected override bool FailOnLoggedErrors => true;
protected override IEnumerable<string> AllowedLoggedErrors => ["Failed to load optional texture"];
```

The errors are taken when the test body ends, before teardown, so what the game logs while it shuts down does not count. A pooled host starts each test with empty logs.

## Client settings

`HeadlessClient.Settings` reads and changes settings live, by their keys in `clientsettings.json`, as the settings menu does. Watchers fire, and a graphics change rebuilds the frame buffers and reloads the shaders. `Apply` returns a handle that puts the previous values back:

```csharp
using (client.Settings.Apply(ClientSettingsProfile.Of("far", ("viewDistance", 256))))
{
    await session.StepFramesAsync(30);
}
```

`ClientSettingsProfile.Presets` holds the game's graphics presets (`minimum` to `maximum`) with the settings the graphics menu applies for each. A theory runs once per preset:

```csharp
[Theory, ClientSettingsMatrix("minimum", "high")]
public async Task WorldRenders(ClientSettingsProfile profile)
{
    using IDisposable _ = Client!.Settings.Apply(profile);
    await Session!.StepFramesAsync(10);
}
```

`[ClientSetting("viewDistance", 96)]` on a scenario class applies a setting to each of its tests once the client is up, and restores it when the test ends. Settings are process-wide in the game, so always restore what a test changes.

## Hotkeys

`HeadlessClient.Hotkeys` reads the hotkeys the client has registered, the game's and its mods', and fires them by their code:

```csharp
HotkeyInfo open = Client!.Hotkeys.Require("mymod:openpanel");
Assert.Equal(HotkeyBinding.Of(GlKeys.P, ctrl: true), open.Current);

HotkeyTriggerResult result = await Client.Hotkeys.TriggerAsync("mymod:openpanel");
Assert.True(result.Fired);
Assert.Null(Client.Hotkeys.ConflictWith("mymod:openpanel"));
```

- `TriggerAsync` sends the combination the hotkey is bound to as one key event with its modifiers as flags, down and up, through the same entry point as real input, then steps a frame. The real gating applies: a mod's key listener, a dialog that has the keyboard, whether character controls are allowed, the game mode, and which hotkey comes first when two share a combination.
  - `Fired` is true when the hotkey's own handler took the key.
  - `FiredCode` names the hotkey that did, another one on a clash.
  - `Consumed` with no `FiredCode` means something took the key before any hotkey saw it, such as the chat line while it has the keyboard.
- `PressAsync` presses the keys one by one through `Input`, modifiers first, holds them for `holdFrames` and releases them, even when the test fails. Held modifiers also hold what is bound to them, such as sprint on Ctrl. Double taps, such as the fly toggle's Space Space, and mouse buttons with modifiers work too. Two presses of the same key count as a double tap when the second comes within 200 ms of frame time of the first, at the default 1/60 s per frame twelve frames, however fast the machine steps them.
- Movement and other controls the game reads as held keys have no handler (`HasHandler` is false), so `TriggerAsync` cannot fire them: use `PressAsync` with enough frames, or `Controls`.
- `Rebind` binds a hotkey to another combination as the controls menu does, so the systems that watch bindings, such as player movement, follow, and shift-click follows sneak and ctrl-click sprint unless the menu sets them apart. Dispose the result to put the old binding back; it goes back even after the client is gone, since hotkeys are process-wide in the game. Unlike the menu, it does not raise `HotkeysChanged`.
- `Conflicts()` lists every group of hotkeys bound to exactly the same combination. The game has three on purpose: sneak and shift-click, sprint and ctrl-click, middle click and pick block. It does not list overlaps the game resolves at press time: a binding with no modifiers also fires on Ctrl, Shift or Alt with its key when no exact binding takes it, and a mouse binding with no modifiers matches any.
- Global hotkeys, such as F11 and F12, fire even while a dialog has the keyboard.
- For a while after a player joins, the game's intro tip takes the K key for its "hold K" help. A binding on K, even with modifiers, does not reach hotkeys while the tip shows.

## Client commands

`HeadlessClient.Commands` runs the client's chat commands, the ones that start with a dot, such as a mod's `.mymod status` or the game's `.clientconfig`. A command runs on the client thread with the same lookup, the same privileges and the same result line in chat as when the player types it in the chat dialog. It returns the command's status, message, error code and data, and the chat lines the client showed while it ran:

```csharp
ClientCommandResult result = await Client!.Commands.ExecuteAsync(".mymod status");
Assert.Equal(EnumCommandStatus.Success, result.Status);
Assert.Equal("ready", result.Message);

await Client.Commands.ExecuteSuccessAsync(".clientconfig viewDistance 64");
Assert.True(Client.Commands.Exists("mymod"));
```

- `Message` is the message as the player saw it in chat. A command that succeeds with no message shows no line, and its `Message` is null. So does an unknown command: its "No such command exists" line is in `ChatLines`.
- Every line the client shows itself is a `Notification`, a client command's result included; only the server's lines carry `CommandSuccess` or `CommandError`.
- `ExecuteSuccessAsync` throws a `ClientCommandException` unless the result is `Ok`: a success, or a deferred result, as for server commands.
- A handler that throws fails the test with its own exception.
- A command whose arguments are looked up later reports `Deferred` first and its real result once the lookup is done. `ExecuteAsync` waits for that by stepping frames, the server's too in a client-server scenario, up to `maxFrames` (600). A handler that returns `TextCommandResult.Deferred` itself never reports again, so the wait runs out and the result is `Deferred`; pass `maxFrames: 0` to take the first result as it comes. The wait steps the server too, so its world time advances. A real result that comes after the wait is still shown, during whatever steps next, and lands in the next command's `ChatLines`.
- The chat lines are those shown while the command ran. Lines that arrive later, such as the server's answer to a packet the command sent, are not in them.
- A command a test runs is not undone by a rollback: restore what it changed, as for settings.
- Server commands start with a slash and run on the server, not here: in a server scenario, `ServerScenarioBase.ExecuteCommand` runs them.

Mods' hooks on chat being sent (`OnSendChatMessage`) do not run. To cover them and the typed path itself, open the chat dialog and type through `Input` and `Ui` instead.

Compared with server commands, `ClientCommandResult.Message` and `Data` are what `CommandResult.StatusMessage` and `ReturnValue` are there, and `ClientCommandException` is the client's `CommandExecutionException`.

The client's delayed callbacks (`RegisterCallback`) run on real time in engine mode, not on frames: a mod that completes a command that way finishes after a number of milliseconds, however many frames that takes.

## Window size and GUI scale

A dialog that fits at 1920x1080 and GUI scale 1 can be cut off at 1280x720 or GUI scale 1.5.
`client.Window` resizes an engine-mode client's window the way a player dragging its edge does:
the game rebuilds its framebuffers, and in the next frame the dialogs lay themselves out again.

```csharp
await client.Window.ResizeAsync(1280, 720);   // runs the frame after the resize
Assert.Equal(1280, client.CaptureFrame().Width);
```

- **Size limits.** The game's smallest window is 600x400.
- **Restoring the size.** A scenario class puts the window back to the size and GUI scale it
  booted with after each test. A pooled client that cannot be put back is torn down instead.
- **The `Window` properties.** `Window.CurrentWidth` and `CurrentHeight` give the size now;
  `Width` and `Height` stay the boot size.

`Window.UseAsync(layout)` applies a size and a GUI scale together, and puts both back when it is
disposed. `[WindowSizes]` runs a theory once per size, and per scale in `GuiScales`. Without
`GuiScales`, a size may carry its own scale, as in `"1280x720@1.5"`:

```csharp
[ClientTheory]
[WindowSizes("1280x720", "1920x1080", GuiScales = [1f, 1.5f])]
public async Task PanelFitsOnScreen(WindowLayout layout)
{
    await using IAsyncDisposable _ = await Client!.Window.UseAsync(layout);
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
    await Session!.StepFramesAsync(2);

    DialogLayout panel = Client.Ui!.Layout("GuiDialogMyPanel");
    PharosAssert.DialogOnScreen(panel);
    PharosAssert.NoOverlappingElements(panel);
}
```

- **`Ui.Layout(nameOrType)`** (or `Ui.Layout<TDialog>()`) reads an open dialog's composers and
  elements with their bounds in screen pixels, as laid out in the last frame.
- **`PharosAssert.DialogOnScreen`** fails when a composer, or an element laid out within its
  composer, reaches past an edge of the window. It names each one and says by how much.
  Elements inside a clipping area, such as the rows of a scrolled list, are cut off by it, so
  they are not checked.
- **`PharosAssert.NoOverlappingElements`** fails when two elements of a composer overlap. By default
  it compares the elements a player reads or uses: buttons, inputs, switches, drop-downs, slot
  grids and text. Containers and backgrounds always hold their children.
- **`[ClientSettingsMatrix]`** also takes single settings written `key:value`, such as
  `guiScale:1.5`. xUnit does not combine the rows of two data attributes, so `[WindowSizes]`
  refuses to share a theory with it: give the scales to `[WindowSizes]` instead.

The game remembers the window size in the client's settings, as it does for a player.

## Reloading assets

`client.ReloadAsync(category)` does what the game's `.reload` command does, then runs a frame:

| Category | What happens |
|---|---|
| `AssetCategory.shaders` | The engine's shaders are compiled again, and the mods' `ReloadShader` handlers run. |
| `AssetCategory.shapes` | The shapes are read again, and the `ReloadShapes` listeners re-tessellate. |
| `AssetCategory.textures` | The textures are loaded again, and the `ReloadTextures` listeners run. |
| `AssetCategory.lang` | The translations are read again. |
| any other | Its assets are read again. |

```csharp
await client.ReloadAsync(AssetCategory.shaders);
Assert.Equal("ok", (await client.Commands.ExecuteSuccessAsync(".mymod shaderstate")).Message);
```

- **Failures throw.** A shader reload that reports errors (a shader that does not compile, or a mod
  handler that returns false) throws, unless `allowErrors: true` is passed. The returned
  `ReloadResult` says how many assets were read again and whether the reload succeeded.
- **All at once.** `ReloadAllAsync()` reloads translations, shapes, textures and shaders, in that
  order.
- **Other categories.** `ReloadAsync("config")` reloads a category by its code, such as one a mod
  registers.
- **Not supported:** sounds and music.

## Translations

`client.Lang` tracks the translation lookups the client makes on its main thread that find no
entry for their key: the game then shows the key itself, like `mymod:item-foo`. The scenario base
classes reset it before each test.

```csharp
using (client.Lang.Use("pt-br"))
{
    PharosAssert.AllTranslated(client, domain: "mymod");
    // ... open the mod's dialogs, look at its items ...
}

Assert.Empty(client.Lang.MissingKeysOf("mymod"));
```

- **`MissingKeys`** lists every key with no entry in its language, each once, with how often it
  was looked up and its domain.
  - **`MissingKeysOf(domain)`** filters by domain. The game itself looks up some plain English
    sentences as keys, so filter by your mod's id.
  - **`MissingKeysIncludingFallbacks`**, or `includeFallbacks: true`, also lists the keys the
    language lacks but English has; the game shows those in English.
  - **Not counted:** the game's translation of the status line of a command Pharos runs.
- **`Use(language)`** shows another language until it is disposed. In a scenario class, the
  language the client booted with comes back at the end of the test anyway. Dialogs already open
  keep the text they were composed with. `Use` loads the language again from the client's assets,
  so translations only a server-side mod supplies are not in it.
- **`PharosAssert.AllTranslated(client, domain, language)`** checks that every item and block of a
  domain has a name. It looks for the `item-` or `block-` key the game looks names up by; names a
  class builds in code are not checked.
- **`PharosAssert.NoMissingTranslations(client, domain)`** asserts `MissingKeys` is empty.

The game keeps one set of translations and one current language per process: a server booted in
the same process shares them, so `Use` switches the server's language too.

## Real network connections and auth

An embedded server can also listen on a real TCP and UDP port, as the game's `/allowlan` command opens it:

```csharp
var server = EmbeddedServerHost.Boot(new ServerWorldOptions { ListenPort = 0 });   // 0 picks a free port
var session = client.ConnectTcp(server, ClientAuth.Offline("Alice"));             // ticks the server in lockstep
```

`ConnectRemote(host, port, auth)` joins a server this process does not run, such as a dedicated server in a container. The `RemoteServerSession` it returns steps only the client, since the server keeps its own clock.

`ClientAuth` decides who the client is:

| Auth | Needs | Joins |
|------|-------|-------|
| `ClientAuth.Offline(name)` | Nothing; the client answers the login token itself | Servers with `VerifyPlayerAuth` off (the embedded default) |
| `ClientAuth.Online(name, uid, sessionKey, signature)` | A Vintage Story account session and access to `auth3.vintagestory.at` | Any server, including ones with `VerifyPlayerAuth` on |

`ClientAuth.FromEnvironment(prefix)` reads an account session from `{prefix}_PLAYERNAME`, `_PLAYERUID`, `_SESSIONKEY` and `_SESSIONSIGNATURE`, as the launcher stores them after a login. The session key is a credential: keep it in a CI secret, never in the repository. Pharos clears it from the process-wide client settings when the client is disposed. The online tests in this repository run only when those variables are set.

In-memory connections are never verified, whatever `VerifyPlayerAuth` says: the server treats them as local.

To run against a dedicated server in Docker, in CI or in a cloud environment, see [CI and Cloud Environments](ci-and-cloud.md).

## Threads

The engine-mode client runs on a dedicated main thread, and an embedded server on a dedicated game thread, as in the game. Pharos marshals boot, frames, captures and teardown there, whichever thread the test runs on. Code that touches GL or queues engine main-thread work should use `HeadlessClient.RunOnClientThread(...)`, and code that touches live server state should use `EmbeddedServerHost.RunOnGameThread(...)`.
- Rendering uses whatever OpenGL the process gets. On Linux CI that is Mesa llvmpipe under Xvfb (see `scripts/run-headless-linux.sh`).
