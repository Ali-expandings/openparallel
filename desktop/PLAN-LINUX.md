# OpenParallel for Linux: plan

Status: started 2026-10-14, right after the Windows version. Same product, same engine design,
same Avalonia interface; this file covers what is different on Linux. The Windows plan is
`PLAN-WINDOWS.md`. The `.NET` code now lives in `desktop/` (was `windows/`) because one solution
builds both.

## 1. What it is

Make a second (third, …) copy of an installed app with its own login and data, open it from the
applications menu like any app, and say honestly how proven that separation is. The original app
is never modified. Same principles as Mac and Windows: no patched binaries (copies are launch
recipes), honest status, everything recoverable (Trash), no telemetry or background service,
keyboard first.

## 2. Technology

| Option | Look on Linux | Reuse | Verdict |
|---|---|---|---|
| **Same Avalonia 12 + FluentAvalonia UI** | Clean Fluent look, consistent with Windows; not GNOME/KDE-native | Whole UI and engine | **Chosen**: one interface to keep polished |
| GTK 4 + libadwaita (GirCore C# bindings) | Native GNOME | Engine only, second UI | Later, if GNOME users ask |
| Qt / KDE Kirigami | Native KDE | Engine only | Rejected: C++/QML, third UI |
| Electron / Tauri | Web | None | Rejected (same reasons as Mac) |

Platform wording changes, not the design: Trash instead of Recycle Bin, applications menu instead
of Start menu, Files instead of File Explorer. The window uses the system font (Ubuntu, Cantarell,
Noto). Published self-contained for `linux-x64` and `linux-arm64`, so nothing needs installing.

## 3. How a second copy works on Linux

### 3.1 Recipes

| Family | Detected by (desktop entry `Exec`, resolved binary) | Recipe | Status |
|---|---|---|---|
| Chromium browsers (Chrome, Chromium, Edge, Brave, Vivaldi, Opera) | binary names, or `chrome_crashpad_handler` beside it | `--user-data-dir=<data>/chromium --class=<id>` | Supported |
| Electron apps (Slack, Discord, Obsidian, Signal, Claude, …) | `resources/app.asar` or `resources/app/package.json` | `--user-data-dir=<data>/electron --class=<id>` | Supported |
| VS Code family (Code, VSCodium, Cursor, Windsurf) | `resources/app/product.json` | `--user-data-dir … --extensions-dir …` | Supported |
| Firefox family (Firefox, Firefox ESR, LibreWolf, Waterfox, Zen, Floorp) | `application.ini` + `omni.ja` | `-profile <data>/firefox -no-remote --class=<id>` | Supported |
| Telegram Desktop | `telegram-desktop` / `Telegram` | `-many -workdir <data>/telegram` | Supported |
| JetBrains IDEs | `bin/idea.properties`, `pycharm.sh`… | `<PRODUCT>_PROPERTIES` env in the shortcut (`env K=V …`) | Experimental |
| **Any other app** | — | Own `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME` under `<data>/xdg` | **Experimental** (Linux apps mostly follow these) |
| D-Bus single-instance apps (`DBusActivatable=true`) | desktop entry key | — | Shares data: a second start opens a window of the running copy |
| Single-instance apps (Steam, Zoom, Spotify) | known list | — | Not supported (website instance offered where one exists) |

How an app is installed changes only the wrapper:

| Install | Recipe wrapper | Notes |
|---|---|---|
| Distribution package / tarball | run the binary | |
| AppImage | run the `.AppImage` | updates replace the file in place, the shortcut keeps working |
| Flatpak | `flatpak run --filesystem=<data> [--env=K=V] <app-id> <args>` | the sandbox is given just the instance folder |
| Snap | `/snap/bin/<name> <args>`, data inside `~/snap/<name>/common/OpenParallel/<slug>` | strict confinement can't see `~/.local`; Experimental |

### 3.2 Website instances

The user's Chromium browser as an app window: `<browser> --app=<url> --user-data-dir=<data>/web
--class=<id>`. Chrome, Chromium, Edge, Brave and Vivaldi count, from a package, Flatpak or Snap.

### 3.3 Where things live (XDG)

```text
Instances:  ~/.local/share/OpenParallel/Instances/<slug>/instance.json  (+ the app's data)
Shortcuts:  ~/.local/share/applications/openparallel-<slug>.desktop   (optional copy on the desktop)
Logs:       ~/.local/share/OpenParallel/Logs/<slug>.log
Settings:   ~/.config/OpenParallel/settings.json
```

The shortcut is a desktop entry with `Name`, `Exec` (quoted per the Desktop Entry spec, `%`
doubled), the source app's `Icon`, `StartupWMClass` matching `--class` so the dock shows each
instance as its own app, and `X-OpenParallel-Instance=<slug>`. Environment goes into
`Exec=env K=V …`, so no launcher process is needed on Linux.

### 3.4 Running, Show, Quit, Trash

- **Running:** `/proc/<pid>/cmdline` and `/proc/<pid>/environ`. An instance runs when a process's
  arguments contain its exact profile arguments (`--user-data-dir=<path>`, `-profile <path>`…)
  or its environment holds its exact `KEY=value`. Helpers (`--type=`, `-contentproc`) are skipped.
  A file manager opened on the data folder never counts (it has the path, not the switch).
- **Show:** X11 / XWayland: `_NET_ACTIVE_WINDOW` for the instance's window (found through
  `_NET_WM_PID`), via libX11 directly, no extra tools. Pure Wayland gives apps no such call:
  OpenParallel says so instead of pretending.
- **Quit:** `SIGTERM` to the instance's main processes (the same as quitting from the app's
  menu for Chromium, Electron and Firefox). **Force quit:** `SIGKILL` to them and their children.
- **Trash:** the freedesktop.org Trash spec (`~/.local/share/Trash/files` + `.trashinfo`), so
  Files' Trash shows them and Restore works. Refused while the instance runs.
- **Show in Files:** `org.freedesktop.FileManager1.ShowItems` over D-Bus, else `xdg-open` the folder.

### 3.5 Installed apps

Desktop entries from `$XDG_DATA_HOME/applications` and every `$XDG_DATA_DIRS/applications`
(that includes Flatpak's and Snap's export folders). Skipped: `NoDisplay`, `Hidden`, non-
`Application` entries, OpenParallel's own. `Exec` is parsed per spec (quotes, escapes, field
codes dropped, leading `env K=V` kept as environment).

## 4. Interface

Same pages, keyboard map and wording as Windows, with the Linux words above. Tray: Avalonia's
StatusNotifierItem icon (KDE, Ubuntu; GNOME needs the AppIndicator extension, so it stays off
by default as on Windows). Single instance: a second start shows the first window (local socket,
works on both systems; replaces the Windows-only named event).

## 5. Code layout

```text
desktop/src/OpenParallel.Core/          shared engine (models, naming, matcher, service)
desktop/src/OpenParallel.Core/Windows/  WindowsPlatform, Start menu apps, exe inspector, recipes
desktop/src/OpenParallel.Core/Linux/    LinuxPlatform (/proc, signals, X11, trash), desktop
                                        entries, inspector, recipes
desktop/src/OpenParallel.App/           one UI; platform words come from the platform
desktop/src/OpenParallel.Cli/           opctl (both)
desktop/tests/                          unit tests (run on the Mac) + fixtures
desktop/scripts/                        build.sh (win + linux), wine-test.sh, linux-test.sh
```

## 6. Packaging

`OpenParallel-<version>-linux-x64.tar.gz` and `…-linux-arm64.tar.gz`: the app folder plus
`install.sh` (per user, no sudo: `~/.local/opt/OpenParallel`, `~/.local/bin/openparallel`, a
desktop entry and icon) and `uninstall.sh`. AppImage later (needs `appimagetool` and FUSE).

## 7. Testing

- **On the Mac:** unit tests for desktop-entry parsing and writing, `Exec` quoting, the
  inspector over fake app trees, recipes, Flatpak/Snap wrapping, the trash writer, and the
  `/proc` matcher over recorded process lists.
- **In Ali's Parallels Linux VM (ARM64, from his external drive):** `scripts/linux-test.sh`
  installs the build and stand-in apps, then runs end-to-end checks like the Wine suite, with the
  real Chromium and Firefox where the distribution has them. UI checks run the real window on the
  VM's desktop, driven with `xdotool` inside the VM through `prlctl exec`, screenshots through
  `prlctl capture`. Nothing is typed into the Mac.

## 8. Phases

| Phase | What | Done when |
|---|---|---|
| L0 | Move to `desktop/`, split platform pieces behind interfaces | Windows still 29 unit + 39 Wine checks |
| L1 | Linux engine: desktop entries, inspector, recipes, `/proc`, signals, trash, X11 | unit tests pass on the Mac |
| L2 | opctl + Linux stand-in app + `linux-test.sh` | script ready to run in the VM |
| L3 | UI on Linux: wording, Files, tray, single instance | builds for linux-x64/arm64 |
| L4 | tar.gz packages + install/uninstall scripts | packages built |
| L5 | Run everything in the VM, fix what it finds | all checks pass, screenshots reviewed |

## 9. Risks

- Wayland gives no way to raise another app's window; Show works on X11 and XWayland apps only.
- Apps that hand off to a running copy over D-Bus can't be separated by data folders alone.
- Snap confinement and Flatpak permissions vary per app; both start as Experimental.
- Tray icons on stock GNOME need an extension.
- The VM must be ARM64 on this M1 Mac; Parallels' service currently fails to start (restart).

## 10. Progress log

### 2026-10-14: L0–L4 done; L5 waits for the VM

- L0: `windows/` renamed `desktop/`. Platform pieces moved behind `IPlatform` (+ `Name`,
  `ShortcutsFolder`, shortcut naming, argument quoting, Reveal/Open) and the new `IAppKnowledge`
  (inspect, recipes, installed apps, browsers). Windows keeps 29 unit + **39/39 Wine** checks.
- L1: `Core/Linux`: `DesktopEntry` (spec parsing, `Exec` quoting both ways), `LinuxAppKnowledge`
  (families, package/AppImage/Flatpak/Snap wrappers, XDG recipe, single-instance list),
  `LinuxPlatform` (`/proc` argv + environ, SIGTERM/SIGKILL, X11 `_NET_ACTIVE_WINDOW`, gdbus
  ShowItems, xdg-open), `LinuxTrash` (freedesktop spec), `LinuxIcons` (hicolor PNGs),
  `ProcessMatcher.MatchArguments`. **19 Linux unit tests; 48/48 total.** The tests caught that
  manifests were always stamped `windows` (fixed in `InstanceService.Create`).
- L2: opctl runs on both. `tests/fixtures/FakeLinuxApp` + `scripts/linux_test.py`: install.sh,
  detection, two Electron instances launched through their menu entries (`gtk-launch`), running
  detection, the original app not mistaken, SIGTERM quit, Trash refused while running, Trash with
  `.trashinfo`, JetBrains env in `Exec=env`, generic XDG, Firefox/Telegram/VS Code switches, Zoom
  refused, website app window, real Chromium/Firefox when installed, clean up.
- L3: UI words per system (`Words`: Trash, applications menu, Files, system tray), Linux icons
  from desktop entries, file picker for programs/AppImages/.desktop, single instance through a
  Unix socket (`OneInstance`), settings in `~/.config/OpenParallel`.
- L4: `scripts/build-linux.sh --package` → `OpenParallel-1.0.0-linux-arm64.tar.gz` (42 MB),
  `…-linux-x64.tar.gz` (44 MB), each with `install.sh`/`uninstall.sh`.
- L5 (next): Ali's Parallels Ubuntu image (ARM64) from the external drive. `scripts/vm.sh info`,
  then `scripts/vm.sh root 'apt-get install -y xdotool'`, `scripts/vm.sh test`, then the UI pass
  with `vm.sh shot/key/type/click`. Parallels' service failed to start on 2026-10-14 (restart).

### 2026-10-15: L5 on real Ubuntu 24.04 ARM (Parallels, Wayland)

- VM "Ubuntu 24.04 ARM64 (1)" on Ali's drive "THE BOSS". No Mac shared folder, so
  `scripts/vm.sh send` pipes the build into `~/op-test` through `prlctl exec` (stdin works) and
  `scripts/vm.sh test-sent` runs the test. `prlctl exec` takes one shell string; `vm.sh` now
  copies the session's DISPLAY, XAUTHORITY (XWayland's random file), WAYLAND_DISPLAY and D-Bus
  address from a session process, without which no window opens. Keys go in with
  `prlctl send-key-event` (xdotool can't reach Wayland windows).
- End-to-end **46/46**, including the real Firefox and Thunderbird snaps writing their own
  profiles in `~/snap/<name>/common/OpenParallel/<slug>`.
- Found and fixed:
  - `opctl launch` in a script or pipe never returned: the copy inherited opctl's output.
    Apps now start through `/bin/sh -c 'exec "$0" "$@"'` with stdin/out/err on /dev/null
    (same process id; `LinuxPlatform.DetachedArguments`).
  - A Snap Firefox copy showed "Your Firefox profile cannot be loaded": a Snap Firefox refuses a
    `-profile` folder that doesn't exist. Every recipe's folders are made before the first
    start and again on open for older copies (`RecipeBook.ProfileFolders`).
  - Thunderbird (and Betterbird) were "shares data": now Firefox family.
  - The New instance list held 46 entries, settings panels, updaters, monitors and a KDE-only
    System Monitor among them. Entries now follow `OnlyShowIn`/`NotShowIn` against
    `XDG_CURRENT_DESKTOP` and leave out Settings, DesktopSettings, HardwareSettings,
    PackageManager, Monitor and Documentation categories (`LinuxAppKnowledge.Listed`).
  - Icons were blank for system apps: they come from the icon theme (Yaru → Humanity →
    hicolor on Ubuntu) now, read from gsettings or GTK's settings file (`LinuxIcons`).
  - A copy's page said Supported for Snaps and unknown apps, showed the empty instance folder as
    its data and 1 KB as its size. The status and its note are saved with each new copy
    (`StatusAtCreation`, `StatusNotes`; older copies fall back by launch mode); data folder and
    size include the Snap's folder.
- Known, not fixed: on Wayland a Snap Firefox copy shares the original's dock icon (neither
  `--class` nor `--name` changes it); a second start can't raise an open window (GNOME blocks
  self-raising; the dock icon does it). The notification-area icon is off by default.
- Tests 77/77, Wine 39/39. Packages: `artifacts/OpenParallel-1.0.0-linux-arm64.tar.gz` (44 MB),
  `…-linux-x64.tar.gz` (46 MB).
- Same night (Ali asleep, "do all needed"): installed the Chromium snap (155) in the VM. Real
  Chromium copies work; a **website instance** in snap Chromium quit at once because its
  profile was under ~/.local/share. Website profiles now go inside the browser snap's folder
  too, and the `web` folder is made up front (all systems). The test now opens a real website
  instance when a real Chromium is present: **48/48**. Wine 39/39.
- VS Code 1.142 (Microsoft's ARM64 tarball in `~/op-apps`, its `chrome-sandbox` set to root
  4755 as the .deb does; without that Ubuntu 24.04 aborts VS Code itself): it was detected as a
  **Chromium browser** (its folder has Chromium's crash handler and .pak files, and that test ran
  before the VS Code one), so a copy would share the original's extensions and keep its
  updater. The file-based Chromium test now runs after the VS Code and Electron markers. Real VS
  Code copy: own profile, own extensions folder, updater off. The test covers one real app per
  family (Chromium, Firefox, VS Code) and a real website instance: **53/53**.
- Ali: "did you try the apps I listed in Windows in Linux?" Installed the official ARM64 builds:
  Claude 2.42304.0 (.deb; Anthropic's apt source failed to verify in the VM with "Unknown error
  executing apt-key", the documented direct .deb worked and registers the source itself),
  ChatGPT 26.1013.31256 (.deb, Codex inside), Antigravity 2.23.1 (tar.gz, sandbox helper set
  root 4755) and Cursor 3.24.18 (AppImage, `--no-sandbox` in its menu entry). Each ran beside its
  original with its own data. Found and fixed:
  - Claude rewrites its process title, so `/proc/<pid>/cmdline` is one string: its copy was never
    "running". Arguments are now also matched as whole words inside such a title
    (`ProcessMatcher.HasArgument`).
  - ChatGPT's Codex kept using `~/.codex` in the copy (sign-in, config, sessions shared). An
    Electron app with `resources/codex` now gets `CODEX_HOME=<copy>/codex-home`, as on the Mac.
    On Windows a Store app can't be given environment variables: ChatGPT's status there says
    Codex stays shared, and it's no longer in the Tested list.
  Test 56/56 (one real app per family, Electron included); unit 81/81; Wine 39/39.

