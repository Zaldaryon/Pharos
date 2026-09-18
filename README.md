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

[ServerWorld(seed: "12345", playStyle: "creativebuilding", worldType: "superflat")]
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
