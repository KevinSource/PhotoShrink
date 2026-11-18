# CLAUDE.md

Notes for working on this repo.

## What it is

PhotoShrink: a WinForms app (.NET 10, `net10.0-windows`) that adds a "Shrink photo > 1200/1600/2400 px"
cascading submenu to Explorer's right-click menu and shrinks the selected photos in place. Per-user
registration only (HKCU). See README.md for user-facing behaviour.

## Layout

```
PhotoShrink.sln
PhotoShrink/                  the app (WinExe, no console window)
  Program.cs                  entry point, dispatch on command line
  CommandLine.cs              argument parsing (--max, --install, --uninstall, --silent, --report)
  BatchCoordinator.cs         mutex + named pipe: merges Explorer's one-process-per-file into one batch
  ImageShrinker.cs            the shrink for one file; never throws, returns ShrinkResult
  ShrinkResult.cs             per-file outcome
  Summary.cs                  summary text, byte formatting, --report JSON lines
  ProgressForm.cs             leader's window: gather → shrink → open SummaryForm
  SummaryForm.cs              result window (stays open)
  SettingsForm.cs             no-argument launch: JPEG quality + install/uninstall buttons
  Settings.cs                 %APPDATA%\PhotoShrink\settings.json (JpegQuality, QuietPeriodMs)
  ShellIntegration.cs         registry keys + Send To .lnk files (IShellLinkW COM interop)
PhotoShrink.TestKit/          console app: end-to-end tests (makes images, launches exe per file)
run-tests.cmd                 build + run the test kit
install.cmd / uninstall.cmd   publish to %LOCALAPPDATA%\Programs\PhotoShrink and --install / --uninstall
```

## Rules that must hold

* System.Drawing only — no image NuGet packages.
* Registry writes only under `HKCU\Software\Classes`. Uninstall must remove everything Install adds:
  `SystemFileAssociations\<ext>\shell\PhotoShrink` for .jpg/.jpeg/.png/.gif (size items
  nested under each, `SubCommands=""`), the legacy `PhotoShrink.Menu` key, empty
  parent keys, and the three Send To shortcuts. If you add a key, add its removal.
* Mutex/pipe names include the size so batches at different sizes never mix.
* A follower process exits only after the leader replied "OK"; otherwise it retries and may become the
  next leader. Don't drop paths.
* The leader must release the mutex on the thread that took it (the UI thread). `CollectAsync` is
  awaited from the form, so its continuation is on that thread.
* Never write into the original file. Temp file in the same folder → read back and verify → `File.Replace`
  (fallback `File.Move(overwrite)`) → restore created/modified dates.
* Apply EXIF orientation before measuring. Skip if long edge ≤ max, or result not ≥10% smaller.
  GIFs are skipped. One file failing must not stop the batch.
* No per-file pop-ups; one summary per batch.

## Build and test

```
dotnet build PhotoShrink.sln -c Release
dotnet PhotoShrink.TestKit\bin\Release\net10.0-windows\PhotoShrink.TestKit.dll PhotoShrink\bin\Release\net10.0-windows\PhotoShrink.exe testrun
```

`run-tests.cmd` does both (double-clickable; writes `test.status` when done). The test kit doesn't register anything. It uses the hidden `--report <file>` switch, which appends a JSON
line per batch and skips the summary window. `testrun/` is git-ignored.

Don't run `--install` on the developer's machine without asking first.

## Not done (on purpose)

* `ExtendedSubCommandsKey` pointing at a shared HKCU key was tried first: Explorer showed the arrow
  but an empty flyout. Keep the items nested under each verb with `SubCommands=""`.
* No packaged IExplorerCommand shell extension; on Windows 11 the menu sits under "Show more options".
* Metadata (EXIF/GPS/ICC) is not copied to the shrunk file.
