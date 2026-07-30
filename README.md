# File Renamer

A simple Windows desktop app (WPF, .NET 9) for batch-renaming or batch-copying files in a folder using
easy-to-read wildcard "masks", with a live preview of the resulting file names before anything happens
on disk.

## Features

- Pick an **input folder** and either:
  - **Rename files in place**, or
  - **Copy files to another folder** (with a destination folder picker)
- Define an **input mask** (how existing file names are matched) and an **output mask** (how new file
  names are built), with inline tips and worked examples in the UI
- **Live preview** grid showing, for every file in the input folder: original name, computed new name,
  and a status (`Rename`, `No change`, `No match`, `Duplicate!`, `Target exists!`)
- One button to actually perform the rename/copy, with a confirmation prompt and a results summary
  (including any per-file errors)

## Mask syntax

| Symbol | In the **input** mask | In the **output** mask |
|--------|------------------------|--------------------------|
| `#`    | Matches one or more digits | Inserts the matched number |
| `*`    | Matches any run of text | Inserts the matched text |
| `0` before `#` | Not special (must match literally unless part of a `#` run) | Zero-pads the number. The padding width equals the length of the run, e.g. `0#` = 2 digits, `00#` = 3 digits. Numbers longer than the width are never truncated. |
| anything else | Must match literally (case-insensitive) | Inserted literally |

### Example

Given files:

```
DVD VIDEO RECORDER_1.mp4
DVD VIDEO RECORDER_12.mp4
```

- Input mask: `DVD VIDEO RECORDER_#.mp4`
- Output mask: `Title-0#.mp4`

Produces:

```
Title-01.mp4
Title-12.mp4
```

Files that don't match the input mask are left unchanged in the preview (and are skipped by the
rename action, though they are still copied as-is when using copy mode).

## Requirements

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (for building/running from source)
  - Running a published, self-contained build does not require the SDK or runtime to be installed

## Project structure

```
FileRename.sln
src/
  FileRename/
    FileRename.csproj      Project file (WPF + Windows Forms for the folder picker dialog)
    App.xaml(.cs)          Application entry point and shared styles/resources
    MainWindow.xaml(.cs)   Main window UI and event handling
    MaskEngine.cs          Pure mask parsing/matching logic (no UI dependencies)
    FilePreviewItem.cs     Row model bound to the preview DataGrid
    Assets/AppIcon.ico     Application icon (used for both the .exe and the window/taskbar icon)
```

## Building

From the repository root:

```powershell
dotnet build
```

This builds `FileRename.sln`, which includes the `src/FileRename/FileRename.csproj` project.

## Running

```powershell
cd src\FileRename
dotnet run
```

## Publishing a standalone executable

To produce a self-contained, single-file `.exe` that can run on a machine without the .NET runtime
installed:

```powershell
cd src\FileRename
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output executable will be under `bin\Release\net9.0-windows\win-x64\publish\`.

## Recommendations for future improvements

- **Undo support** — keep a log of the last rename/copy batch and offer a one-click undo.
- **Subfolder support** — optionally recurse into subdirectories, with an option to mirror the folder
  structure at the destination.
- **Conflict resolution options** — instead of just skipping duplicates/existing targets, offer
  "overwrite", "skip", or "auto-rename" (e.g. append `(1)`) strategies.
- **Saved presets** — let users save/load favorite input/output mask combinations.
- **Drag-and-drop** — allow dragging a folder (or files) onto the window instead of only using the
  folder picker.
- **Additional mask tokens** — e.g. `%d`/`%t` for date/time stamps, `%e` for original extension only,
  or capturing/reordering multiple numbered groups explicitly (`#1`, `#2`, ...).
- **Sorting/filtering the preview grid** — click column headers to sort, or filter to show only rows
  that will change.
- **Multi-threaded copy** for very large folders, with a progress bar and cancel button.
- **Automated tests** — extract the existing manual verification of `MaskEngine` into a proper unit
  test project (e.g. xUnit) so mask-parsing regressions are caught automatically.
- **Dark mode / theming** — respect the Windows light/dark theme setting.
- **Localization** — externalize UI strings for translation.
