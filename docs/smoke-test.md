# Smoke testing a mod

The first test every mod wants: does it load, join and play without errors, on the server and
on the client? Pharos runs it in two ways:
- `pharos smoke`, a command that needs no test project;
- `ModSmokeTest`, a scenario class a test project inherits with no body.

Both do the same thing:
1. Boot an embedded server with the mods, and a real engine-mode client with them too.
2. Join, run the commands you give, then walk forward, turn around, jump and look up and down for
   a number of frames.
3. After each step, check that the client is still in the world, the connection is up and the
   server is still running.

The run fails when:
- either side crashes, or the client is disconnected;
- a command fails;
- the client or the server logs an error;
- the play takes longer than its watchdog (five minutes);
- with `--strict` or `[StrictBoot]`, either side logged a warning while booting (see
  [Boot diagnostics](boot-diagnostics.md)).

A failed run saves its [failure artifacts](failure-artifacts.md): a screenshot, the logs, the
boot diagnostics and `run.json`.

## The `pharos` command

Install the tool, point it at your game, and run it under a display:

```bash
dotnet tool install -g Zaldaryon.Pharos.Cli
export VINTAGE_STORY=/path/to/vintagestory
xvfb-run -a pharos smoke --mod bin/Release/mymod.zip --strict
```

| Option | Meaning |
|--------|---------|
| `--mod <path>` | A mod to load: a folder, a `.zip` or a `.dll`. Repeat it for several mods. Required. |
| `--frames <n>` | Frames to play, the server ticking once per frame. 600 by default, ten seconds of game time. |
| `--command <text>` | A server console command to run after the join, as an admin. Repeatable. Each must succeed. |
| `--strict` | Fail on any warning logged while booting. |
| `--allow <regex>` | A boot warning that `--strict` lets pass. Repeatable. |
| `--allow-error <text>` | A fragment of a logged error to let pass. Repeatable. |
| `--timeout <seconds>` | How long the play may take, 300 by default, scaled by `PHAROS_TIMEOUT_SCALE`. |
| `--artifacts <dir>` | Where failure artifacts and `smoke.log` go: `$PHAROS_ARTIFACTS`, else `./pharos-artifacts`. |
| `--game <dir>` | The game install, instead of `$VINTAGE_STORY`. |
| `--seed`, `--world-type`, `--play-style` | The world. Superflat and creative building by default, which boot fast but skip world generation and survival systems: use `--world-type standard --play-style surviveandbuild` to cover them. |
| `-v`, `--verbose` | Show the game's own output instead of writing it to `smoke.log`. |

| Exit code | Meaning |
|-----------|---------|
| 0 | The smoke test passed. |
| 1 | The smoke test failed. The output says why, and the artifacts show it. |
| 2 | It could not run: bad arguments, no game install, no display, or a mod that does not exist. |

Besides the play's own watchdog, a whole run (boot and teardown included) is given up after the
play's timeout plus ten minutes, both scaled by `PHAROS_TIMEOUT_SCALE`, so a mod that hangs while
loading cannot hang a CI job. Stopping `pharos` (Ctrl+C, or a CI runner's timeout) stops the game
it started too.

### Your game, not ours

The tool ships none of Vintage Story's files. You need your own copy of the game. On its first
run against an install, the tool builds a folder holding its own files and links to the
install's assemblies and native libraries, laid out the way the game expects them. It runs from
that folder from then on. The folder lives under the local application data folder
(`~/.local/share/pharos/cli` on Linux), or under `PHAROS_CLI_CACHE`. It is rebuilt when the tool
or the install changes; delete it to start afresh. Where the system does not allow symbolic links,
the files are copied, the game's `assets` folder included, which costs nearly a gigabyte per
folder. The tool is tested on Linux; Windows and macOS installs lay their files out differently
and are not covered by its CI yet.

## In a test project

```csharp
using Zaldaryon.Pharos.XUnit;

[ServerMods("../../../../MyMod/bin/Release/mymod.zip")]
[StrictBoot]
public class Smoke : ModSmokeTest
{
    protected override IReadOnlyList<string> SmokeCommands => ["/time set day"];
}
```

`ModSmokeTest` is a `ClientServerScenarioBase` with one test, `ModBootsJoinsAndPlays`. Its options:
- `SmokeFrames` (600) and `SmokeCommands` set how long it plays and what it runs.
- `AllowedLoggedErrors` lets known errors pass.
- `[AllowBootDiagnostic]` allows known boot warnings.
- `[PharosMods]` adds client-only mods.

It fails before booting when no mod is named, since a smoke test of vanilla alone would pass for
every mod. Each run boots fresh hosts (`WorldIsolation.Restart`).

## In CI

A job that smoke-tests a mod on every push, on GitHub Actions:

```yaml
smoke:
  runs-on: ubuntu-latest
  env:
    VS_VERSION: "1.22.7"
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: "10.0.x"
    - name: Install Vintage Story, Xvfb and Mesa
      run: |
        sudo apt-get update && sudo apt-get install -y xvfb libgl1-mesa-dri libopenal1 libglfw3
        mkdir -p "$HOME/vintagestory"
        wget -q "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_${VS_VERSION}.tar.gz" -O vs.tar.gz
        tar xzf vs.tar.gz -C "$HOME/vintagestory" --strip-components=1
    - name: Build the mod
      run: dotnet build -c Release
    - name: Smoke test
      env:
        LIBGL_ALWAYS_SOFTWARE: 1
        GALLIUM_DRIVER: llvmpipe
        MESA_GL_VERSION_OVERRIDE: "4.5"
        MESA_GLSL_VERSION_OVERRIDE: "450"
        ALSOFT_DRIVERS: null
      run: |
        export VINTAGE_STORY="$HOME/vintagestory"
        dotnet tool install -g Zaldaryon.Pharos.Cli
        xvfb-run -a pharos smoke --mod bin/Release/mymod.zip --strict --artifacts artifacts/pharos
    - name: Upload what failed
      if: failure() || cancelled()
      uses: actions/upload-artifact@v4
      with:
        name: smoke-test
        path: artifacts/pharos
        if-no-files-found: ignore
```

Cache the game download with `actions/cache` as this repository's own workflow does.
