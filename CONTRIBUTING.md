# Contributing to Pharos

This document describes how to contribute to the Pharos test harness for Vintage Story mods.

## Getting Started

1. Fork the repository and clone your fork.
2. Install the .NET 8.0 SDK.
3. Set the `VINTAGE_STORY` environment variable to your Vintage Story installation directory.
4. Run `dotnet build Pharos.sln` to verify your setup.

## Development Workflow

### Branches

All contributions target `main`. Create a feature branch from `main` with a descriptive name:

```
feat/short-description
fix/issue-number-description
```

### Pull Request Process

1. Create a feature branch from `main`.
2. Make your changes with clear, focused commits.
3. Run the test suite locally: `dotnet test Pharos.sln -c Release`
4. Push your branch and open a pull request targeting `main`.
5. Address any review feedback.
6. Once approved and CI passes, your PR will be merged.

## Coding Standards

### C# Style

Follow the existing code style in the repository:

- Use file-scoped namespaces.
- Prefer `var` when the type is obvious from the right side.
- Use `readonly` for fields that do not change after construction.
- Prefer expression-bodied members for single-line implementations.
- Use `sealed` on classes unless inheritance is explicitly needed.
- Prefix private fields with underscore (`_fieldName`).
- Prefix static fields with `s_` (`s_staticField`).
- Use `nameof()` instead of string literals for member names.

### Documentation

- Add XML documentation comments to public APIs.
- Keep comments concise and focused on why, not what.

## Test Requirements

All code changes require tests:

- New features must include tests demonstrating the feature.
- Bug fixes must include a test that would have caught the bug.
- Tests use xUnit with FluentAssertions.
- Test class names follow `{ClassName}Tests`.
- Test method names follow `{Method}_{Scenario}_{ExpectedResult}`.

### Running Tests

Run the full test suite:

```bash
dotnet test Pharos.sln -c Release
```

On Linux without a display, use the headless script:

```bash
./scripts/run-headless-linux.sh
```

This script configures Mesa software rendering with llvmpipe and runs tests inside Xvfb.

## CI Pipeline

CI runs in two lanes.

- **Quick lane:** every pull request runs it. It builds the solution and runs, on Linux, every test that does not boot a real client or server or measure throughput (`Category!=Live&Category!=Benchmark`). It takes a few minutes.
- **Full lane:** it runs the whole suite on Linux with Mesa under Xvfb, on Windows, and against a dedicated server in Docker. It runs:
  - on every push to `main`;
  - every night;
  - by hand, from the Actions tab;
  - for a pull request labeled `full-ci`;
  - before every release.

Pull requests that only change documentation skip the build. A new push to a pull request cancels the run in progress.

Keep `full-ci` for pull requests that need the full platform checks before merging. Ordinary pull requests use the quick lane; pushes to `main` still run the full lane. The full suite runs in three shards on each platform. Their filters live in `scripts/ci-shards.json`; the last shard includes everything outside the first two, so a new namespace remains covered. Package and mod smoke checks run once on Linux.

Tests marked with a scenario attribute are `Category=Live` on their own. A test class that boots a client or a server without one says so with `[Trait(PharosTraits.Category, PharosTraits.Live)]`. Run the quick lane locally with:

```bash
dotnet test Pharos.sln -c Release --filter "Category!=Live&Category!=Benchmark"
```

On Windows, build once against your Vintage Story installation and reuse that build for local tests:

```powershell
$env:VINTAGE_STORY = 'C:\Games\VintageStory'
$env:VSLANG = '1033'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
dotnet build Pharos.sln -c Release
dotnet test Pharos.sln -c Release --no-build --filter 'Category!=Live&Category!=Benchmark'

# Full suite, or one of the same shards used by CI:
dotnet test Pharos.sln -c Release --no-build
$shards = Get-Content scripts/ci-shards.json -Raw | ConvertFrom-Json
dotnet test Pharos.sln -c Release --no-build --filter $shards.include[0].filter
```

Local Windows tests provide feedback before pushing. The full CI also checks Linux native dependencies, Mesa rendering and the dedicated server.

Live tests need OpenGL 4.3. To match Windows CI, stage the Mesa `opengl32.dll`, `libglapi.dll` and `libgallium_wgl.dll` next to the test host in `tests/Zaldaryon.Pharos.Tests/bin/Release/net10.0`, and set `GALLIUM_DRIVER=llvmpipe`. The workflow uses `f3d-app/install-mesa-windows-action` for this setup.

The language settings match CI. The parallel runner currently identifies aborted VSTest workers from English diagnostics, so localized output can make its crash and hang tests fail.

The test jobs need a cached Vintage Story installation. The cache is keyed by version.

## Releasing

1. Merge a pull request that bumps `<Version>` in every package's `.csproj` and `Version` in `src/Zaldaryon.Pharos.Bridge/ModInfo.cs`.
2. Add the release notes as `docs/releases/v<version>.md`. Without them, GitHub generates notes from the merged pull requests.
3. Run the **Release** workflow from the Actions tab on `main`, with the version.

The workflow checks that the packages carry the version and that the tag is new, then runs the full CI. Only when all of it passes does it publish the packages to NuGet and create the GitHub release, with its tag on the commit it tested.

## Questions

Open an issue if you have questions about contributing.
