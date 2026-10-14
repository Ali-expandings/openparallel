# Contributing to OpenParallel

Thanks for helping. The most useful contributions, in order:

1. **Test reports.** An app, its exact version, your system, and what happened when you made a copy. That's what moves an app from Supported to Tested on the [apps list](https://openparallel.pages.dev/apps/). Use the [issue form](https://github.com/Ali-expandings/openparallel/issues/new?template=separate_data.yml).
2. **Bug reports** with steps to see the problem again. **Report a problem** in the app writes most of the report for you.
3. **Recipes** for app families OpenParallel doesn't know yet.
4. Fixes and improvements to the apps, the engine or the website.

## Ground rules

These are what make OpenParallel trustworthy, so pull requests are held to them:

- **The apps you copy are never touched.** No writing into another app's bundle or install folder, no patching, no re-signing. A copy is a launcher plus a data folder.
- **Honest statuses.** A status only goes up with evidence. *Tested* means that exact version ran as two copies with separate processes and separate data folders; *Verified* means a real sign-in and sign-out check. An app update sends Tested back to Supported. When unsure, choose the lower status.
- **Nothing leaves the computer.** No telemetry, analytics, crash reporting, update checks or background services. Report a problem hands the report to the user's own mail app or browser.
- **Removal is reversible.** Delete through the Trash or Recycle Bin, never permanently.
- **Native first.** The macOS app uses standard SwiftUI and AppKit components; Windows and Linux use Avalonia with FluentAvalonia. No custom palettes or web views pretending to be native controls.
- **Match the code around you**: its naming, comment density and idioms.

## Getting set up

| Part | Where | Requirements | Test |
|---|---|---|---|
| macOS app and CLI | `Sources/`, `Tests/`, `scripts/` | macOS 14+, Swift 6 | `./scripts/test.sh` |
| Windows and Linux | `desktop/` | .NET 10 SDK | `cd desktop && dotnet test --project tests/OpenParallel.Core.Tests` |
| Website | `web/` | any shell | `web/build.sh`, then serve `web/dist` |

The Windows build can be made and tested end to end on a Mac with Wine (`desktop/scripts/wine-test.sh`); the Linux build has a matching harness for a VM (`desktop/scripts/linux-test.sh`). See [desktop/README.md](desktop/README.md).

## Adding a recipe

A recipe is how a copy of one kind of app gets its own data: usually a command-line switch the app already supports (`--user-data-dir`, `-profile`, `-workdir`), sometimes an environment variable or a settings file.

- **macOS:** `Sources/OpenParallel/Engine.swift` (`CompatibilityProfile.analyze` recognises the family; the launcher script passes the switches).
- **Windows:** `desktop/src/OpenParallel.Core/AppInspector.cs` recognises the app; `RecipeBook.cs` turns it into a launch plan.
- **Linux:** `desktop/src/OpenParallel.Core/Linux/LinuxAppKnowledge.cs`, sharing `RecipeBook.cs`.

Add a test that inspects a stand-in app and checks the plan (see `desktop/tests/OpenParallel.Core.Tests` and `Tests/OpenParallelTests`). A new recipe starts as **Experimental** or **Supported**, never Tested.

## Recording a tested version

On macOS, `./scripts/real-app-smoke-test.sh --app /Applications/<App>.app --instances 2` runs the repeatable test: two copies, two processes, two profile roots, source signature intact. A version that passes goes into `Sources/OpenParallel/RealAppApprovals.swift` and [REAL_APP_TEST_MATRIX.md](REAL_APP_TEST_MATRIX.md), and its row in `web/data/register.json` becomes `tested`. Please include the smoke-test output in the pull request.

## Pull requests

- Keep each pull request to one change, with tests for behaviour changes.
- Run the tests for every part you touched.
- Describe how you checked it with a real app, or why that wasn't possible.
- User-facing text follows [PRODUCT.md](PRODUCT.md): plain, calm and exact, with no hype words, and never claiming more than was shown.

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE), and you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
