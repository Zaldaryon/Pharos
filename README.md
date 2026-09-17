# Pharos

In-process headless client test harness for Vintage Story. Boots an embedded client without a window, drives it frame by frame, and exposes rendering inspection APIs to xUnit scenario code.

## Status

Pre-release. See the [v0.1.0 Roadmap](ROADMAP.md) for the development plan and [open issues](https://github.com/Zaldaryon/Pharos/issues) for current progress.

## What it does

Pharos runs a real Vintage Story client inside your test process. The client has no visible window and no audio, but it loads chunks, meshes geometry, and issues OpenGL draw calls exactly like the normal client. Your test code controls the frame loop, inspects rendering state, and asserts on the results.

Use cases:
- Rendering regression tests for client-side mods
- Performance benchmarks that measure actual draw calls and allocations
- Visual diff tests comparing framebuffer output across versions
- Automated smoke tests for mod compatibility

## Planned packages

| Package | Purpose |
|---------|---------|
| `Zaldaryon.Pharos` | Core headless client bootstrap and frame stepper |
| `Zaldaryon.Pharos.Bridge` | In-client mod exposing inspection hooks to scenario code |
| `Zaldaryon.Pharos.XUnit` | `[ClientScenario]` and `[ClientTheory]` attributes with lifecycle management |

## Platform support

Pharos targets Windows and Linux as first-class platforms:

- **Windows**: Hidden GLFW window with FBO, runs on any GPU
- **Linux**: Mesa llvmpipe software rasterizer under Xvfb, runs in CI without a GPU

Both platforms report OpenGL 4.5 core and produce identical rendering behavior.

## Requirements

- .NET 9 or later
- Vintage Story 1.21.x or 1.22.x (see [compatibility matrix](https://github.com/Zaldaryon/Pharos/issues/22) when published)

## Quick start

*Available after v0.1.0 release.*

```csharp
[ClientScenario]
public class ChunkRenderingTests : ClientScenarioBase
{
    [Fact]
    public void ChunksAreCulledOutsideFrustum()
    {
        // Position camera looking at a known chunk
        Client.Camera.Position = new Vec3d(0, 100, 0);
        Client.Camera.LookAt(new Vec3d(32, 64, 32));
        
        // Advance one frame
        Client.Frame(0.016);
        
        // Assert on culling state
        var culled = Client.Inspection.Frustum.CulledChunkCount;
        Assert.True(culled > 0, "Expected chunks outside frustum to be culled");
    }
}
```

## Project structure

```
Pharos/              Core library: headless bootstrap, frame stepper, inspection API
Pharos.Bridge/       Client-side mod: exposes internal state to test process
Pharos.XUnit/        xUnit integration: attributes, fixtures, lifecycle
tests/               Internal tests for Pharos itself
```

## Contributing

Pharos is in early development. The [roadmap](ROADMAP.md) defines the work order. Check the [project board](https://github.com/users/Zaldaryon/projects/1) for current status.

## License

[MIT](LICENSE)

## Support

If Pharos helps your mod development, consider supporting via [Ko-fi](https://ko-fi.com/zaldaryon).
