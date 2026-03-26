# Easy Eyes

A Windows desktop app that reminds you to rest your eyes. After 20 minutes of screen use, a gradient overlay appears to prompt a break. If you lock your screen briefly (under 20 seconds), a toast notification reminds you to rest when you return.

The app lives in the system tray with pause, resume, and snooze controls. Core logic is driven by a state machine (see [PLAN.md](PLAN.md) for details).

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
