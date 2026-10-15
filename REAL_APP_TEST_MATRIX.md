# Real App Test Matrix

Snapshot: 2026-08-19. 36 exact versions across 26 apps. The table below is generated from `Sources/OpenParallel/RealAppApprovals.swift`, the source of truth; superseded versions stay listed because an approval covers its exact version only.

## Approval Meaning

Every row below passed this repeatable test on this Mac:

1. Verify source app code signature.
2. Build two OpenParallel linked-source shortcuts.
3. Launch both shortcuts concurrently.
4. Observe two distinct target-app PIDs.
5. Confirm each process references its own OpenParallel profile root through arguments or environment.
6. Kill only test processes.

This is a **process-and-profile approval**, not an account-level authentication approval. Sign-in, sign-out, keychain, OAuth callback, and every app feature still need account-level acceptance testing before status can become Verified.

## Passed Exact Versions

| App | Bundle ID | Version | Recipe | Instances | Tested (UTC) |
| --- | --- | --- | --- | --- | --- |
| Anki | `net.ankiweb.anki` | 26.8 | HOME profile | 2 | 2026-08-02 |
| Antigravity | `com.google.antigravity` | 2.2.1 | Antigravity | 2 | 2026-08-02 |
| Antigravity IDE | `com.google.antigravity-ide` | 2.1.1 | Antigravity IDE / VS Code | 2 | 2026-08-02 |
| Antigravity IDE | `com.google.antigravity-ide` | 2.5.5 | Antigravity IDE / VS Code | 2 | 2026-08-18 |
| Audacity | `org.audacityteam.audacity` | 3.7.8.0 | HOME profile | 2 | 2026-08-02 |
| Brave Browser | `com.brave.Browser` | 151.1.93.129 | Chromium | 2 | 2026-08-02 |
| Brave Browser | `com.brave.Browser` | 151.1.93.136 | Chromium | 2 | 2026-08-19 |
| Claude | `com.anthropic.claudefordesktop` | 1.24012.9 | Claude Electron profile | 2 | 2026-08-02 |
| Claude | `com.anthropic.claudefordesktop` | 1.32352.1 | Claude Electron profile | 2 | 2026-08-18 |
| Claude | `com.anthropic.claudefordesktop` | 1.32885.1 | Claude Electron profile | 2 | 2026-08-19 |
| Codex | `com.openai.codex` | 26.721.81911 | Codex profile + `CODEX_HOME` | 2 | 2026-08-02 |
| Codex | `com.openai.codex` | 26.803.61601 | Codex profile + `CODEX_HOME` | 2 | 2026-08-18 |
| Cursor | `com.todesktop.230313mzl4w4u92` | 3.10.11 | VS Code | 2 | 2026-08-02 |
| DBeaver Community | `org.jkiss.dbeaver.core.product` | 26.1.3 | Eclipse workspace/config | 2 | 2026-08-02 |
| Discord | `com.hnc.Discord` | 0.0.402 | `DISCORD_USER_DATA_DIR` + `--multi-instance` | 2 | 2026-08-02 |
| Discord | `com.hnc.Discord` | 0.0.407 | `DISCORD_USER_DATA_DIR` + `--multi-instance` | 2 | 2026-08-18 |
| Firefox | `org.mozilla.firefox` | 153.0.1 | Firefox `-profile -no-remote` | 2 | 2026-08-02 |
| Firefox | `org.mozilla.firefox` | 153.0.4 | Firefox `-profile -no-remote` | 2 | 2026-08-18 |
| Gemini | `com.google.GeminiMacOS` | 1.84.4.574 | HOME profile | 2 | 2026-08-02 |
| Ghostty | `com.mitchellh.ghostty` | 1.3.1 | HOME profile | 2 | 2026-08-02 |
| iTerm2 | `com.googlecode.iterm2` | 3.6.11 | HOME profile | 2 | 2026-08-02 |
| iTerm2 | `com.googlecode.iterm2` | 3.6.10 | HOME profile | 2 | 2026-08-19 |
| Obsidian | `md.obsidian` | 1.12.7 | Electron profile | 2 | 2026-08-02 |
| OpenCode | `ai.opencode.desktop` | 1.17.16 | ToDesktop | 2 | 2026-08-18 |
| Openscreen | `com.siddharthvaddem.openscreen` | 1.4.0 | Electron profile | 2 | 2026-08-18 |
| Screen Studio | `com.timpler.screenstudio` | 3.6.0-4214 | Electron profile | 2 | 2026-08-18 |
| Signal | `org.whispersystems.signal-desktop` | 8.21.0 | ToDesktop | 2 | 2026-08-02 |
| Spotify | `com.spotify.client` | 1.2.94.583 | `--mu` + user/cache paths | 2 | 2026-08-02 |
| Spotify | `com.spotify.client` | 1.2.96.518 | `--mu` + user/cache paths | 2 | 2026-08-18 |
| Sublime Text | `com.sublimetext.4` | Build 4200 | HOME profile | 2 | 2026-08-02 |
| Telegram Desktop | `com.tdesktop.Telegram` | 7.0.7 | `-many -workdir` | 2 | 2026-08-02 |
| Tor Browser | `org.torproject.torbrowser` | 15.0.6 | Firefox `-profile -no-remote` | 2 | 2026-08-02 |
| Visual Studio Code | `com.microsoft.VSCode` | 1.127.0 | VS Code | 2 | 2026-08-02 |
| Vivaldi | `com.vivaldi.Vivaldi` | 8.1.4087.64 | Chromium | 2 | 2026-08-19 |
| Zen | `app.zen-browser.zen` | 1.21.10b | Firefox `-profile -no-remote` | 2 | 2026-08-02 |
| Zen | `app.zen-browser.zen` | 1.21.14b | Firefox `-profile -no-remote` | 2 | 2026-08-18 |

