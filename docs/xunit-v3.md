# xUnit v3 Migration Guide

Pharos ships two xUnit packages: `Zaldaryon.Pharos.XUnit` (v2) and `Zaldaryon.Pharos.XUnit.V3` (v3). This guide explains how to migrate from v2 to v3 and what changes.

## Why v3?

xUnit v3 brings several improvements:

- **Microsoft Testing Platform (MTP)**: Native support for the new test platform with better parallel execution, quarantine, and sharding
- **`TestContext`**: Access to test metadata, cancellation tokens, attachments, and warnings
- **`TheoryDataRow` with labels and skip**: Per-row display names and conditional skips for matrix theories
- **`ValueTask` lifecycle**: Modern async pattern in `IAsyncLifetime`
- **Deprecation**: xUnit v2 will age out of IDE support and the Verify package it uses is deprecated

## Migration

### 1. Update the package reference

```xml
<!-- Before -->
<PackageReference Include="Zaldaryon.Pharos.XUnit" Version="0.8.0" />

<!-- After -->
<PackageReference Include="Zaldaryon.Pharos.XUnit.V3" Version="0.8.0" />
```

### 2. Update namespace imports

```csharp
// Before
using Zaldaryon.Pharos.XUnit;

// After
using Zaldaryon.Pharos.XUnit.V3;
```

### 3. Base classes unchanged

The scenario base classes (`ClientScenarioBase`, `ServerScenarioBase`, `ClientServerScenarioBase`) work the same way. Your test code does not need changes.

### 4. Attributes migrated

All scenario attributes work identically:

| Attribute | Notes |
|-----------|-------|
| `[ClientScenario]`, `[ServerScenario]`, `[ClientServerScenario]` | Same behavior |
| `[ClientTheory]`, `[ServerTheory]`, `[ClientServerTheory]` | Same behavior |
| `[WindowSizes]`, `[ClientSettingsMatrix]` | Now with per-row labels and skip |
| `[WorldConfigMatrix]`, `[ServerConfigMatrix]`, `[ModConfigMatrix]` | Same behavior |
| `[CommandPrivilegeMatrix]` | Same behavior |
| `[GameVersion]` | Same behavior |
| `[Live]`, `[Soak]` | Same behavior |

## New Features

### Cancellation Token Flow

In v3, `TestContext.Current.CancellationToken` flows into all Pharos waits. You can pass it explicitly:

```text
[ClientServerScenario(TimeoutMs = 60_000)]
public async Task Forge_heats()
{
    CancellationToken ct = TestContext.Current.CancellationToken;
    await Session.StepUntilAsync(() => Hot(pos), maxFrames: 600, ct);
}
```

The Pharos watchdog remains the authority; xUnit's timeout stays off. The test's token adds cooperative cancellation.

### Per-Row Skip in Theories

Matrix attributes now support per-row skip with labels:

```text
[ClientTheory]
[WindowSizes("1280x720", "1920x1080", "3840x2160", 
    GuiScales = new[] { 1f, 1.5f, 3f },
    SkipSizes = new[] { "1280x720@3:GUI scale 3 requires at least 1920x1080" })]
public async Task Renders_at_all_sizes(WindowLayout layout)
{
    // GUI scale 3 at 1280x720 is automatically skipped
}
```

Skipped rows appear in the test report with their reason.

### Failure Artifacts as Attachments

Screenshots, logs, and `run.json` are attached to the test result via `TestContext.AddAttachment`:

- Attachments are capped at 10 MB each
- Media types are inferred from file extension
- Files exceeding the limit generate a warning instead

### Isolation Warnings

When Pharos boots a fresh host because rollback failed or another class took the pooled host, a warning is added to the test output:

```
Warning: isolation: restarted (rollback failed: entity count changed)
```

## MTP Support

### Running with MTP

Use `pharos run` with the MTP runner:

```bash
# Run with MTP runner
pharos run tests/MyMod.Tests --runner mtp --parallel 4

# With repeat
pharos run tests/MyMod.Tests --runner mtp --repeat 3

# With quarantine (auto-retry and isolate flaky tests)
pharos run tests/MyMod.Tests --runner mtp --quarantine pharos.quarantine.json

# With sharding
pharos run tests/MyMod.Tests --runner mtp --shard 1/4

# With JUnit and CTRF reports
pharos run tests/MyMod.Tests --runner mtp --report-junit junit.xml --report-ctrf ctrf.json
```

### Quarantine File

Create `pharos.quarantine.json` to manage flaky tests:

```json
{
  "tests": [
    {
      "name": "MyMod.Tests.FlakyTest.Sometimes_fails",
      "expires": "2026-12-31",
      "reason": "Tracking issue #123"
    }
  ]
}
```

Quarantined tests:
- Are retried automatically on failure
- Do not fail the build if they fail after retry
- Generate warnings in the report
- Expire on the given date

## What Does Not Migrate

### AtlasCompat

`AtlasCompat` is v2-only and has no v3 equivalent. Atlas itself is v2-only infrastructure that xUnit v3 does not use. If you have `AtlasCompat` references, remove them when migrating.

### Custom Test Case Ordering

If you implemented custom `ITestCaseOrderer`, the v3 interface differs. See the [xUnit v3 documentation](https://xunit.net/docs/getting-started/v3/migration) for details.

## Running Both Versions

During migration, you can run both v2 and v3 suites in CI:

```yaml
jobs:
  test-v2:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test tests/MyMod.Tests.V2

  test-v3:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: pharos run tests/MyMod.Tests.V3 --runner mtp
```

Once all tests pass on v3, remove the v2 project.

## Troubleshooting

### "Could not load type IAsyncLifetime"

Ensure you're using the v3 package:
```xml
<PackageReference Include="Zaldaryon.Pharos.XUnit.V3" Version="0.8.0" />
```

### "TestContext.Current is null"

`TestContext.Current` is only available inside a running test. Check that:
1. You're in a test method, not a class constructor
2. You're using the v3 runner, not the v2 runner

### Attachments not appearing

Check:
1. The file exists and is under 10 MB
2. The test actually failed (attachments are only added on failure)
3. Your test runner supports attachments (MTP does, older runners may not)

## Version History

| Pharos | xUnit v3 | Notes |
|--------|----------|-------|
| 0.8.0 | Preview | Initial v3 support |
| 0.8.x | Stable | Full parity with v2, MTP support |
| 1.0.0 | Default | v3 becomes the default, v2 deprecated |
