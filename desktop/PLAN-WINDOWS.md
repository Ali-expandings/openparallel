# OpenParallel for Windows: plan

Status: approved direction, implementation started 2026-10-14. This file is the single plan
for the Windows version; update it as decisions change.

## 1. What it is

The same product as the Mac app: make a second (third, …) copy of an installed app that has its
own login and data, open it from the Start menu like any app, and see honestly whether that
separation has been proven. The original app is never modified.

Principles carried over from the Mac version, unchanged:

- **Never modify the vendor app.** Copies are launch recipes (arguments, environment), not
  patched binaries.
- **Honest status.** Tested means an exact version passed a two-copy test. Supported means a
  recipe exists. Nothing is called verified without an account-level test.
- **Everything recoverable.** Removing an instance moves it to the Recycle Bin.
- **No telemetry, no background service, no network calls** from OpenParallel itself.
- **Native look.** Built from the platform's standard controls, no custom palette, no card soup.
  On Windows that means the Windows 11 Fluent design language: navigation pane, settings cards,
  content dialogs, info bars, the system accent colour.
- **Keyboard first.** Every action reachable without a mouse.

## 2. Technology

| Option | Native look | Builds on this Mac | Runs in Wine | Verdict |
|---|---|---|---|---|
| WinUI 3 (Windows App SDK) | Best | No (XAML compiler is Windows-only) | No | Rejected: cannot build or test here |
| WPF + WPF-UI | Very good | Yes (`EnableWindowsTargeting`) | Uncertain for .NET (core) WPF | Fallback |
| **Avalonia 12 + FluentAvaloniaUI 3** | Very good: WinUI controls (NavigationView, SettingsExpander, ContentDialog, InfoBar) | Yes | Own renderer (Skia), best odds | **Chosen** |
| WinForms | Dated | Yes | Good | Rejected: looks like 2005 |
| Electron / Tauri | Web | Yes | Partial | Rejected for the same reasons as on the Mac |

Stack: **.NET 10 LTS**, **Avalonia 12.1**, **FluentAvaloniaUI 3.1**, **FluentIcons.Avalonia**
(Fluent System Icons), xUnit for tests. Published as a self-contained single-file `win-x64`
(and later `win-arm64`) executable, so nothing has to be installed first.

Wine risk: Avalonia's GPU path goes Skia → ANGLE/Direct3D → Wine → Metal. If the window renders
blank or corrupt, OpenParallel switches to Avalonia's software renderer when it detects Wine
(`ntdll!wine_get_version`). Phase 0 is a go/no-go smoke test of exactly this, before any real UI.

## 3. How a second copy works on Windows

### 3.1 Recipes

