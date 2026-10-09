<p align="center">
  <img src="docs/pharos-logo.svg" alt="Pharos logo" width="120" height="120" />
</p>

# Pharos

In-process headless test harness for Vintage Story client and server systems. Pharos boots a real Vintage Story client or server inside your test process, drives it frame by frame or tick by tick, and exposes rendering and world-state inspection APIs to xUnit test code.

## What It Does

Pharos runs Vintage Story inside your test process. The client loads chunks, meshes geometry, and issues OpenGL draw calls exactly like the normal client, but with no visible window and no audio. The server runs a full simulation loop with a real player registry and savegame stack, but in an isolated temporary sandbox. Your test code controls the frame and tick loops, positions the camera, inspects rendering state, places blocks, and asserts on results.

Use cases:

- Rendering regression tests for client-side mods
- Server-side integration tests without a running game instance
- Performance benchmarks that measure actual draw calls and allocations
- Visual diff tests comparing framebuffer output across versions
- Automated smoke tests for mod compatibility
- Lockstep client-server end-to-end scenario testing

## Requirements

- Vintage Story 1.22.x (1.22.7 recommended)
- .NET 10 SDK
- OpenGL 4.3+ for full inspection features (3.3 for basic headless)
- Windows or Linux (macOS not supported)

See the [compatibility matrix](docs/compatibility.md) for version details.

## Installation

### Client scenario testing

Add the Pharos XUnit metapackage to your test project:

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

This brings in the core library, xUnit integration, and xUnit itself.

### Server scenario testing

The server APIs are part of the same package. No extra package is needed.

### Migrating from Pixnop.Atlas

If you have existing Atlas test suites, install the compatibility shim for zero-code-change migration:

```bash
dotnet add package Zaldaryon.Pharos.AtlasCompat
```

Or use the CLI migration tool:

```bash
pharos migrate-atlas ./your-test-project
```

See [Migrating from Atlas](docs/migrating-from-atlas.md) for the full guide.

Set the `VINTAGE_STORY` environment variable to your game installation:

```bash
# Windows (PowerShell)
$env:VINTAGE_STORY = "C:\Program Files\Vintage Story"

# Linux
export VINTAGE_STORY=~/.local/share/vintagestory
```

## Quick Start: Client Scenarios

Create a test class that inherits from `ClientScenarioBase` and mark test methods with `[ClientScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

public class ChunkRenderingTests : ClientScenarioBase
{
    [ClientScenario]
    public void ClientLoadsChunksAroundPlayer()
    {
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(32, 64, 32);
        FrameController!.Frames(60);
        Assert.True(Client!.WorldMap.LoadedChunks > 0);
    }
}
```

## Quick Start: Server Scenarios

Inherit from `ServerScenarioBase` and use `[ServerScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

[ServerWorld(seed: 12345, playStyle: "creativebuilding", worldType: "superflat")]
public class InventoryTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task GivingItemToPlayer_UpdatesInventory()
    {
        var player = await CreateTestPlayerAsync("TestPlayer");
        player.GiveItem("game:sword-iron");
        Assert.True(player.HasItem("game:sword-iron"));
    }
}
```

## Quick Start: Client-Server Scenarios

Combine both in one locked-step test with `[ClientServerScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

public class BlockInteractionTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public async Task BreakingBlock_UpdatesBothSides()
    {
        var pos = new BlockPos(10, 64, 10);
        Server.Api.World.BlockAccessor.SetBlock(1, pos);
        await Session.StepUntilAsync(() => ClientServerAssert.BlockSynced(Session, pos));
        ClientServerAssert.ClientServerBlockSynced(Session, pos);
    }
}
```

## Quick Start: Smoke-Test a Mod

Check that a mod loads, joins and plays without errors, with no test project:

```bash
dotnet tool install -g Zaldaryon.Pharos.Cli
export VINTAGE_STORY=/path/to/vintagestory
xvfb-run -a pharos smoke --mod bin/Release/mymod.zip --strict
```

Or, in a test project, inherit the ready-made scenario:

```csharp
[ServerMods("../../../../MyMod/bin/Release/mymod.zip")]
[StrictBoot]
public class Smoke : ModSmokeTest;
```

See [Smoke Testing a Mod](docs/smoke-test.md).

To run a live suite faster, `pharos run` runs its test classes in parallel worker processes and
merges their results into one TRX:

```bash
pharos run tests/MyMod.Tests --filter "Category=Live" --parallel 2 --trx results.trx
```

See [Parallel Runs](docs/parallel-runs.md).

To build a test world, stamp in a WorldEdit schematic, or boot each class from a saved world that
`pharos fixture` made from a builder test:

```csharp
await this.PlaceSchematicAsync("Fixtures/furnace-room.json", new BlockPos(512, 4, 512));
```

```bash
pharos fixture tests/MyMod.Tests --scenario VillageBuilder.Build --out tests/MyMod.Tests/Fixtures/village.vcdbs
```

See [Test Worlds](docs/test-worlds.md).

## Quick Start: A Real Client Joining a Server

Boot the client in engine mode to run the vanilla client startup. The client joins the server, goes through character creation, receives chunks and renders the world through the game's own pipeline. It needs no auth server and no outbound network access:

```csharp
using var server = EmbeddedServerHost.Boot(new ServerWorldOptions { WorldType = "superflat" });
using var client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions { BootMode = ClientBootMode.Engine });
using var session = client.ConnectLoopback(server, "Pilot");

Assert.True(session.WaitForPlayerJoined(TimeSpan.FromSeconds(120)));
FramebufferSnapshot frame = client.CaptureFrame();
```

See [Engine Mode](docs/engine-mode.md) for what runs and how the offline join works.

