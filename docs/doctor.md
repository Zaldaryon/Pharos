# Doctor and Init

`pharos doctor` checks the machine before a suite does, so a missing game, runtime, display or
native library is one line with a fix hint instead of a stack trace from inside the game.
`pharos init` writes the first test project for a mod.

## Doctor

```bash
pharos doctor
pharos doctor --json
pharos doctor --project tests/MyMod.Tests --fix
```

Each check prints one line:

```text
ok   Vintage Story: 1.22.7 at /home/me/.local/share/vintagestory (vanilla)
ok   .NET: .NET 10.0.13, which matches the game 1.22.7
ok   OpenGL: display :1; Mesa supplies the GL context
ok   Natives: OpenAL, Cairo, GLFW, SkiaSharp, Harmony and protobuf-net
ok   Project: MyMod.Tests.csproj
ok   Ports: four loopback ports could be bound
```

The checks are:

- **Vintage Story**: the install named by `--game`, `VINTAGE_STORY` or a default folder, the version
  the game reports, whether the folder is vanilla or carries Optimum, and the two game DLLs.
- **.NET**: the runtime Pharos runs on, against the major the game needs (10 for 1.22.x, 8 for
  1.21.x).
- **OpenGL**: a display on Linux, or `xvfb-run` when there is none. A client scenario is still the
  only check that creates a context.
- **Natives**: the OpenAL, Cairo, GLFW, SkiaSharp, Harmony and protobuf-net files in the game's
  `Lib` folder.
- **Project**: the test project's Pharos package and its `VintagestoryAPI` reference, when there is
  a project in the folder.
- **Ports**: four loopback ports can be bound for a server and its clients.

`--json` writes the same report as one object with `allGood`, `exitCode` and a `checks` array. A
check has `name`, `status` (`ok`, `warn` or `fail`), `message` and sometimes `hint`.

`--fix` writes only files that are safe to write:

- `pharos-headless.sh` when there is no display: it sets `LIBGL_ALWAYS_SOFTWARE=1` and runs the
  command under `xvfb-run`;
- `pharos.runsettings` when there is a project: it sets `VINTAGE_STORY` and `LIBGL_ALWAYS_SOFTWARE`
  for a runner that reads run settings.

The exit code is 0 when no check failed, 1 when a check failed and 2 when doctor could not run.
Warnings do not fail the command.

## Init

```bash
pharos init
pharos init --dir tests/MyMod.Tests --mod-project src/MyMod/MyMod.csproj --name MyMod.Tests
```

`init` writes:

- `MyMod.Tests.csproj`, which references `Zaldaryon.Pharos.XUnit` and the mod project, and takes
  `VintageStoryPath` from `VINTAGE_STORY`;
- `SmokeTests.cs`, one server scenario that boots the server;
- `xunit.runner.json`, which keeps test collections serial because the game keeps its state in
  statics.

It then runs the doctor on the new project. It does not build the project; the next command is a
`dotnet test` on the new file.

`init` refuses a nonempty folder unless `--force` is passed. An existing file in a forced folder is
overwritten only when it has the names above.

## Limits

- The graphics check reads the display and `xvfb-run`; it does not create a GL context. A client
  scenario reports the context it could not create.
- `--fix` does not install packages, start a display server or write the game. It writes the two
  files named above.
- `init` writes a minimal project. The `dotnet new` templates, the reusable workflow and the xUnit
  v3 variant are the subject of #296.
