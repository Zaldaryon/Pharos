# Inspection API Reference

Pharos provides several inspection classes that expose the client's rendering internals to test code. Each inspector targets a specific subsystem and uses Harmony patches or reflection to capture state without modifying the game's behavior.

## Client internals: renderers, tick listeners, particles and highlights

These read an engine-mode client's own registries while it plays: what a mod registered, and
what it does frame by frame. A fixture-mode client runs none of it, and these throw
`InvalidOperationException` there. Each record names the mod its code belongs to, by the
assembly the code is in. The game's own content counts as its mods' (`essentials`, `survival`,
`creative`). `null` means the engine's own systems, or a helper of the game's API that a mod uses,
such as a block entity's animation renderer.

### Renderers

```csharp
RendererInfo mine = Assert.Single(Client.Renderers.Of("mymod"));
Assert.Equal(EnumRenderStage.Opaque, mine.Stage);
Assert.Equal(30, await Client.Renderers.CountCallsAsync(mine, frames: 30));
```

- **Listing.** `All()`, `Of(modId)` and `At(stage)` list renderers in the order the game calls
  them. Each `RendererInfo` gives:
  - `Stage`, `Index`, `RenderOrder` and `RenderRange`;
  - `ProfilingName`, the name the mod registered it with;
  - `TypeName`: for a `DummyRenderer`, the type and method of its action;
  - `Mod`.
- **Counting.** `CountCallsAsync(renderer, frames)` and `CountCallsAsync(frames)` (every renderer)
  step the frames, the whole session's in a client-server test, and count each stage the game
  runs.
- **Conditional stages.** Some stages do not run every frame:
  - `Ortho` stops while the GUI is hidden;
  - `OIT` needs the transparent pass on;
  - the shadow stages need shadows on;
  - nothing renders before the player and the blocks around them have loaded.

### Tick listeners and callbacks

```csharp
TickListenerInfo listener = Assert.Single(Client.TickListeners.Of("mymod"));
Assert.Equal(50, listener.IntervalMs);
int calls = await Client.TickListeners.CountCallsAsync(listener, frames: 120);
```

- **Listing.** `All()`, `Of(modId)` and `Callbacks()` list plain and block listeners and delayed
  callbacks. Each gives its id, its interval or time left (`DueInMs`), its block position, its
  handler (type and method) and its mod.
- **Timing.** The client runs a listener once its interval has passed on its own clock, which
  follows real time, not the frames' `dt`: how many calls some frames make depends on how long
  they take. Assert against what the mod counted over the same frames, or that there was at
  least one call.
- **Counting.** `CountCallsAsync(frames)` counts every listener at once. While a count runs, each
  listener's handler is wrapped and `All()` still shows the original.

### Particles

```csharp
using (ParticleCapture capture = Client.Particles.Capture())
{
    await Client.Commands.ExecuteSuccessAsync(".mymod sparks");
    await Session.StepFramesAsync(2);
    SpawnedParticles sparks = Assert.Single(capture.Spawned, p => p.Color == SparkColor);
    Assert.Equal(EnumParticleModel.Quad, sparks.Model);
}
Assert.True(Client.Particles.Alive(EnumParticleModel.Quad) > 0);
```

- **Alive.** `Alive(model)` counts the particles alive on the main thread and the particle thread
  together, from the pools' own lists, so it is current whether or not particles render.
- **Capture.** `Capture()` records each spawn while it is open: from the mod, from the server,
  block breaking, entities and weather.
  - Ambient particles spawn all the time: filter `Spawned` by type, color or position, or pass
    `includeOffThread: false` to leave out the particle thread's.
  - A spawn is recorded when its pool takes it, with what the pool took (`Spawned`), which can
    be fewer than asked when the particle setting limits them.
  - The values are copied then, because the game reuses and changes properties objects.
  - For `SimpleParticleProperties` they include position, quantity, color, life length and size.
  - `limit` caps how many spawns are kept; `Truncated` says when it cut some.

### Highlights

```csharp
ClientHighlight marked = Client.Highlights(slot: 9)!;
Assert.Equal(expectedBlocks, marked.Positions);
```

