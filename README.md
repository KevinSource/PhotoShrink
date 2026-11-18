# PhotoShrink

Adds a **Shrink photo** submenu to the Windows right-click menu for `.jpg`, `.jpeg`, `.png` and `.gif`
files. It shrinks the selected photos so the long edge is at most the size you pick:

```
Shrink photo  >  1200 px
                 1600 px
                 2400 px
```

Select one photo or hundreds, right-click, pick a size. PhotoShrink asks once ("Shrink 23 selected
files…?", saying what will happen to the originals), shows a small progress window, then one summary
window listing what was shrunk, skipped and failed, with an **Undo** button.

On Windows 11 the menu is under **Show more options** (or Shift+right-click), because it is a classic
menu entry.

## What happens to the originals

Choose in the settings window (**Settings…** on the confirmation or summary window, or run
`PhotoShrink.exe` with no arguments) under **When shrinking**:

| Option | The photo | The original |
|---|---|---|
| **Replace, original to Recycle Bin** (default) | replaced by the shrunk version | in the Recycle Bin |
| **Replace, original to Originals folder** | replaced by the shrunk version | moved to `Originals\` next to it |
| **Save a copy, leave the original** | untouched | untouched; shrunk copy saved as `name (1600).jpg` |

* **Undo** on the summary window reverses the whole batch: it puts each original back (from the
  Recycle Bin or the Originals folder) and deletes the shrunk version, or in copy mode deletes the
  copies. It only works while that summary window is open. After that, restore from the Recycle Bin or
  the Originals folder by hand. Undo leaves a file alone if you've changed it since it was shrunk.
* **Drives without a Recycle Bin** (network shares, most USB sticks and SD cards): Windows would
  delete the original permanently there, so PhotoShrink uses the Originals folder instead and says so in
  the summary.
* If an original is bigger than your Recycle Bin can hold, Windows asks before deleting it permanently.
  Answer **No** and that file fails with its original untouched.
* The confirmation dialog can be turned off with **Don't ask me again**, and back on in settings
  (**Ask before shrinking**).

## Build

Open `PhotoShrink.sln` in Visual Studio and build, or from a terminal in this folder:

```
dotnet build PhotoShrink.sln -c Release
```

Needs the .NET 10 SDK (Windows). No NuGet packages: image work uses System.Drawing, which comes with
WinForms.

## Install

Easiest: double-click **`install.cmd`**. It publishes the program as a single `PhotoShrink.exe` in
`%LOCALAPPDATA%\Programs\PhotoShrink` and registers it from there. The exe is framework-dependent
(about 0.3 MB): it needs the .NET 10 Desktop Runtime, which the .NET 10 SDK includes.

The menu points at whatever copy of `PhotoShrink.exe` you install from, so put it somewhere it will
stay (not in `bin\Debug`, which a Clean deletes). By hand, that's:

```
dotnet publish PhotoShrink\PhotoShrink.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=embedded -o "%LOCALAPPDATA%\Programs\PhotoShrink"
"%LOCALAPPDATA%\Programs\PhotoShrink\PhotoShrink.exe" --install
```

or double-click `PhotoShrink.exe` and click **Add to right-click menu** in the settings window. (When it is already on the menu, the button
reads **Repair right-click menu**, or **Use this copy for the menu** if the menu points at a different copy.)

Install is per user: it writes only under `HKEY_CURRENT_USER\Software\Classes` and your Send To
folder, so it doesn't need admin rights and doesn't affect other users. It adds:

* the **Shrink photo** submenu for `.jpg`, `.jpeg`, `.png`, `.gif`
  (`HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\PhotoShrink`, with the three size items
  nested under it in a `shell` subkey, flagged by an empty `SubCommands` value). Because it's registered by file extension under
  `SystemFileAssociations`, it keeps working if you change your default photo app.
* three **Send To** shortcuts: *Shrink photos - 1200 px*, *- 1600 px*, *- 2400 px*. Send To works for
  any file type you select (non-photos are skipped and listed) and passes all files to one process.

If you move the program, run `--install` again from the new location.

## Uninstall

Double-click **`uninstall.cmd`** (removes the menu, the shortcuts and the program folder), or run

```
PhotoShrink.exe --uninstall
```

or click **Remove from right-click menu** in the settings window. This removes every registry key PhotoShrink added (the
`PhotoShrink` verb under each extension, the old `PhotoShrink.Menu` key from version 1 if present, and any
`SystemFileAssociations\<ext>\shell` parent keys it left empty) and the three Send To shortcuts.
Then delete the program folder, and `%APPDATA%\PhotoShrink` if you want the setting gone too.

Add `--silent` to `--install` / `--uninstall` to skip the confirmation message box.

## What "shrink" does

For each file:

1. **Turns phone photos upright.** If the photo has an EXIF orientation tag (values 1–8), the pixels are
   rotated/flipped to match, then the size is measured.
2. **Skips it, untouched,** if the long edge is already at or below the chosen size. Small photos are
   never enlarged.
3. **Redraws it** at the new size with high-quality bicubic resampling.
4. **Saves it in the same format:**
   * JPEG at quality 85 (change it in the settings window; stored in
     `%APPDATA%\PhotoShrink\settings.json`).
   * PNG as PNG, keeping transparency.
   * GIF: **skipped** — redrawing would throw away animation frames.
   * anything else: skipped and listed.
5. **Skips it, untouched,** if the new file wouldn't be at least 10% smaller than the original.
6. **Writes the new image safely:** it goes to a temp file in the same folder and is read back to check
   it's a valid image of the right size. Only then is the original moved (to the Recycle Bin or the
   Originals folder) and the new file renamed into its place, or, in copy mode, renamed to
   `name (1600).ext`. The original is never written to or half-overwritten.
7. **Gives the shrunk file the original's created and modified dates**, so photo apps and "sort by date"
   still put it in the same place.

One bad file doesn't stop the others; it's listed under *Failed* with the reason.

### Metadata

Re-saving drops the photo's metadata: EXIF (camera, exposure, **date taken**, **GPS location**),
XMP/IPTC tags, and any embedded colour profile. The file's own created/modified dates are kept. If you
need the GPS location or "date taken" field, use the Originals-folder or copy option, or restore the
original from the Recycle Bin.

## Selecting many photos

A classic menu entry makes Explorer start **one PhotoShrink process per selected file**. PhotoShrink
merges them:

* The first process takes a named mutex (`Local\PhotoShrink-<size>`) and listens on a named pipe
  (`PhotoShrink-<size>-s<session>`).
* Every later process sends its file path to the first one, waits for an "OK", and exits — no window.
* When no new file has arrived for 1.5 seconds, the first process starts work on the whole batch and
  shows one summary.

The size is part of the names, so a 1200 px batch and a 1600 px batch started at the same time stay
separate. A process that arrives just after a batch has closed starts a new batch rather than getting
lost. The 1.5 s wait can be changed with `"QuietPeriodMs"` in `settings.json`.

The menu items set `MultiSelectModel=Player`, which removes Explorer's usual 15-file limit for classic
menu entries.

## Command line

```
PhotoShrink.exe                          settings window
PhotoShrink.exe --max 1600 file [file…]  shrink files (any size from 16 to 30000)
    --mode recycle|originals|copy        override the "When shrinking" setting for this run
    --yes                                skip the confirmation dialog
PhotoShrink.exe --install [--silent]
PhotoShrink.exe --uninstall [--silent]
```

## Testing

`PhotoShrink.TestKit` is an end-to-end test that does not touch the registry. It creates test images
(large landscape JPEG, portrait JPEG with EXIF orientation 6, transparent PNG, GIF, small JPEG, corrupt
`.jpg`, a `.bmp`), then starts `PhotoShrink.exe` one process per file, the way Explorer does, at each of
the three sizes on fresh copies. It also checks all eight EXIF orientations, 55 files at once, two
batches at different sizes started together, Send To style (one process, all files), and each of the
three "When shrinking" options with Undo. The Recycle Bin test sends a few test files to your Recycle
Bin and its Undo takes them straight back out, so nothing is left there.

```
dotnet build PhotoShrink.sln -c Release
dotnet PhotoShrink.TestKit\bin\Release\net10.0-windows\PhotoShrink.TestKit.dll ^
    PhotoShrink\bin\Release\net10.0-windows\PhotoShrink.exe testrun
```

Or just double-click `run-tests.cmd`, which builds and runs the tests. Results are printed and saved
to `testrun\results.txt`.
