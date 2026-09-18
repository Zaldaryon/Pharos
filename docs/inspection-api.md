# Inspection API Reference

Pharos provides several inspection classes that expose the client's rendering internals to test code. Each inspector targets a specific subsystem and uses Harmony patches or reflection to capture state without modifying the game's behavior.

## GlCommandProxy

Records OpenGL draw and buffer commands executed during a frame.

### Purpose

Count draw calls, multi-draw calls, indirect draws, buffer allocations, and VAO allocations. Use this to verify that rendering optimizations reduce draw call counts or detect unexpected allocations during steady-state rendering.

### Key Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Enable()` | void | Installs Harmony patches to start recording |
| `Disable()` | void | Removes patches and stops recording |
| `Reset()` | void | Zeros all counters without changing enabled state |
| `Snapshot()` | `GlCommandRecord` | Returns an immutable snapshot of current counts |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsEnabled` | bool | True if patches are installed and recording |

### GlCommandRecord Fields

| Field | Description |
|-------|-------------|
| `DrawCalls` | glDrawArrays, glDrawElements, and instanced variants |
| `MultiDrawCalls` | glMultiDrawArrays, glMultiDrawElements |
| `IndirectDrawCalls` | glMultiDrawArraysIndirect, glMultiDrawElementsIndirect |
| `BufferAllocations` | glGenBuffer, glGenBuffers |
| `BufferDeletions` | glDeleteBuffer, glDeleteBuffers |
| `VertexArrayAllocations` | glGenVertexArray, glGenVertexArrays |
| `TotalDrawCalls` | Sum of all draw call types |

### Example

```csharp
[ClientScenario]
public class DrawCallTests : ClientScenarioBase
{
    [Fact]
    public void SteadyStateHasNoBatching()
    {
        var proxy = new GlCommandProxy();
        proxy.Enable();
        
        // Let the client stabilize
        FrameController!.Frames(60);
        
        proxy.Reset();
        FrameController.Frames(10);
        var record = proxy.Snapshot();
        
        proxy.Disable();
        
        Assert.True(record.IndirectDrawCalls > 0, "Expected indirect draw calls");
        Assert.Equal(0, record.BufferAllocations);
    }
}
```

## CullingInspector

Queries per-frame frustum culling state to identify which chunks are visible, frustum-culled, or occlusion-culled.

### Purpose

Verify that frustum and occlusion culling work correctly. Check that chunks outside the camera frustum are not drawn. Detect regressions where culling stops working.

### Key Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Snapshot()` | `CullingSnapshot` | Captures current visible/culled chunk lists |
| `GetFrustumPlanes()` | `IReadOnlyList<FrustumPlane>` | Returns the six frustum plane equations |

### CullingSnapshot Fields

| Field | Type | Description |
|-------|------|-------------|
| `VisibleChunks` | `IReadOnlyList<ChunkPos>` | Chunks inside the frustum |
| `CulledChunks` | `IReadOnlyList<ChunkPos>` | Chunks outside the frustum |
| `OcclusionCulledChunks` | `IReadOnlyList<ChunkPos>` | Chunks in frustum but hidden by cave culling |
| `FrustumPlanes` | `IReadOnlyList<FrustumPlane>` | The six frustum planes |
| `TestCount` | int | Number of chunks tested |
| `TotalChunks` | int | Total chunks in the world map |

### Example

```csharp
[ClientScenario]
public class CullingTests : ClientScenarioBase
{
    [Fact]
    public void ChunksOutsideFrustumAreCulled()
    {
        var inspector = new CullingInspector(Client!.ClientMain);
        
        // Position camera looking at specific area
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(32, 64, 32);
        FrameController!.Frames(30);
        
        var snapshot = inspector.Snapshot();
        
        Assert.True(snapshot.CulledChunks.Count > 0, "Expected some chunks to be culled");
        Assert.True(snapshot.VisibleChunks.Count > 0, "Expected some chunks to be visible");
    }
}
```

## MemoryInspector

Tracks mesh pool state and allocation counts for MeshData and ItemRenderInfo objects.

### Purpose

Detect memory leaks and verify that mesh pooling works. Confirm that steady-state rendering does not allocate new MeshData objects. Measure pool hit/miss rates.

### Key Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Enable()` | void | Installs allocation tracking patches |
| `Disable()` | void | Removes patches |
| `Reset()` | void | Zeros counters |
| `PoolSnapshot()` | `MeshPoolSnapshot` | Captures pool sizes and hit/miss counts |
| `AllocationSnapshot()` | `AllocationSnapshot` | Captures type allocation counts |
| `MeasureAllocations(Action)` | long | Measures bytes allocated by an action |

### MeshPoolSnapshot Fields

