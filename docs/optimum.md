# Testing on Optimum builds

Optimum is a patched Vintage Story build. It ships its own `VintagestoryAPI.dll` and patches `VintagestoryLib.dll`, and its API carries a static `Vintagestory.API.Config.OptimumDiagnostics` class with its counters. Pharos runs on an Optimum install like on any other: point `VINTAGE_STORY` at it and build the tests against it.

## Which build the tests run on

- `OptimumInstall.Loaded` is the Optimum build the tests loaded, or null on vanilla. `OptimumInfo.Version` is Optimum's own version, read from a `Version`, `OptimumVersion` or `BuildVersion` member of its diagnostics type, or `"unknown"` when the build does not say. `OptimumInfo.LibPatched` says whether the engine library carries Optimum's patches.
- `OptimumInstall.Detect(installDir)` reads an install without loading it.
- Every scenario's output starts with the build, such as `Vintage Story 1.22.7 (Optimum 0.9.1), Pharos 0.6.0`. `pharos run` prints it too, and its JSON `started` event carries `optimum`. A failure's `run.json` has an `optimum` field: `{ "version", "libPatched" }`, or null on vanilla.
- **Mismatched builds.** Tests built against vanilla and run on Optimum, or the reverse, stop with a message saying to build them again, unless `PHAROS_ALLOW_GAME_MISMATCH=1`. An install whose API is Optimum's but whose engine library is not patched gets a warning.

## Running a test only on Optimum, or never on it

```csharp
[RequireOptimum(Reason = "measures Optimum's tessellation counters")]
public sealed class TessellationTests : ClientScenarioBase
{
    [ClientScenario]
    public async Task Counts_Chunks() { ... }
}

[ClientScenario]
[SkipOnOptimum(Reason = "Optimum replaces the vanilla tessellator")]
public async Task Vanilla_Tessellator() { ... }
```

- Both attributes go on a method or a class, and apply to the Pharos scenario attributes. The skip reason names the install.
- A test that both attributes reach, one on the class and one on the method included, fails rather than never running.
- Plain xUnit tests use `[OptimumFact]`, `[OptimumTheory]` and `[NotOptimumFact]`; `[RequireOptimum]` and `[SkipOnOptimum]` do nothing on a plain `[Fact]`.

## Optimum's counters

```csharp
[ClientScenario]
[RequireOptimum]
public async Task Moving_TessellatesChunks()
{
    OptimumDriver optimum = Client!.Optimum;
    using OptimumWindow window = optimum.Measure();
    await Session!.StepFramesAsync(120);
    Assert.True(window.Delta("tessellation.chunks") > 0, optimum.Summary());
}
```

- `client.Optimum` throws on vanilla, with a message saying to add `[RequireOptimum]`; `client.HasOptimum` checks without throwing. `server.Optimum` gives the same counters: they belong to the whole process.
- **`Counters`** are Optimum's public static numeric fields, by name, and the `key=value` pairs of each `Get*Summary()` method, named after the method: `GetTessellationSummary()` returning `chunks=4, uploads=2` gives `tessellation.chunks` and `tessellation.uploads`. Pairs may be separated by commas, semicolons or spaces, and a unit after the number (`1.5ms`, `40%`) is dropped. A field or summary that throws is left out. `Counter(name)` reads one, and an unknown name throws listing the counters Optimum has.
- **`Measure()`** opens a window: `Delta(name)` says how much a counter grew since. Optimum's state is left alone, so a delta can be negative if Optimum resets its counters during the window.
- **`Reset()`** runs Optimum's own `Reset` or `ResetCounters` when it has one, and returns true; otherwise it leaves Optimum alone and returns false. Either way the counters then read relative to their values now, so every counter starts from zero.
- **`Summary()`** joins every `Get*Summary()`, one per line, for a failure message.
- **`Command(arguments)`** runs Optimum's `.optimum` client command and returns its `ClientCommandResult`.

## In CI

Optimum is not downloadable from a fixed address, so the weekly compatibility run covers vanilla versions only. To test on Optimum in a mod's CI, extract the Optimum build over a copy of the game set up by the `setup-vintage-story` action (see "Using Pharos in your mod's CI" in `ci-and-cloud.md`), then build and run the tests against it.
