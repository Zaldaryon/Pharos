<p align="center">
  <img src="docs/pharos-logo.svg" alt="Pharos logo" width="120" height="120" />
</p>

# Pharos

In-process headless client test harness for Vintage Story. Pharos boots a real Vintage Story client without a window, drives it frame by frame, and exposes rendering inspection APIs to xUnit test code.

## What It Does

Pharos runs a Vintage Story client inside your test process. The client loads chunks, meshes geometry, and issues OpenGL draw calls exactly like the normal client, but with no visible window and no audio. Your test code controls the frame loop, positions the camera, inspects rendering state, and asserts on the results.

Use cases:

- Rendering regression tests for client-side mods
- Performance benchmarks that measure actual draw calls and allocations
- Visual diff tests comparing framebuffer output across versions
- Automated smoke tests for mod compatibility

## Requirements

- Vintage Story 1.22.x (1.22.7 recommended)
- .NET 10 SDK
- OpenGL 4.3+ for full inspection features (3.3 for basic headless)
- Windows or Linux (macOS not supported)

See the [compatibility matrix](docs/compatibility.md) for version details.

## Installation

Add the Pharos XUnit metapackage to your test project:

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

This brings in everything you need: the core library, xUnit integration, and xUnit itself.

Set the `VINTAGE_STORY` environment variable to your game installation:

```bash
# Windows (PowerShell)
$env:VINTAGE_STORY = "C:\Program Files\Vintage Story"

# Linux
export VINTAGE_STORY=~/.local/share/vintagestory
```

## Quick Start

Create a test class that inherits from `ClientScenarioBase` and mark test methods with `[ClientScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

public class ChunkRenderingTests : ClientScenarioBase
{
    [ClientScenario]
    public void ClientLoadsChunksAroundPlayer()
    {
        // Position the test player
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(32, 64, 32);
        
        // Advance frames to let chunks load
        FrameController!.Frames(60);
        
        // Assert on world state
        Assert.True(Client!.WorldMap.LoadedChunks > 0);
    }
}
```

Run tests with the standard dotnet command:

```bash
dotnet test -c Release
```

On Linux without a display, use the headless script:

```bash
./scripts/run-headless-linux.sh
```

See the [scenario writing guide](docs/writing-scenarios.md) for detailed examples.

## Packages

| Package | Description |
|---------|-------------|
| [Zaldaryon.Pharos](https://www.nuget.org/packages/Zaldaryon.Pharos) | Core headless client bootstrap and frame stepper |
| [Zaldaryon.Pharos.Bridge](https://www.nuget.org/packages/Zaldaryon.Pharos.Bridge) | In-client mod exposing inspection hooks to scenario code |
| [Zaldaryon.Pharos.XUnit](https://www.nuget.org/packages/Zaldaryon.Pharos.XUnit) | xUnit integration with `[ClientScenario]` and lifecycle management |

Install `Zaldaryon.Pharos.XUnit` to get all three packages as dependencies.

## Platform Support

| Platform | Method | Notes |
|----------|--------|-------|
| Windows | Hidden GLFW window with FBO | Works on any GPU |
| Linux | Mesa llvmpipe under Xvfb | Works in CI without a GPU |
| macOS | Not supported | GLFW cannot create headless OpenGL contexts |

Both Windows and Linux produce identical rendering behavior and report OpenGL 4.5 core profile.

## Inspection APIs

Pharos exposes several inspection classes for asserting on rendering internals:

| Class | Purpose |
|-------|---------|
| `GlCommandProxy` | Count draw calls, buffer allocations, and VAO operations |
| `CullingInspector` | Query which chunks are visible, frustum-culled, or occlusion-culled |
| `MemoryInspector` | Track mesh pool hit/miss rates and type allocations |
| `ShaderInspector` | Intercept uniform uploads and detect redundant state changes |
| `FramebufferSnapshot` | Capture pixels for visual regression comparisons |

See the [inspection API reference](docs/inspection-api.md) for method signatures and examples.

## Documentation

- [Writing Scenarios](docs/writing-scenarios.md): How to use `[ClientScenario]`, `[ClientTheory]`, `[PharosMods]`, and isolation modes
- [Inspection API Reference](docs/inspection-api.md): Detailed API documentation for all inspector classes
- [Compatibility Matrix](docs/compatibility.md): Supported VS versions, OpenGL requirements, and platform notes
- [Roadmap](ROADMAP.md): Development plan and issue tracking
- [Contributing](CONTRIBUTING.md): How to contribute to Pharos

## Status

Pre-release. All 22 issues for v0.1.0 are complete. Packages are available on NuGet. See the [roadmap](ROADMAP.md) for the development history.

## License

[MIT](LICENSE)

## Support

If Pharos helps your mod development, consider supporting via [Ko-fi](https://ko-fi.com/zaldaryon).
