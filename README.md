# Easy Eyes

A Windows desktop app that reminds you to rest your eyes. After 20 minutes of screen use, a gradient overlay appears to prompt a break. If you lock your screen briefly (under 20 seconds), a toast notification reminds you to rest when you return.

The app lives in the system tray with pause, resume, and snooze controls. A "Pause media on lock" toggle (enabled by default) automatically pauses any playing media when the screen is locked or the display turns off. Core logic is driven by a state machine (see [PLAN.md](PLAN.md) for details).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) (see `global.json` for the exact version)
- Windows (WPF)

## Build

```
dotnet build
```

## Run Tests

```
dotnet test
```

## Publish

```
dotnet publish EasyEyes/EasyEyes.csproj --configuration Release --output ./publish
```

This produces a single-file, self-contained executable in the `publish/` directory.

## Release

Releases are automated via GitHub Actions. To publish a new version:

1. Update `CHANGELOG.md`: move items from `[Unreleased]` into a new `[x.y.z] - YYYY-MM-DD` section.
2. Commit and push.
3. Tag and push: `git tag vx.y.z && git push --tags`.

The workflow builds, tests, and creates a GitHub Release with `EasyEyes.exe` attached and release notes extracted from the changelog.
