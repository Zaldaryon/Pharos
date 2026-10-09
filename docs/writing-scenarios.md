# Writing Client Scenarios

This guide explains how to write client scenario tests using Pharos. A scenario is an xUnit test that runs against a real Vintage Story client in headless mode.

## Basic Setup

### 1. Install the Package

Add the Pharos XUnit metapackage to your test project:

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

This brings in `Zaldaryon.Pharos` and `xunit.core` as dependencies.

### 2. Create a Scenario Class

Inherit from `ClientScenarioBase` and use the `[ClientScenario]` attribute on test methods:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

public class ChunkLoadingTests : ClientScenarioBase
{
    [ClientScenario]
    public async Task InjectedChunkIsMeshed()
    {
        // The client is booted before the test runs. By default it is a fixture-mode client:
        // no server, chunks are injected and every frame is deterministic.
        ChunkPos target = new(10, 2, 10);
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(target)
            .WithDefaultSunlight(31)
            .Fill(0, 0, 0, 31, 1, 31, 1) // a layer of block id 1
            .Build();

        Client!.InjectChunk(fixture, triggerTesselation: true);

        Assert.True(await Client.WaitForChunkMeshed(target, maxFrames: 10));
    }
}
```

To test against a real world instead, boot the client in engine mode and connect it to a server (see [Engine Mode](engine-mode.md)), or use a `ClientServerScenarioBase`, which does both for you:

```csharp
public class MyTests : ClientScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine };
}
```

### 3. Run Tests

Run tests with the standard dotnet test command:

```bash
dotnet test -c Release
```

On Linux without a display, use the headless script:

```bash
./scripts/run-headless-linux.sh
```

## ClientScenarioBase

The base class provides access to the headless client and its subsystems.

### Protected Properties

| Property | Type | Description |
|----------|------|-------------|
| `Client` | `HeadlessClient?` | The headless client instance |
| `Player` | `IClientTestPlayer?` | Test player for position and camera control |
| `FrameController` | `DeterministicFrameController?` | Frame stepping API |
| `IsolationMode` | `IsolationMode` | Override to change isolation behavior |

### Frame Control

Use `FrameController` to advance the client deterministically:

```csharp
// Single frame with fixed timestep
FrameController!.Frame(0.016f);

// Multiple frames
FrameController.Frames(60);

// Advance until a condition is true
FrameController.FramesUntil(() => Client!.WorldMap.LoadedChunks > 10, maxFrames: 300);
```

### Player Control

Use `Player` to move the test player and control the camera:

```csharp
// Teleport to position
Player!.SetPosition(100, 80, 200);

// Point camera at a target
Player.LookAt(100, 64, 300);

// Get current position
var pos = Player.Position;
```

## Attributes

### [ClientScenario]

Marks a test method as a client scenario. Equivalent to xUnit's `[Fact]` but indicates the test uses the Pharos client lifecycle.

```csharp
[ClientScenario]
public void MyTest()
{
    // Test code here
}
```

### [ClientTheory]

Marks a data-driven test method. Equivalent to xUnit's `[Theory]`. Combine with `[InlineData]`, `[MemberData]`, or `[ClassData]` to run the same test with different inputs.

```csharp
[ClientTheory]
[InlineData(0, 100, 0)]
[InlineData(1000, 80, 1000)]
[InlineData(-500, 120, 500)]
public void ChunksLoadAtPosition(int x, int y, int z)
{
    Player!.SetPosition(x, y, z);
    FrameController!.Frames(60);
    
    Assert.True(Client!.WorldMap.LoadedChunks > 0);
}
```

### [PharosMods]

Declares mod paths to stage into a mod folder the client loads from, before it boots. Apply to the test class or assembly. Only an engine-mode client runs the mod loader.

```csharp
[PharosMods("mods/MyMod.zip", "mods/DependencyMod")]
public class ModCompatibilityTests : ClientScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine };
}
```

Mod paths can be:
- Absolute paths to mod directories, zip files or dll files
- Paths relative to the test's working directory (the test assembly's output folder under `dotnet test`)
- Multiple paths via the params array

The staged copy is deleted together with the client.

### Game versions

`[RequireGameVersion]` skips a scenario, or every scenario of a class, on other installs. The skip reason names the range and the installed version:

```csharp
[ClientScenario]
[RequireGameVersion(">=1.22.0", Reason = "Uses the 1.22 calendar API.")]
public void SeasonsFollowTheNewCalendar() { ... }
```

- **Ranges:** `>=1.22.0`, `>1.21.6`, `<=1.22.7`, `<1.23.0`, `=1.22.7`, or a bare `1.22.7`.
  - Commas join comparisons that must all hold: `>=1.21.0, <1.23.0`.
  - `||` joins alternatives.
  - `1.22.x`, `1.22.*`, a bare `1.22` and `=1.22` mean any 1.22 version. A comparison against `1.22` means `1.22.0`: `<=1.22` leaves out `1.22.7`.
- **Pre-releases** compare as versions: `>=1.22.0` leaves out `1.22.0-rc.3`, and `>=1.22.0-rc.1` takes it.
- **Several attributes** must all hold, on the method, its class and the base classes.
- **A range that cannot be read** fails the test instead of skipping it.
- **When the version cannot be read**, the test runs.
- **Plain xUnit tests** use `[GameVersionFact(">=1.22.0")]` and `[GameVersionTheory(...)]`. For them, a range that cannot be read skips the test with the reason, since xUnit drops a test whose attribute throws.
- **Fixtures still boot.** xUnit still creates the class and collection fixtures of a class whose tests are all skipped.

## Isolation Modes

By default, tests in a class share a single client instance for performance. Override `IsolationMode` to change this behavior. Scenarios run one at a time across the whole test run, because the game keeps process-wide static state.

### SharedClient (default)

All tests share one client. Fast, but tests can affect each other.

```csharp
public class FastTests : ClientScenarioBase
{
    // Uses SharedClient by default
}
```

### RollbackState

Tests share a client, but the player position and the camera (position, yaw, pitch) are put back to where they were when the client was first booted, before every test.

```csharp
public class IsolatedStateTests : ClientScenarioBase
{
    protected override IsolationMode IsolationMode => IsolationMode.RollbackState;
    