## Windows: first real-machine tests

Windows 11 on ARM in Parallels Desktop, running the x64 build under Windows' emulation,
2026-10-15/16. Each row: the copy made from OpenParallel's New instance list and the original app
running at the same time as separate processes, the copy on its own data folder (checked in the
folder and, for Store apps, in the process's arguments). Recorded in
`desktop/src/OpenParallel.Core/InstanceService.cs` (`Ledger`), so these exact versions show as
**Tested** in the Windows app.

| App | Exe | Version | Recipe | Installed from |
| --- | --- | --- | --- | --- |
| Firefox | `firefox.exe` | 142.0 | Firefox `-profile -no-remote` | already on the machine |
| Cursor | `Cursor.exe` | 3.24.18 | VS Code (`--user-data-dir`, `--extensions-dir`, copy's updater off) | official ARM64 user setup |
| Antigravity | `Antigravity.exe` | 2.23.1.0 | Electron `--user-data-dir` | official ARM64 installer |
| Claude | `Claude.exe` | 2.42304.0 | Microsoft Store (MSIX) Electron, started through Windows' activation API | official ARM64 MSIX |
| ChatGPT (Codex) | `ChatGPT.exe` | 155.0.8059.40 | Microsoft Store (MSIX) Electron, started through Windows' activation API | Microsoft Store |

## Linux: first real-machine tests

Ubuntu 24.04.3 LTS on ARM64 (kernel 6.8, GNOME on Wayland) in Parallels Desktop, 2026-10-15/16,
with `desktop/scripts/linux_test.py` (53/53): each copy started from OpenParallel, recognised as
running, writing its own profile, and quit. The Linux app has no tested-version record yet, so
these still show as **Supported** or **Experimental** (Snaps) there.

| App | Package | Version | Recipe |
| --- | --- | --- | --- |
| Firefox | Snap | 129.0.1-1 | Firefox `-profile -no-remote`, profile in `~/snap/firefox/common/OpenParallel/` |
| Thunderbird | Snap | 115.12.2-1 | Firefox family, profile in `~/snap/thunderbird/common/OpenParallel/` |
| Chromium | Snap | 155.0.8059.79 | Chromium `--user-data-dir`, profile in `~/snap/chromium/common/OpenParallel/` |
| Visual Studio Code | Microsoft's ARM64 tarball | 1.142.0 | VS Code (`--user-data-dir`, `--extensions-dir`, copy's updater off) |
| Website instance (example.com) | in the Chromium Snap | 155.0.8059.79 | `--app` with its own profile in the snap's folder |

## WebKit Test

Two generated WebKit shortcuts loaded `https://example.com` concurrently with distinct persistent `WKWebsiteDataStore` UUIDs. This proves WebKit process/store separation. Web-mode account login still needs user acceptance testing.

## Explicit Failures And Replacements

| App | Result | OpenParallel behavior |
| --- | --- | --- |
| Native WhatsApp via linked launcher | Hardcoded bundle ID and App Groups make a second process share one account | Shortcut creation blocked; use a Web shortcut for `https://web.whatsapp.com` |
| Telegram Lite (`org.telegram.desktop`) | App Store sandbox build rejected parallel isolated instances | Shortcut creation blocked; install official Telegram Desktop direct build |
| Generic Discord recipe | Second process exited | Replaced by dedicated Discord recipe; dedicated recipe passed |
| Generic Spotify recipe | Second process exited | Replaced by dedicated Spotify `--mu` recipe; dedicated recipe passed |

## Downloaded Test Sources

- Telegram Desktop 7.0.7: official Telegram macOS download, Developer ID `Telegram FZ-LLC (C67CF9S4VU)`.
- DBeaver Community 26.1.3: official GitHub release DMG, SHA-256 `3585fae75814a440e1d8eef6d192a5edf51360b14aa53273c82fd89cde159f2b`, Developer ID `DBeaver Corporation (42B6MDKMW8)`.
- Sublime Text Build 4200: official Sublime Text macOS ZIP, Developer ID `Sublime HQ Pty Ltd (Z6D26JE4Y4)`, stapled notarization ticket.
- Brave Browser 151.1.93.136 and Vivaldi 8.1.4087.64 (2026-08-19): official vendor DMGs, copied into the lab, extended attributes cleared, then verified with `codesign --verify --strict` before testing.
- Anki, Audacity, Brave Browser, Ghostty, iTerm2, and Signal: installed through checksum-pinned Homebrew casks sourced from each vendor's official download URL, then verified with `codesign` before testing.

Lab downloads and app copies were kept under `~/Library/Application Support/OpenParallel/TestLab/` and were never bundled into OpenParallel. That folder no longer exists on the development Mac, so re-testing an app means downloading it again.