Run tests with:

```bash
dotnet test -c Release
```

On Linux without a display:

```bash
./scripts/run-headless-linux.sh
```

## Packages

| Package | Description |
|---------|-------------|
| [Zaldaryon.Pharos](https://www.nuget.org/packages/Zaldaryon.Pharos) | Core headless client bootstrap, frame stepper, and inspection APIs |
| [Zaldaryon.Pharos.Bridge](https://www.nuget.org/packages/Zaldaryon.Pharos.Bridge) | In-client mod exposing inspection hooks to scenario code |
| [Zaldaryon.Pharos.XUnit](https://www.nuget.org/packages/Zaldaryon.Pharos.XUnit) | xUnit integration: `[ClientScenario]`, `[ServerScenario]`, `[ClientServerScenario]` |
| [Zaldaryon.Pharos.AtlasCompat](https://www.nuget.org/packages/Zaldaryon.Pharos.AtlasCompat) | Drop-in compatibility shim for `Atlas.Api` and `Atlas.XUnit` |
| [Zaldaryon.Pharos.Cli](https://www.nuget.org/packages/Zaldaryon.Pharos.Cli) | The `pharos` command: `pharos smoke`, `pharos run --parallel`, benchmarks, Atlas migration. Uses your own game install |

Install `Zaldaryon.Pharos.XUnit` for client and server testing. Install `Zaldaryon.Pharos.AtlasCompat` to migrate existing Atlas test suites without code changes.

## Platform Support

| Platform | Method | Notes |
|----------|--------|-------|
| Windows | Hidden GLFW window with FBO | Works on any GPU |
| Linux | Mesa llvmpipe under Xvfb | Works in CI without a GPU |
| macOS | Not supported | GLFW cannot create headless OpenGL contexts |

Both Windows and Linux produce identical rendering behavior and report OpenGL 4.5 core profile.

## Inspection APIs

### Client rendering

| Class | Purpose |
|-------|---------|
| `GlCommandProxy` | Count draw calls, buffer allocations, and VAO operations |
| `CullingInspector` | Query which chunks are visible, frustum-culled, or occlusion-culled |
| `MemoryInspector` | Track mesh pool hit/miss rates and type allocations |
| `ShaderInspector` | Intercept uniform uploads and detect redundant state changes |
| `FramebufferSnapshot` | Capture pixels for visual regression comparisons |
| `IndirectDrawInspector` | Verify GPU indirect draw dispatch and command buffer contents |

### Server and network

| Class | Purpose |
|-------|---------|
| `EmbeddedServerHost` | Native embedded server with tick control and sandbox isolation |
| `ServerSandbox` | Isolated temp directory, mod staging, SQLite-safe cleanup |
| `WorldSnapshot` | In-memory world state capture and rollback |
| `PacketRecorder` | Record and replay deterministic server packet streams |
| `PacketTracer` | Assert that specific packet IDs cross the loopback boundary |
| `NetworkDegradationSimulator` | Inject latency, packet drop, and jitter |

### Assertions

| Class | Purpose |
|-------|---------|
| `PharosAssert` | Client-side rendering assertions (draw collapse, chunk visibility, UV, GL leaks) |
| `ClientServerAssert` | End-to-end block sync, position sync, inventory sync assertions |

See the [inspection API reference](docs/inspection-api.md) for method signatures and examples.

## Documentation

- [Writing Client Scenarios](docs/writing-scenarios.md): `[ClientScenario]`, `[ClientTheory]`, `[PharosMods]`, and isolation modes
- [Writing Server Scenarios](docs/writing-server-scenarios.md): `[ServerScenario]`, `ServerScenarioBase`, `IServerTestPlayer`, and world helpers
- [Engine Mode](docs/engine-mode.md): Booting a client that joins a real server offline, with character creation and the vanilla render pipeline
- [Smoke Testing a Mod](docs/smoke-test.md): `pharos smoke` and `ModSmokeTest`, in a terminal or in CI
- [Boot Diagnostics](docs/boot-diagnostics.md): The warnings a client or server logs while booting, `[StrictBoot]` and `[AllowBootDiagnostic]`
- [Parallel Runs](docs/parallel-runs.md): `pharos run --parallel`, which runs test classes in worker processes and merges their results
- [Test Worlds](docs/test-worlds.md): Schematics, saved worlds with `[ServerWorld(SaveFile = ...)]`, and `pharos fixture`
- [Failure Artifacts and the Watchdog](docs/failure-artifacts.md): The screenshot, logs, traffic and run details a failing scenario saves, and the timeout that stops a hung one
- [CI and Cloud Environments](docs/ci-and-cloud.md): Running the suite in CI and cloud environments, offline or online auth, and a dedicated server in Docker
- [Client-Server Testing](docs/writing-client-server-scenarios.md): `[ClientServerScenario]`, lockstep stepping, and packet assertions
- [Inspection API Reference](docs/inspection-api.md): Detailed API for all inspector classes
- [Compatibility Matrix](docs/compatibility.md): Supported VS versions, OpenGL requirements, and platform notes
- [Migrating from Atlas](docs/migrating-from-atlas.md): Drop-in shim and step-by-step migration guide
- [Roadmap](ROADMAP.md): Development history and phase completion status
- [Contributing](CONTRIBUTING.md): How to contribute to Pharos

## Status

v0.3.0 complete. All 36 issues across milestones M1 through M14 are merged. Packages are available on NuGet. See the [roadmap](ROADMAP.md) for the full history.

## License

[MIT](LICENSE)

## Support

If Pharos helps your mod development, consider supporting via [Ko-fi](https://ko-fi.com/zaldaryon).
