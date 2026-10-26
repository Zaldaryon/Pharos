# Platform Loop Stepping

By default, Pharos steps frames through `ScreenManager.Render`, which is deterministic and fast but
bypasses the platform's frame timing logic. For testing frame pacing, VSync behavior, background FPS
limits, and any Optimum/OptiTime patches on platform-loop paths, use `PlatformLoopStepper`.

## Quick Start

```csharp
// Access the platform loop stepper from an engine-mode client
PlatformLoopStepper platformLoop = Client.PlatformLoop;

// Check if platform loop stepping is supported
if (!platformLoop.IsSupported)
{
    // Handle unsupported case with machine-readable reason
    Console.WriteLine($"Platform loop not supported: {platformLoop.UnsupportedReason}");
    return;
}

// Step one frame via the full platform loop
PlatformStepResult result = platformLoop.Step();

if (result.Succeeded)
{
    Console.WriteLine($"Frame {result.FrameNumber}: {result.MeasuredDuration:F4}s");
    Console.WriteLine($"Frame pacing active: {result.FramePacingActive}");
    Console.WriteLine($"VSync mode: {result.VsyncMode}");
}
```

## When to Use

Use platform loop stepping when testing:

- **Frame pacing**: The MaxFps limiter uses `Thread.Sleep` between frames
- **VSync behavior**: How the client behaves with VSync on vs off
- **Background FPS**: Reduced FPS when the game is not focused
- **Optimum/OptiTime patches**: Optimizations that hook into the platform loop

For most tests, the default `FrameAsync` is preferred because it's deterministic and doesn't
depend on real timing.

## Capability Detection

The stepper provides machine-readable capability detection:

```csharp
PlatformLoopStepper platformLoop = Client.PlatformLoop;

// Check support before stepping
Assert.True(platformLoop.IsSupported, platformLoop.UnsupportedReason);

// Or handle gracefully
PlatformStepResult result = platformLoop.Step();
if (!result.Supported)
{
    Skip.If(true, result.UnsupportedReason);
}
```

## Frame Pacing State

Query the current frame pacing configuration:

```csharp
FramePacingSnapshot pacing = platformLoop.GetPacingState();

Console.WriteLine($"VSync mode: {pacing.VsyncMode}");         // 0=off, 1=on
Console.WriteLine($"Max FPS: {pacing.MaxFps}");               // 15-241
Console.WriteLine($"Pacing active: {pacing.IsPacingActive}"); // true when VSync off + FPS limited
Console.WriteLine($"Total frames: {pacing.FrameCount}");
Console.WriteLine($"Total time: {pacing.TotalTime:F2}s");
```

Frame pacing is active when:
- VSync is off (`VsyncMode != 1`)
- MaxFps is between 10 and 241 (exclusive)

## Step Results

Every step returns a `PlatformStepResult` with:

| Property | Type | Description |
|----------|------|-------------|
| `Succeeded` | bool | Whether the step completed |
| `Supported` | bool | Whether platform stepping is available |
| `FrameNumber` | long | 1-based frame count |
| `MeasuredDuration` | double | Actual frame time in seconds |
| `RequestedDt` | float | Requested delta time |
| `FramePacingActive` | bool | Whether MaxFps limiter ran |
| `VsyncMode` | int | VSync setting (0=off, 1=on, -1=unknown) |
| `UnsupportedReason` | string? | Why stepping is not supported |
| `Exception` | Exception? | Error that caused failure |

## Multiple Frames

Step multiple frames at once:

```csharp
IReadOnlyList<PlatformStepResult> results = platformLoop.StepFrames(10);

foreach (var result in results)
{
    if (!result.Succeeded) break;
    Console.WriteLine($"Frame {result.FrameNumber}: {result.MeasuredDuration:F4}s");
}
```

## Differences from FrameAsync

| Aspect | FrameAsync | PlatformLoop.Step |
|--------|------------|-------------------|
| Timing | Deterministic dt | Real wall-clock |
| Frame pacing | Not exercised | Exercised |
| VSync | Not exercised | Exercised |
| Thread.Sleep | Not called | Called when pacing |
| Optimum paths | Render-only | Full platform loop |
| Test isolation | High | Medium |

## Requirements

- Engine mode client (`ClientBootMode.Engine`)
- Vintage Story version with accessible `window_RenderFrame` method

If requirements are not met, `IsSupported` returns false and `UnsupportedReason` explains why.
