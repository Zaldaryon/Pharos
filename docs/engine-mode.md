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

## Threads

The engine-mode client runs on a dedicated main thread, and an embedded server on a dedicated game thread, as in the game. Pharos marshals boot, frames, captures and teardown there, whichever thread the test runs on. Code that touches GL or queues engine main-thread work should use `HeadlessClient.RunOnClientThread(...)`, and code that touches live server state should use `EmbeddedServerHost.RunOnGameThread(...)`.
- Rendering uses whatever OpenGL the process gets. On Linux CI that is Mesa llvmpipe under Xvfb (see `scripts/run-headless-linux.sh`).