    [ClientScenario]
    public void FirstTest()
    {
        Player!.SetPosition(100, 100, 100);
        // State rolls back after this test
    }
    
    [ClientScenario]
    public void SecondTest()
    {
        // Player is back at the baseline position
    }
}
```

### FreshClient

Each test gets a completely new client instance. Slowest, but fully isolated.

```csharp
public class FullyIsolatedTests : ClientScenarioBase
{
    protected override IsolationMode IsolationMode => IsolationMode.FreshClient;
    
    [ClientScenario]
    public void TestA()
    {
        // Fresh client, no state from other tests
    }
    
    [ClientScenario]
    public void TestB()
    {
        // Another fresh client
    }
}
```

## Using Inspectors

Create inspectors to examine rendering state. See [Inspection API Reference](inspection-api.md) for details.

```csharp
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Memory;

public class InspectionTests : ClientScenarioBase
{
    [ClientScenario]
    public void MeasureDrawCalls()
    {
        var proxy = new GlCommandProxy();
        proxy.Enable();
        
        FrameController!.Frames(60);
        proxy.Reset();
        FrameController.Frames(10);
        
        var record = proxy.Snapshot();
        proxy.Disable();
        
        Assert.True(record.TotalDrawCalls > 0);
    }
    
    [ClientScenario]
    public void CheckCulling()
    {
        var inspector = new CullingInspector(Client!.ClientMain);
        
        FrameController!.Frames(30);
        var snapshot = inspector.Snapshot();
        
        Assert.True(snapshot.VisibleChunks.Count > 0);
    }
}
```

## Visual Regression Tests

Capture framebuffer pixels and compare against baseline images:

```csharp
public class VisualTests : ClientScenarioBase
{
    [ClientScenario]
    public void SceneMatchesBaseline()
    {
        // Set up scene
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(50, 64, 50);
        FrameController!.Frames(90);
        
        // Capture and compare
        var snapshot = Client!.Framebuffer.Capture();
        var baseline = FramebufferSnapshot.FromFile("baselines/scene.png");
        var result = snapshot.Compare(baseline, tolerance: 0.02f);
        
        // Save actual on failure for debugging
        if (result.DiffPixelCount > 50)
        {
            snapshot.SaveToPng("artifacts/scene-actual.png");
        }
        
        Assert.True(result.DiffPixelCount < 50,
            $"Visual diff: {result.DiffPixelCount} pixels exceed tolerance");
    }
}
```

## Best Practices

### Keep Tests Focused

Each test should verify one behavior. Avoid combining multiple assertions about different subsystems in a single test.

### Allow Time for Loading

The client needs frames to load chunks, mesh geometry, and stabilize. Use `FramesUntil` to wait for specific conditions rather than hardcoding frame counts.

```csharp
FrameController!.FramesUntil(
    () => Client!.WorldMap.LoadedChunks >= 50,
    maxFrames: 600);
```

### Clean Up Inspectors

Always call `Disable()` on inspectors that use Harmony patches. Use try/finally if the test might throw:

```csharp
var proxy = new GlCommandProxy();
proxy.Enable();
try
{
    // Test code
}
finally
{
    proxy.Disable();
}
```

### Use Descriptive Names

Test method names should describe the scenario and expected outcome:

```csharp
// Good
public void ChunksOutsideFrustum_AreCulled()

// Bad
public void Test1()
```

### Save Artifacts on Failure

When visual tests fail, save the actual output for debugging:

```csharp
if (!result.IsMatch)
{
    Directory.CreateDirectory("artifacts");
    snapshot.SaveToPng($"artifacts/{TestContext.TestName}-actual.png");
}
```
