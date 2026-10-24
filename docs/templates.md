# Templates and the Reusable Workflow

`Zaldaryon.Pharos.Templates` holds two `dotnet new` templates: `pharos-mod-tests`, a test project for a mod, and `pharos-ci`, a GitHub Actions workflow that runs it. The workflow calls `mod-ci.yml`, a reusable workflow in this repository.

The templates and `mod-ci.yml` ship from the first release after 0.7.0. During development, the workflow template calls `@main`; that reference works once this change has merged. Pin a commit instead when testing an unmerged branch. The 0.7.0 packages predate the client assembly-loading fix used by these tests, so use packages built from the same commit and override `PharosVersion` until the next release. Before publishing a release, update the package defaults and workflow references to that release's version and tag; the release workflow checks both.

## A test project

```bash
dotnet new install Zaldaryon.Pharos.Templates
dotnet new pharos-mod-tests -o MyMod.Tests --mod-project ../MyMod/MyMod.csproj --client
dotnet test MyMod.Tests
```

| Option | Default | |
|---|---|---|
| `--mod-project` | (required) | The mod's `.csproj`, relative to the new project's folder. |
| `--framework` | `xunit2` | `xunit2` references `Zaldaryon.Pharos.XUnit`; `xunit3` references `Zaldaryon.Pharos.XUnit.V3`, which is a preview. |
| `--client` | `false` | Add a test in which an engine-mode client joins the server. |

The project holds:

- `Smoke.cs`, a `ModSmokeTest` that boots the server with the mod and fails on its errors (see
  [Smoke Testing a Mod](smoke-test.md));
- `ServerTests.cs`, a `ServerScenarioBase` test that checks the server loaded and enabled the mod;
- `ClientServerTests.cs`, with `--client`, a `ClientServerScenarioBase` test that checks a client
  joined and loaded the mod too;
- `AssemblyInfo.cs`, with `[assembly: ServerMods(ModUnderTest.Path)]`;
- `xunit.runner.json`, which keeps the tests serial, since the game keeps its state in statics;
- `pharos.runsettings`, which sets software rendering and the null audio device for runners that
  read run settings.

`VINTAGE_STORY` names the install the project builds against and runs; `-p:VintageStoryPath=...` overrides it. The build stops with a message when there is no install there. The project references the install's `VintagestoryAPI`, `VintagestoryLib`, `0Harmony` and `protobuf-net`, as the tests call the game directly.

### The mod under test

The project builds the mod project first and generates `ModUnderTest.Path`, the absolute path of the mod's output folder, under `obj/`. The helper `ModUnderTest.Id` reads the top-level `modid` from that folder's `modinfo.json`, ignoring key case. It accepts JSON comments and trailing commas; for other nonstandard JSON syntax, set `PharosModId` explicitly. If duplicate keys differ only by case, the last nonempty string wins. The game loads the mod from that folder, as it would from its `Mods` folder; the mod's assembly is not copied next to the tests.

| Property | |
|---|---|
| `PharosModProject` | The mod project. The template sets it from `--mod-project`. |
| `PharosModPath` | A mod folder or zip that is already built. When set, the mod project is not built. |
| `PharosModId` | The mod's ID, for a mod whose `modinfo.json` is not in its output. |
| `PharosVersion` | The version of the Pharos packages. A `PharosVersion` environment variable also sets it. |

The tests fail with a setup message when they find no top-level `modid`. A mod that declares its ID only in a `[assembly: ModInfo]` attribute, or a prebuilt mod zip, needs `PharosModId`.

## A workflow

```bash
dotnet new pharos-ci --test-project MyMod.Tests/MyMod.Tests.csproj
```

This writes `.github/workflows/pharos.yml`, which runs the tests on every push to `main`, on pull requests and by hand.

| Option | Default | |
|---|---|---|
| `--test-project` | (required) | The test project, relative to the repository root. |
| `--game-version` | The version Pharos is tested against | The Vintage Story version to install. |
| `--windows` | `true` | Run on Windows as well as Linux. |

## The reusable workflow

```yaml
jobs:
  pharos:
    uses: Zaldaryon/Pharos/.github/workflows/mod-ci.yml@main   # or a release tag, or a commit
    with:
      test-project: MyMod.Tests/MyMod.Tests.csproj
      game-version: "1.22.7"
      os: '["ubuntu-latest", "windows-latest"]'
```

Each runner in `os` gets a job. It installs .NET and the game with the [`setup-vintage-story`](ci-and-cloud.md#using-pharos-in-your-mods-ci) action of the same commit as the workflow, builds the test project, installs the `pharos` tool, optionally smoke-tests the mod, and runs the tests with `pharos run --parallel` (see [Parallel Runs](parallel-runs.md)). The run's Markdown report goes to the job summary.

| Input | Default | |
|---|---|---|
| `test-project` | (required) | The test project, relative to the repository root. |
| `game-version` | `1.22.7` | The Vintage Story version to install. |
| `os` | `'["ubuntu-latest"]'` | A JSON list of runners. |
| `parallel` | `2` | How many test workers run at once. |
| `filter` | (none) | A `dotnet test` filter, such as `Category=Live`. |
| `smoke` | `false` | Run `pharos smoke` on the mod before the tests. |
| `smoke-mod` | The test project's `PharosModPath` | The mod folder or zip to smoke-test. |
| `upload-artifacts` | `true` | Upload the results. |
| `artifact-name` | `pharos` | The start of the artifact's name; the runner and the attempt are added. |
| `timeout-scale` | `1` on Linux, `2` on Windows | `PHAROS_TIMEOUT_SCALE` for the tests. |
| `dotnet-version` | `10.0.x` | The .NET SDK to install. |
| `pharos-version` | The `Zaldaryon.Pharos` the project restores | The version of the `pharos` tool. |
| `packages` | (none) | An artifact of the calling run with Pharos packages of version `pharos-version`, which the project and the tool use instead of NuGet's. |

The artifact holds the merged TRX, `pharos-run.json`, the report, the smoke test's output and the failure artifacts of each failing test (see [Failure Artifacts](failure-artifacts.md)). The job context the workflow reads to find its own commit is not available on GitHub Enterprise Server.

## Limits

- The templates write a project for one mod. A test that needs other mods adds them with
  `[ServerMods]` or `[PharosMods]`.
- `pharos-ci` writes a workflow for GitHub Actions only.
- The game install is downloaded from the official CDN; private or modified installs need their
  own setup step.
