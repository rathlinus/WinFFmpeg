# Developing FFConvert

## Setup

Run `scripts\setup-dev.ps1` once from an elevated PowerShell. It does three things and skips each one that is already in place:

- installs the .NET 10 SDK with winget
- turns on Windows Developer Mode, which allows registering an unsigned package from a folder
- adds the MSVC x64 toolset to Visual Studio Build Tools 2022, or installs the Build Tools when they are missing. The Native AOT compiler needs its linker.

The conversion tests also need ffmpeg on `PATH`.

## Scripts

| Script | What it does |
|---|---|
| `scripts\build.ps1` | Publishes the shell extension, then builds the app as `artifacts\FFConvert.msix`. The package is unsigned. |
| `scripts\build.ps1 -Register` | Also unpacks the package to `artifacts\layout` and registers it from there. This is the quick loop while developing. |
| `scripts\sign.ps1` | Signs the package with the `CN=FFConvert` certificate from your user store and creates that certificate when there is none. |
| `scripts\install.ps1 -Build` | Builds, signs and installs. |
| `scripts\release.ps1 -Version 1.2.0` | Builds and signs a release and collects its files in `artifacts\release`. |
| `scripts\generate-assets.ps1` | Draws the app icon in every size the manifest needs. Run it only to change the icon. |
| `packaging\Install.ps1` | The installer that ships in a release. `install.ps1` calls the same script. |

The library and the tests build on their own with `dotnet build` and `dotnet test`. The app project needs the published shell extension and stops with an error without it, so build the app with `build.ps1`.

## Projects

| Project | What it is |
|---|---|
| `src/FFConvert.Core` | Presets, the ffmpeg command line, the job queue, settings. No UI, and compatible with Native AOT because the shell extension links it. |
| `src/FFConvert.Shell` | The context menu handler: a COM `IExplorerCommand` compiled to a native DLL. |
| `src/FFConvert.App` | The WinUI 3 app and the MSIX manifest. |
| `tests/FFConvert.Core.Tests` | Unit tests, and conversions run against ffmpeg. |

## From a click to an ffmpeg process

1. `Package.appxmanifest` registers a COM class as a context menu verb for all files. When Explorer builds a menu, Windows starts a COM surrogate (`dllhost.exe`) that loads `FFConvert.Shell.dll`.
2. The DLL exposes one root command and one sub-command per menu entry. Explorer asks each for its state and passes the selection. `ContextMenuModel` in Core decides which entries apply, and the rest report themselves as hidden. Saved presets come from the same `settings.json` the app writes.
3. A click calls `Invoke` on the sub-command, which starts `FFConvert.exe --run <id> <files>`. A selection too long for a command line is written to a list file and passed with `--files-from`.
4. `FFConvert.exe` is a single instance. The first process owns a mutex and listens on a named pipe. A later process sends its request over the pipe and exits, so every click lands in one queue.
5. `JobQueue` converts one file at a time. It runs `ffprobe` for the duration, builds the arguments with `ArgumentBuilder` and starts ffmpeg with `-progress pipe:1`, which reports progress as `key=value` lines on standard output.
6. ffmpeg writes to `name.partial.ext`. The file gets its final name only after ffmpeg exits with success. On failure or cancellation it is deleted.

Cancelling writes `q` to ffmpeg's standard input, which lets it stop cleanly, and kills the process if it is still running three seconds later. Every ffmpeg process is also placed in a Windows job object that ends it when the app exits for any reason.

## Notes on the shell extension

The verb is registered for `*`, not for a list of extensions. Explorer did not show a verb registered per extension, so the handler filters in `GetState` instead.

The handler is published with Native AOT. Windows loads it on every right-click, and a DLL that had to start the .NET runtime first would make the menu slow to open. `build.ps1` adds the Visual Studio Installer folder to `PATH` for the publish, because the Native AOT targets fail when `vswhere.exe` cannot be found there.

`ComInterfaces.cs` declares the shell interfaces by hand. Their methods have to stay in the order of the Windows SDK headers, since COM calls them by position.

While a package is registered, the surrogate keeps `FFConvert.Shell.dll` open. `build.ps1 -Register` removes the registration before it replaces the files.

The app and the handler both read settings from the package's `LocalState` folder, named explicitly in `AppPaths`, so the two processes always agree on the file.

## Versions

Windows refuses to install a package when the same version is already installed with different content. `build.ps1` therefore numbers every build. Without `-Version` the number is `0.<days since 2026-01-01>.<minute of the day>.0`, which rises with each build and stays below every release. A release passes its own version.

## Tests

```powershell
dotnet test tests\FFConvert.Core.Tests
```

`JobQueueTests` generates short clips with ffmpeg's test sources and converts them, covering each output format, cancellation, failures and file naming. Those tests return without checking anything when ffmpeg is not installed, so a green run on a machine without ffmpeg says less than it seems to.

`ManifestTests` checks things the compiler cannot: that the manifest and the shell extension use the same class id, and that the verb is registered for all files.
