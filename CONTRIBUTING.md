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

The CI workflow runs on every push and pull request:

1. **Build**: Compiles the solution in Release configuration.
2. **Test (Linux)**: Runs tests with Mesa software rendering inside Xvfb.
3. **Test (Windows)**: Runs tests on Windows with native graphics.

Both test jobs require a cached Vintage Story installation. The cache is keyed by version.

## Questions

Open an issue if you have questions about contributing.