- **Reading.** `Highlights(slot)` gives the blocks a slot highlights, with their colors (empty for
  the game's default), mode, shape and scale. That covers the server's highlight packets and the
  client's own `HighlightBlocks`.
- **Empty and cleared slots.** It returns null for a slot nothing highlighted in. A cleared slot,
  from either side, has no positions.
- **Slots.** `HighlightSlots()` lists the slots.

## Items: tooltips and icons

`client.Items` gives an engine-mode client's items. It covers stacks by code, the tooltips the game
builds for them, and their GUI icons, rendered as the game's own `.exponepng` renders them.

```csharp
ItemStack sword = client.Items.Stack("mymod:sword-iron");
Assert.Contains("Durability: 500 / 500", client.Items.Tooltip(sword).Lines);
GoldenImageAssertion.Assert(client.Items.RenderIcon(sword, 64), "Goldens/sword-iron.png");
```

- **`Stack(code, quantity, type)`** finds the item or block with the code. The domain is "game"
  when the code has none. When an item and a block share a code, `type` picks one.
- **`Tooltip(stack, extendedInfo)`** builds the tooltip as the game does: the stack's name as
  `Title`, and the description its collectible builds (`GetHeldItemInfo`) as `Text`.
  - `PlainText` and `Lines` give the description without its markup.
  - `extendedInfo` adds the lines the game shows with extended debug info on, such as the code.
  - The caller's stack is not changed.
- **`RenderIcon(stack, size)`** renders the stack's GUI icon into an offscreen framebuffer and
  returns a `FramebufferSnapshot`.
  - The icon is rendered in the GUI pass of a frame, so each call advances the client by one
    frame.
  - It is drawn at GUI scale 1, whatever the client's setting.
  - The frame's GL state and matrices are put back afterwards.
  - It throws when the stack has no GUI model.
- **`RenderIcons(domain)`** renders every block and item of a domain that is in a creative
  inventory tab (all of them with `creativeOnly: false`), up to 256 per frame.
- **`SaveIcons(domain, directory)`** writes each one to `block/<path>.png` or `item/<path>.png`,
  for making golden images.

The pixels are the framebuffer's, top row first, RGBA. Opaque and fully transparent pixels match
the game's export at GUI scale 1. Edges blended over the transparent background do not match it byte for byte,
so make goldens with `SaveIcons` or `RenderIcon`, not with `.exponepng`.

## Frame measurements

`Session.MeasureFramesAsync(frames)` steps a client-server session and measures it; `Client.MeasureFramesAsync(frames)` measures a client on its own. Engine mode only.

```csharp
await Session.StepFramesAsync(30);                       // warm up: the first frames compile code
FrameMeasurement before = await Session.MeasureFramesAsync(300);
await Client.Commands.ExecuteSuccessAsync(".mymod effects on");
FrameMeasurement with = await Session.MeasureFramesAsync(300);
Assert.True(with.Median - before.Median < TimeSpan.FromMilliseconds(2), with.ToString());
```

A `FrameMeasurement` gives:

- **`Work`.** The client's work in each frame, as wall-clock time on the client thread, in a `TimingStats`: min, median, P95, P99, max, mean, total, and every sample. `Median`, `P95` and the rest are on the measurement too.
  - Percentiles take the nearest rank, so P99 of fewer than 100 frames is the slowest.
  - Pharos steps frames itself, so there is no idle wait to leave out.
- **Where the time went.**
  - `Stages`: each render stage's time per frame, with the frames it ran in.
  - `Renderers`: each renderer's time per frame and calls, mods' and the engine's; `Of(modId)` filters them.
  - `GameTick`: the client's tick listeners.
  - `MainThreadTasks`: queued client-thread work, Pharos's and the game's, received packets among it.
  - `Other`: input, GUI, culling, and what lies between the stages.
- **Allocations.**
  - `AllocatedBytes` and `AllocatedBytesPerFrame` count the client thread only.
  - `ProcessAllocatedBytes` covers every thread.
  - The `Gen0`, `Gen1` and `Gen2` collection counts are the process's.
- **`ServerTicks`.** In a session measurement, the embedded server's work per tick and what its thread allocated. Ticks while the server is suspended (as during an autosave) are left out of the times and counted in `SuspendedTicks`.
  - Each tick is timed up to where the server sleeps out the rest of it, so its 33 ms tick rate is not counted.
  - It is null for `Client.MeasureFramesAsync`.

What the times leave out:
- work on other threads: chunk tessellation, the network thread, async particles;
- the GPU: GL calls return before drawing is done, so a renderer is charged for submitting its work, and one that waits on the GPU can be charged for the renderers before it.

The hooks change nothing the game does, and record nothing outside a measurement.

### A CI gate

Prefer comparing two measurements in the same run, as above: both run on the same machine. For a checked-in baseline, `FrameBaseline` uses the baselines file `pharos benchmark` uses:

```csharp
FrameMeasurement measured = await Session.MeasureFramesAsync(300);
FrameBaseline.Assert(measured, "pharos-baselines.json", "mymod.village.alloc", FrameStat.AllocatedBytesPerFrame, tolerance: 0.10);
FrameBaseline.Assert(measured, "pharos-baselines.json", "mymod.village.median", FrameStat.Median, tolerance: 0.50);
```

- **Writing baselines.** Run once with `PHAROS_UPDATE_BASELINES=1` to write the measured values. Other keys in the file are kept.
- **Choosing a stat.** Allocations per frame hardly vary between machines, so they suit a tight tolerance. Times need a generous one, and a baseline written on a machine like the one that checks it.
- **Several writers.** Updates take a lock other processes see, so parallel workers can update one file. `pharos benchmark --update-baselines` keeps the keys it does not own.

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
| `BufferAllocations`, `BufferDeletions` | Buffers created and deleted, one per id |
| `VertexArrayAllocations`, `VertexArrayDeletions` | Vertex arrays created and deleted |
| `TextureAllocations`, `TextureDeletions` | Textures created and deleted |
| `FramebufferAllocations`, `FramebufferDeletions` | Framebuffers created and deleted |
| `RenderbufferAllocations`, `RenderbufferDeletions` | Renderbuffers created and deleted |
| `TotalDrawCalls` | Sum of all draw call types |

Objects are counted by hooks installed before anything boots, so a call the runtime compiled early is seen too. `GlResourceLeakDetector` uses these counts for buffers, vertex arrays, textures, framebuffers and renderbuffers.

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

### OpenGL objects a test leaves behind

`client.Memory.TrackGlResources()` records every buffer, vertex array, texture, framebuffer and renderbuffer the client creates and deletes, by id, until it is disposed. `PharosAssert.NoGlLeaks(scope, kinds)` fails when some of them were created and not deleted:

```csharp
using GlResourceScope scope = Client!.Memory.TrackGlResources(new() { CaptureStacks = true });
for (int i = 0; i < 10; i++)
{
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
    await Session!.StepFramesAsync(5);
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
}
PharosAssert.NoGlLeaks(scope, GlResourceKind.VertexArray | GlResourceKind.Texture);
```

- **What the report holds.** `scope.Report(kinds)` lists each object left with its kind, id, the frame it was created in and, with `CaptureStacks` (or `PHAROS_GL_STACKS=1`), the code that created it. A reused id counts as a new object.
- **What is left out:**
  - objects the engine keeps for good: the textures of its block, item and entity texture sets, and its framebuffers with their textures;
  - deletions of objects created before the scope;
  - calls on other threads than the client's.
- **What the engine makes lazily.** It also creates objects for good the first time it draws something: an item's icon, a stack-size label, a dialog's textures. Open and close a dialog once before the scope, or create the objects in one call on the client thread with no frame between, so that only the test's own objects are counted.
- **Forms not tracked.** The `[Out]` array and pointer forms of `GL.Gen*`, such as `GenTextures(n, int[])`, cannot be hooked; neither the game nor its API calls them. `Gen*()`, `Gen*s(n, out id)` and every `Delete*` form are tracked. Deleting an object made through an untracked form counts as deleting one made before the scope.
- **One client at a time.** One client's objects are tracked at a time.
- **When the hooks fail.** If OpenGL object creation cannot be hooked, the client still boots and `TrackGlResources` throws with the reason.

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