| Field | Type | Description |
|-------|------|-------------|
| `SmallPoolSize` | int | Small mesh pool count |
| `MediumPoolSize` | int | Medium mesh pool count |
| `LargePoolSize` | int | Large mesh pool count |
| `PendingRecycleCount` | int | Meshes awaiting return to pool |
| `Hits` | long | Pool requests satisfied from pool |
| `Misses` | long | Pool requests that allocated new |

### AllocationSnapshot Fields

| Field | Type | Description |
|-------|------|-------------|
| `ItemRenderInfoAllocations` | long | ItemRenderInfo constructor calls |
| `MeshDataAllocations` | long | MeshData constructor calls |

### Example

```csharp
[ClientScenario]
public class AllocationTests : ClientScenarioBase
{
    [Fact]
    public void SteadyStateHasNoAllocations()
    {
        var inspector = new MemoryInspector();
        inspector.Enable();
        
        // Warm up
        FrameController!.Frames(120);
        
        inspector.Reset();
        long bytes = MemoryInspector.MeasureAllocations(() =>
        {
            FrameController.Frames(60);
        });
        
        var allocs = inspector.AllocationSnapshot();
        inspector.Disable();
        
        Assert.Equal(0, allocs.MeshDataAllocations);
        Assert.True(bytes < 1024, $"Allocated {bytes} bytes during steady state");
    }
}
```

## ShaderInspector

Intercepts shader uniform uploads and texture bindings to detect redundant state changes.

### Purpose

Find shader uniform uploads that send the same value repeatedly. Verify that texture bindings are correct. Debug shader state issues.

### Key Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Enable()` | void | Installs shader interception patches |
| `Disable()` | void | Removes patches |
| `Reset()` | void | Clears recorded state |
| `Snapshot(int programId)` | `ShaderSnapshot` | Returns uniforms for a specific program |
| `GetRedundantUploadCount()` | int | Count of duplicate uniform uploads |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ActiveProgram` | `ShaderProgramBase?` | Currently bound shader program |
| `BoundTextures` | `IReadOnlyDictionary<int, int>` | Texture unit to texture ID mapping |

### Example

```csharp
[ClientScenario]
public class ShaderTests : ClientScenarioBase
{
    [Fact]
    public void NoRedundantUniformUploads()
    {
        var inspector = new ShaderInspector();
        inspector.Enable();
        
        FrameController!.Frames(60);
        
        int redundant = inspector.GetRedundantUploadCount();
        inspector.Disable();
        
        Assert.True(redundant < 100, $"Found {redundant} redundant uniform uploads");
    }
}
```

## FramebufferSnapshot

Captures and compares framebuffer pixel data for visual regression testing.

### Purpose

Read back rendered pixels for visual diff tests. Save screenshots during test execution. Compare rendered output against baseline images.

### Construction

Capture from the headless framebuffer after a frame step:

```csharp
var snapshot = Client!.Framebuffer.Capture();
```

Load from a PNG file:

```csharp
var baseline = FramebufferSnapshot.FromFile("baseline.png");
```

### Key Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `GetPixel(x, y)` | `(byte R, byte G, byte B, byte A)` | Reads a single pixel |
| `SaveToPng(path)` | void | Saves the snapshot as PNG |
| `Compare(other, tolerance)` | `FramebufferComparisonResult` | Pixel-level comparison |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `RawRgba` | `byte[]` | Raw RGBA pixel data, top-down |
| `Width` | int | Framebuffer width in pixels |
| `Height` | int | Framebuffer height in pixels |

### FramebufferComparisonResult Fields

| Field | Type | Description |
|-------|------|-------------|
| `MeanDiff` | float | Average difference across all pixels (0 to 1) |
| `MaxDiff` | float | Maximum single-channel difference (0 to 1) |
| `DiffPixelCount` | int | Pixels exceeding tolerance |
| `TotalPixels` | int | Total pixels compared |
| `Tolerance` | float | Tolerance used for comparison |

### Example

```csharp
[ClientScenario]
public class VisualRegressionTests : ClientScenarioBase
{
    [Fact]
    public void RenderedFrameMatchesBaseline()
    {
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(0, 64, 100);
        FrameController!.Frames(60);
        
        var snapshot = Client!.Framebuffer.Capture();
        var baseline = FramebufferSnapshot.FromFile("baselines/view-north.png");
        
        var result = snapshot.Compare(baseline, tolerance: 0.02f);
        
        if (result.DiffPixelCount > 0)
        {
            snapshot.SaveToPng("artifacts/view-north-actual.png");
        }
        
        Assert.True(result.DiffPixelCount < 100, 
            $"Visual regression: {result.DiffPixelCount} pixels differ");
    }
}
```
