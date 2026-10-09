# Compatibility Matrix

Pharos runs inside a real Vintage Story client, so it depends on the game's runtime, rendering API, and internal structures. This document lists tested configurations and known limitations.

## Vintage Story Versions

| VS Version | .NET Runtime | Support Level | Notes |
|------------|--------------|---------------|-------|
| 1.22.7 | .NET 10 | Primary | Fully tested, CI verified |
| 1.22.0 to 1.22.6 | .NET 10 | Best effort | Should work, not CI tested |
| 1.21.x | .NET 8 | Best effort | API differences possible |

Primary support means the CI pipeline tests against that version on every commit. Best effort means it should work but is not continuously verified.

### Checked every week

`.github/workflows/compat.yml` builds Pharos against several game versions and runs its live suite on each, every Monday and on demand (Actions, "Compatibility", "Run workflow", with a JSON array of versions such as `["1.21.6","1.22.0","1.22.7"]`). Each version builds against its own install, so a version whose API moved shows as a failed build rather than as failed tests. The run's summary and its `compatibility-table` artifact hold the table: the build, the passed, failed and skipped tests, and the first failing tests of each version. To update the table below, copy it from the latest run. A pre-release such as `1.22.0-rc.3` is downloaded from the unstable channel.

### Tests that need a version

`[RequireGameVersion(">=1.22.0")]` on a scenario or its class skips it on other installs, saying which range it needs and which version is installed. `[GameVersionFact]` and `[GameVersionTheory]` do the same for plain xUnit tests. See "Game versions" in `writing-scenarios.md`.

Every scenario's output starts with the game and Pharos versions it ran with, `pharos run` prints the install it uses, and a failure's `run.json` names the version and the install.

Pharos follows Vintage Story releases. When VS 1.23 ships, 1.22.x becomes best effort and 1.21.x drops to unsupported.

## OpenGL Requirements

| Feature | Minimum | Recommended | Notes |
|---------|---------|-------------|-------|
| Core profile | 3.3 | 4.5 | Basic headless rendering |
| Indirect draw | 4.3 | 4.5 | `GlCommandProxy` indirect call counting |
| Compute shaders | 4.3 | 4.5 | Future inspection features |

Most discrete GPUs from 2012 or later support OpenGL 4.5. Software rendering via Mesa llvmpipe reports OpenGL 4.5 and passes all Pharos tests.

## Platform Support

| Platform | Rendering | Status | Notes |
|----------|-----------|--------|-------|
| Windows | Hidden GLFW window with FBO | Supported | Requires any GPU or software fallback |
| Linux | Xvfb with Mesa llvmpipe | Supported | No GPU needed, works in CI |
| macOS | N/A | Not supported | GLFW cannot create headless contexts |

### Windows

Pharos creates a hidden GLFW window with an offscreen FBO. The window never becomes visible but must exist for OpenGL context creation. Any GPU driver that supports OpenGL 4.3+ works.

### Linux

Pharos uses Xvfb (X Virtual Framebuffer) with Mesa's llvmpipe software rasterizer. This allows running tests on headless CI runners without a GPU. Install the required packages:

```bash
# Debian/Ubuntu
sudo apt install xvfb mesa-utils libosmesa6

# Run tests under Xvfb with software rendering
./scripts/run-headless-linux.sh
```

The script sets `LIBGL_ALWAYS_SOFTWARE=1` and runs tests inside `xvfb-run`.

### macOS

Pharos does not support macOS. Apple deprecated OpenGL on macOS in 2018, and GLFW cannot create offscreen OpenGL contexts on macOS without workarounds that break with each OS update. If you need macOS support, open an issue describing your use case.

## Known Limitations

Pharos captures the client's rendering state through reflection and Harmony patches. Some client internals change between Vintage Story versions, so tests that rely on specific field names or method signatures may break on version updates.

The test process runs a real VS client. Memory usage is similar to playing the game. Expect 2 to 4 GB of RAM consumption during test execution, more with multiple scenarios or large world fixtures.

Framebuffer readback uses `glReadPixels`, which stalls the GPU pipeline. Visual regression tests that capture every frame will be slower than tests that only check draw call counts.