| Family | Detected by | Launch recipe | Status |
|---|---|---|---|
| Chromium browsers (Chrome, Edge, Brave, Vivaldi, Opera) | `chrome_elf.dll` / known exe names | `--user-data-dir="<data>\chromium"` | Supported |
| Electron apps (Claude, Slack, Notion, Obsidian, Discord, …) | `resources\app.asar` or `resources\app\package.json` | `--user-data-dir="<data>\electron"` | Supported |
| Squirrel-installed Electron apps | `Update.exe` beside `app-*\` folders | `Update.exe --processStart <exe> --process-start-args "--user-data-dir=…"` so vendor updates keep working | Supported |
| VS Code family (Code, Cursor, Windsurf, VSCodium, Antigravity) | `resources\app\product.json` | `--user-data-dir … --extensions-dir …` | Supported |
| Firefox family (Firefox, LibreWolf, Waterfox, Zen, Floorp) | `application.ini` + `omni.ja` | `-profile "<data>\firefox" -no-remote` | Supported |
| Telegram Desktop | `Telegram.exe` | `-many -workdir "<data>\telegram"` | Supported |
| JetBrains IDEs | `bin\idea.properties` | `<PRODUCT>_PROPERTIES` env pointing at a per-instance properties file | Experimental |
| Packaged (MSIX / Store) apps | path under `WindowsApps` | Activation with arguments via `IApplicationActivationManager` | Experimental |
| Any other app | — | Starts another process; data stays shared | Shares data |
| Single-instance apps (Zoom, Steam, Office, WhatsApp) | known list | — | Not supported (offer a website instance where one exists) |

The recipe catalogue is data (`recipes.json` compiled in), so adding a family does not touch code.

### 3.2 Website instances

Every Windows 10/11 has Microsoft Edge. A website instance runs the user's Chromium browser
(Edge by default, or Chrome/Brave if chosen) as an app window:
`msedge.exe --app=<url> --user-data-dir="<data>\web"`. That gives its own window, taskbar entry,
cookies and storage, with no embedded browser engine to ship or patch.

### 3.3 Where things live

```text
Instances:  %LOCALAPPDATA%\OpenParallel\Instances\<slug>\instance.json  (+ the app's data folders)
Shortcuts:  Start Menu\Programs\OpenParallel\<Name>.lnk   (optional desktop shortcut)
Logs:       %LOCALAPPDATA%\OpenParallel\Logs\<slug>.log
Settings:   %LOCALAPPDATA%\OpenParallel\settings.json
```

`instance.json` uses the Mac schema (name, slug, family, launchMode, sourcePath, versions,
profileIsolation, wrapperPath, createdAt) plus Windows fields (arguments, environment keys,
appUserModelId, launch target). A folder that holds files keeps its name reserved; an empty one
can be reused.

### 3.4 Shortcuts

`.lnk` files written through `IShellLinkW`/`IPropertyStore`: target, arguments, working folder,
the source exe's icon, a description, and a per-instance `System.AppUserModel.ID` so Windows can
group the copy separately where the app allows it. Recipes that need only arguments point the
shortcut straight at the vendor exe (or `Update.exe`) and work even without OpenParallel.
Recipes that need environment variables point at `OpenParallel.exe --launch <slug>`, which
starts the app with the right environment and exits.

### 3.5 Running, Show, Quit, Remove

- **Running:** the Mac rule, unchanged: a process belongs to an instance when its image lives in
  the source app's folder and its command line or environment points inside that instance's data
  folder (matched with a trailing separator, so `claude` never matches `claude-2`). Command lines
  come from `NtQueryInformationProcess`; environments from the process parameters block.
- **Show:** restore and bring forward that process's main window.
- **Quit:** send `WM_CLOSE` to its top-level windows, the way the taskbar's Close window does. If
  it is still running after a few seconds, offer **Force Quit** as an explicit button. Never kill
  silently.
- **Move to Recycle Bin:** shortcut(s), data folder and log through `SHFileOperation` with undo,
  disabled while running.

### 3.6 Finding installed apps

Start Menu shortcuts (all users and current user) resolved to their targets, plus the Uninstall
registry keys (`DisplayIcon`, `InstallLocation`) and App Paths. Duplicates are merged; uninstallers,
helpers (Firefox's `private_browsing.exe`), installer caches (`\Package Cache\`) and OpenParallel's
own shortcuts are skipped. Microsoft Store apps have neither a shortcut file nor an Uninstall entry:
they come from this user's package list (`HKCU\Software\Classes\Local Settings\Software\Microsoft\
Windows\CurrentVersion\AppModel\Repository\Packages\<full name>`, value `PackageRootFolder`) and
each package's `AppxManifest.xml`. Only desktop (full-trust) apps shown in the Start menu are
listed; Windows' own packages (`Microsoft*`, `Windows.*`), frameworks, resource packages and
helpers with `AppListEntry="none"` are skipped, and the newest version wins.

## 4. Interface (Windows 11 Fluent, mapped from the Mac design you liked)

| Mac | Windows |
|---|---|
| Sidebar grouped by source app, green dot when running | NavigationView pane: one header per app, an item per instance with its app icon and an InfoBadge dot when running; **New instance** button at the top; **Settings** in the footer |
| Grouped form detail pane | Instance page built from SettingsCards: header (icon, name, Running, **Open/Show** accent button, **Quit**), Compatibility, Own data, Size on disk, Folder (Open folder), Source app and version, Shortcut, **Move to Recycle Bin** |
| New Instance sheet | A page in the content frame: App / Website selector; app list with search box, grouped by what an instance gets; then a settings page with Name, Separate login and data, Open after creating, and an **Advanced launch options** expander (the whole header is clickable) |
| Website presets | Same presets, plus a browser picker (Edge default) |
| Settings window | Settings page: shortcuts (Start menu, also on desktop), theme (System/Light/Dark), notification-area icon, open after creating, about and privacy |
| Menu-bar menu | Notification-area icon with the same menu |
| Confirmation sheet | ContentDialog |
| Empty state | Centred icon, title, one line, **New instance** and **New website instance** |

Keyboard map (Windows conventions):

| Keys | Action |
|---|---|
| Ctrl+N / Ctrl+Shift+N | New instance / new website instance |
| Ctrl+F | Search instances |
| Enter, double-click | Open, or bring a running instance forward |
| Ctrl+Shift+Q | Quit the selected instance |
| Delete | Move to Recycle Bin (asks first) |
| Ctrl+E | Open its data folder |
| F5 | Refresh |
| Ctrl+, | Settings |
| Alt+Left, Esc | Back / cancel in the new-instance flow |
| Ctrl+1 / Ctrl+2 | App / Website in the new-instance flow |
| Up/Down from the search box | Move through the app list; Enter continues |

Accessibility: names on every icon-only control, visible keyboard focus, theme follows Windows
light/dark and high contrast.

## 5. Repository layout

```text
windows/
  PLAN.md                         this file
  README.md                       build, test, run
  OpenParallel.Windows.slnx
  src/OpenParallel.Core/          engine: recipes, detection, manifests, matcher, Windows APIs
  src/OpenParallel.App/           Avalonia + FluentAvalonia UI (OpenParallel.exe)
  src/OpenParallel.Cli/           console tool (opctl.exe) for tests and power users
  tests/OpenParallel.Core.Tests/  xUnit, runs on macOS
  tests/fixtures/                 tiny stand-in apps: Electron-like, Chromium-like, Firefox-like,
                                  Telegram-like, single-instance; each logs its arguments and
                                  environment and answers WM_CLOSE
  scripts/build.sh                publish win-x64 self-contained single-file
  scripts/wine.sh                 run anything inside the test bottle
  scripts/wine-test.sh            install fixtures in the bottle and run the end-to-end scenarios
```

## 6. Testing

1. **Unit tests on macOS** (`dotnet test`): manifests, naming and slugs, recipe → command line,
   process matcher (including the `claude` vs `claude-2` case), app grouping, status wording.
2. **End-to-end in Wine, scripted** (`scripts/wine-test.sh`): fixtures installed under
   `C:\Program Files` in the bottle; `opctl` creates instances, checks the `.lnk` files, launches,
   reads the fixture logs to prove each copy got its own data folder, detects running, quits,
   moves to the Recycle Bin. Repeatable, no clicking.
3. **Interface in Wine**: OpenParallel.exe in the bottle, driven with guarded keystrokes and
   screenshots: every flow, every shortcut, light and dark.
4. **Not testable in Wine, stated plainly:** Mica and exact Windows 11 visuals, Store/MSIX apps,
   Edge, real vendor apps, taskbar grouping, SmartScreen. These need a real Windows machine
   before release.

Test bottle: one Sikarugir wrapper, `OpenParallel Test.app`, made with Sikarugir Creator on the
`WS12WineCX24.0.7_5` engine, with OpenParallel.exe set as its program so double-clicking the
wrapper opens the Windows build.

## 7. Phases

| Phase | Done when |
|---|---|
| 0. Toolchain, bottle, smoke test | .NET 10 SDK works here; the bottle exists; an Avalonia + FluentAvalonia window renders correctly in it (else switch renderer or stack) |
| 1. Engine + CLI + fixtures | Core and `opctl` build; unit tests pass on macOS |
| 2. Wine end-to-end (CLI) | The scripted scenario passes in the bottle |
| 3. Interface | Every screen in section 4 implemented |
| 4. Interface tests in Wine, polish | Every flow and shortcut checked in the bottle; no custom styling left |
| 5. Package and document | `dist/windows/OpenParallel.exe` (win-x64), README, vault handoff |

## 8. Risks

| Risk | Plan |
|---|---|
| Avalonia renders badly in Wine | Software renderer under Wine; if still broken, fall back to WPF + WPF-UI |
| Disk space (13 GB free) | About 2.5 GB total: SDK ~0.8 GB, packages ~0.3 GB, bottle ~1.2 GB, builds ~0.2 GB; clean intermediates |
| Wine is not Windows | Every claim says where it was verified; real-Windows checklist kept in the README |
| Unsigned exe | SmartScreen warns on first run until there is a code-signing certificate |
| Store apps | Experimental at best; website instances cover the common ones (WhatsApp, Teams) |

## 9. Not in this version

The Mac-only private native WhatsApp copy; an embedded browser engine; installer signing;
auto-update; Linux build (possible later with the same Avalonia code).

## 10. Progress log

### 2026-10-14: Phase 0 done (go)

- .NET SDK 10.0.402 installed for this user only in `~/.dotnet` (no admin). `scripts/env.sh` sets
  `DOTNET_ROOT`, opts out of SDK telemetry, and points at the test bottle.
- Test bottle created with **Sikarugir Creator** (driven through accessibility, no clicks):
  `~/Applications/Sikarugir/OpenParallel-Test.app`, engine `WS12WineCX24.0.7_5`. Wine binary
  `Contents/SharedSupport/wine/bin/wine`, prefix `Contents/SharedSupport/prefix`, C: drive
  `Contents/SharedSupport/prefix/drive_c`. Reports Windows 10.0.19045, x64, user `crossover`.
  Run anything with `scripts/wine.sh <exe> [args]`; close gracefully with `scripts/wine.sh taskkill /im <name>`.
- **Avalonia 12.1.4 + FluentAvaloniaUI 3.1.0 render correctly in Wine** with software rendering
  (`Program.cs` switches automatically when `ntdll!wine_get_version` exists).
- FluentAvalonia 3 renamed every control with an `FA` prefix: `FANavigationView`,
  `FANavigationViewItem(Header)`, `FASettingsExpander(Item)`, `FAContentDialog`, `FAInfoBar`,
  `FAInfoBadge`, `FASymbolIcon`, `FAFrame`, `FATaskDialog`, `FAFontIconSource`. Theme class:
  `FluentAvalonia.Styling.FluentAvaloniaTheme`. `IsBackButtonVisible` is a bool.
- **.NET single-file publish fails under Wine** ("Incorrect alignment" loading CoreLib from the
  bundle). Test builds are published as a folder (`-p:DebugType=none`); a real-Windows release can
  stay a folder inside an installer/zip.
- Disk: NuGet pulls Linux/WebAssembly Skia natives (~600 MB) a Windows build never uses; prune
  `~/.nuget/packages/*nativeassets.{linux,webassembly}` when space is tight.

### 2026-10-14: Phases 1–2 done

- Engine (`OpenParallel.Core`): models, slugs (Mac rule), Windows command-line quoting, app
  inspector, recipe book (Chromium, Electron, Squirrel via `Update.exe`, VS Code, Firefox,
  Telegram, JetBrains via launcher, website app windows), instance service, process matcher,
  `WindowsPlatform` (`.lnk` + AUMID, Recycle Bin, process command lines and environments, Show
  and Close via window messages), installed-app discovery.
- `opctl` command line and `OpenParallel.exe --launch <slug>`.
- **28/28 unit tests on macOS** (`dotnet test --project tests/OpenParallel.Core.Tests`).
- **39/39 end-to-end checks in the Wine bottle** (`scripts/build.sh && scripts/wine-test.sh`),
  using one stand-in app (`tests/fixtures/FakeApp`) that plays each family by file name and logs
  what it was started with.
- Wine gotchas: `%TEMP%` is `C:\users\crossover\Temp` (on disk `users/Sikarugir`); Wine's helper
  processes inherit stdout, so never read Wine output through a pipe (use files).

### 2026-10-14: Phases 3–5 done (UI, Wine UI test, package)

- UI (`src/OpenParallel.App`): sidebar grouped by source app with icons, running dots and search;
  instance page (status, own data, folder, size, source, shortcuts, how it starts with Copy
  command, log, Move to Recycle Bin); new-instance flow (grouped app list, Browse…, drop,
  options, Advanced arguments + environment, website form with browser picker and presets,
  web alternative for unsupported apps); Settings (theme, defaults, notification area,
  locations, keyboard map, privacy); empty and older-shortcut pages; notification-area icon;
  one OpenParallel per user (a second start shows the first window, even from the tray).
- Engine additions: `InstanceService.MissingShortcuts/RestoreShortcuts` (shortcuts rebuilt from
  the manifest), `InstalledApps.ResolveExe` public. **29/29 unit tests, 39/39 Wine checks.**
- **UI tested in the bottle by hand-driving the real window** (`scripts/winui.swift`: finds the
  Wine window, screenshots via MacOS-MCP, posts keys only to that PID, clicks only when the
  window under the point is the target). `scripts/wine-test.sh --setup-ui` installs the build
  plus nine stand-in apps in the Start menu. Passed: Ctrl+N/Ctrl+Shift+N/Ctrl+1/2/Ctrl+F/Ctrl+,/
  F5/Ctrl+O/Ctrl+Enter/Ctrl+Shift+Q/Delete/Esc/Alt+Left; search + Up/Down + Enter; create app
  (Electron, VS Code with argument + environment, JetBrains launcher, Squirrel newest version),
  website (preset, web alternative from Zoom, open-after off); shares-data toggle locked;
  taken-name hint; Quit; Show doesn't start a second copy; Recycle Bin dialog (Esc cancels,
  confirm removes shortcut + data); missing-shortcut Restore; older-shortcut page + removal;
  dark theme; close-to-tray + second start brings it back; Windows file picker; right-click menu;
  double-click to open.
- Bugs found by that pass and fixed: footer pushed Continue off-screen; Source card squeezed the
  path; long names truncated; preset name overwritten (Avalonia raises TextChanged late, so
  "edited" is now "differs from the last suggestion"); source named after the exe's version info
  instead of the Start menu; context-menu shortcut column clipped; double-click swallowed by the
  nav item; taken-name hint squeezed.
- **Wine-only gaps (check on real Windows):** Show doesn't raise another app's window
  (`SetForegroundWindow` across processes); the tray icon exists (explorer.exe status item) but
  ignores accessibility clicks and hides behind the notch when the menu bar is full; the `.lnk`
  AppUserModelID reads back empty; Recycle Bin = macOS Trash (test runs leave small items there).
- Package: `scripts/build.sh --zip` → `artifacts/OpenParallel-1.0.0-windows-x64.zip` (46 MB;
  folder 110 MB after dropping SkiaSharp/HarfBuzz native .pdb files). Not trimmed: reflection
  JSON and COM interop make trimming risky for ~40 MB.
- Bottle linked: `scripts/link-bottle.sh` sets the wrapper's program to
  `C:\OpenParallel\OpenParallel.exe` and its icon; double-clicking `OpenParallel-Test.app` opens
  OpenParallel for Windows. Stop test runs first (`wineserver -k`): the wrapper waits otherwise.

### 2026-10-14: Microsoft Store apps (first real-Windows report)

- Ali's friend's real Windows test: Claude showed "Not supported" because every app under
  `WindowsApps` was refused, and the red bar gave a second, contradictory reason ("allows only one
  copy"). Claude on Windows is always an MSIX package (`Claude_pzs8sxrjxfjjc`).
- Now (`Core/Windows/PackagedApps.cs`): a Store app whose package holds Electron
  (`resources/app.asar`) is **Experimental**. The manifest gives its AppUserModelId
  (`PackageFamilyName!AppId`); the copy's launch target is `shell:AppsFolder\<AUMID>`, started with
  `IApplicationActivationManager::ActivateApplication` (out of process, arguments passed), mode
  `packaged`, so its shortcut goes through `OpenParallel.exe --launch <slug>`. The profile folder
  is created before the first start (MSIX redirects only folders that don't exist yet), the
  shortcut icon is the package's largest transparent logo saved as `icon.ico` in the instance
  folder (package folders change on every update), recognition keys on the `WindowsApps` folder
  plus the data folder on the command line, and removal also takes the redirected
  `Packages\<family>\LocalCache\Local\…` copy if Windows made one. Other Store apps stay Not
  supported, with a website instance offered for WhatsApp, Spotify, Telegram, Teams and Claude.
- Matching reads a process's environment only for copies whose recipe uses environment variables
  (it read every process in the app's folder before; with `WindowsApps` as the folder that would
  have been every Store process).
- Tests 58/58 (7 new in `PackagedTests.cs`). Wine can't run Store apps: the real check is the
  friend's machine (create a Claude copy, sign in to another account, both open at once).
- Same day, second report: a Cursor copy showed Inno Setup's "Setup has detected that Setup is
  currently running". A copy has its own settings, so it ran its own updater against the same
  installed program while the original's was running. VS Code-family copies (Cursor, VS Code,
  Windsurf…) now start with `"update.mode": "none"` and
  `"update.enableWindowsBackgroundUpdates": false` in their own `settings.json`
  (`RecipeBook.LeaveUpdatesToTheOriginal`, also on Linux); copies made earlier get it the next
  time OpenParallel opens them; an update mode the person set is kept. Updates come from the
  original, which updates the shared program for both. Tests 61/61, Wine 39/39; build sha256
  12a07100…48d5dd at the same link.

### 2026-10-15: real Windows 11 (ARM) in Parallels

- Ali's VM "Windows 11 mie" (Windows 11 on ARM, the x64 build runs under emulation), driven by
  keyboard through `prlctl send-key-event` and read through screenshots.
- Firefox: copy created, opened on a fresh profile, shown as Running, quit to Not running; the
  original Firefox ran beside it on its own profile; the Start menu has "Firefox Parall".
- Installed for the test (official ARM64 installers, Ali's OK): Cursor 3.24.18 (user setup),
  Antigravity 2.23.1, Claude 2.42304 (MSIX; needs admin for its Cowork service, Ali approved it),
  ChatGPT 155.0.8059 from the Store (the Codex app now ships inside it; Ali approved its admin
  prompt).
- Found and fixed: Store apps never reached the New instance list (no shortcut file, no Uninstall
  entry), so Claude and ChatGPT were missing; now listed from the package registry (3.6). Firefox
  Private Browsing was offered as an app of its own, and the .NET SDK's installer showed up; both
  filtered. Tests 70/70.
- After the fix the list shows Antigravity and Cursor as Supported, Claude and ChatGPT as
  Experimental (Store apps built on Electron). The Antigravity copy opened on its own first-run
  screen with its own data folder while the original ran.
- Not finished: the VM paused when its disk (external drive "THE BOSS") filled up (391 MB free),
  in the middle of the Cursor copy. Still to check there: Cursor, Claude and ChatGPT copies
  starting with their own data, side by side with the originals.
- Closing the window keeps OpenParallel in the notification area (setting "Keep in notification
  area"). A second start then did not bring the window back, but that process's files had just
  been replaced by a rebuild; ending it and starting the new build worked. Recheck "close, then
  start again" on a clean run.

