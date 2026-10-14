# OpenParallel for Windows and Linux

Run a second copy of an app with its own login and data. The original app is never changed.
Same engine ideas and wording as the Mac app; built with .NET 10, Avalonia 12 and FluentAvalonia
(one Fluent interface for both systems). Built on a Mac; Windows is tested in a Wine bottle,
Linux in a Parallels VM. Plans and progress logs: `PLAN-WINDOWS.md`, `PLAN-LINUX.md`.

## Layout

| Path | What |
|---|---|
| `src/OpenParallel.Core` | Engine: create/launch/recognise/remove instances (shared) |
| `src/OpenParallel.Core/Windows` | Start menu apps, exe inspection, recipes, `.lnk`, Recycle Bin, window messages |
| `src/OpenParallel.Core/Linux` | Desktop entries, app inspection (packages, AppImage, Flatpak, Snap), `/proc`, signals, freedesktop Trash, X11 |
| `src/OpenParallel.App` | `OpenParallel.exe`, the app (also `OpenParallel.exe --launch <slug>` for shortcuts) |
| `src/OpenParallel.Cli` | `opctl.exe`, JSON command line used by the tests |
| `tests/OpenParallel.Core.Tests` | Unit tests (run on macOS) |
| `tests/fixtures/FakeApp`, `FakeLinuxApp` | Stand-in apps that play Chrome, Electron, VS Code, Firefox, Telegram, PyCharm, Zoom… |
| `packaging/linux` | `install.sh` / `uninstall.sh` shipped inside the Linux download |
| `scripts/` | Windows: `build.sh [--zip]`, `wine-test.sh [--setup-ui]`, `link-bottle.sh`, `winui.swift`. Linux: `build-linux.sh [--package]`, `linux-test.sh`, `vm.sh` |

## Build and test on a Mac

```bash
. scripts/env.sh                                   # .NET in ~/.dotnet, the Sikarugir test bottle
dotnet test --project tests/OpenParallel.Core.Tests # 48 unit tests (Windows + Linux)
./scripts/build.sh                                 # artifacts/win-x64 (add --zip for the download)
./scripts/wine-test.sh                             # 39 end-to-end checks inside the bottle
./scripts/wine-test.sh --setup-ui                  # install build + stand-in apps for UI testing
./scripts/link-bottle.sh                           # double-clicking the bottle opens OpenParallel
./scripts/build-linux.sh --package                 # artifacts/linux-{arm64,x64} + .tar.gz downloads
./scripts/vm.sh test                               # Linux end-to-end checks inside the Parallels VM
```

## Installing on Linux

Unpack `OpenParallel-<version>-linux-<arch>.tar.gz` and run `OpenParallel/install.sh` (no sudo):
the app goes to `~/.local/opt/OpenParallel`, the `openparallel` command to `~/.local/bin`, and it
appears in the applications menu. `uninstall.sh` removes it again and leaves your instances.

On Linux an instance is a menu entry `~/.local/share/applications/openparallel-<slug>.desktop`
plus `~/.local/share/OpenParallel/Instances/<slug>`. The same app recipes apply (Chromium and
Electron also get `--class` so the dock shows each instance on its own), and any other app gets
its own `XDG_CONFIG_HOME`/`DATA`/`CACHE`/`STATE` (Experimental). Flatpaks run with access to just
the instance folder; Snaps keep the instance's data inside the snap. Removing moves everything to
the freedesktop Trash, so Files can restore it.

The bottle is `~/Applications/Sikarugir/OpenParallel-Test.app` (made with Sikarugir Creator);
`OP_BOTTLE` points elsewhere if needed.

## What an instance is on Windows

A Start menu shortcut in `Start Menu\Programs\OpenParallel` (optionally on the desktop too) plus a
data folder in `%LOCALAPPDATA%\OpenParallel\Instances\<name>` holding `instance.json` and the
app's own profile. Chromium and Electron apps get `--user-data-dir`, VS Code also
`--extensions-dir`, Firefox `-profile -no-remote`, Telegram `-many -workdir`, JetBrains IDEs a
`*_PROPERTIES` file (their shortcut runs `OpenParallel.exe --launch`), Squirrel apps start through
`Update.exe` so they always use the newest version, and websites open as a browser app window
with their own profile. Store apps and single-instance apps (Zoom, Steam, WhatsApp, Teams…) are
refused honestly, with a website instance offered when one exists.

Removing an instance moves its shortcuts and data folder to the Recycle Bin, and only while it
is closed. OpenParallel sends nothing anywhere and runs only while its window or tray icon is open.
