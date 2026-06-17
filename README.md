# Tayler Log Tailer

A lightweight Windows desktop app for tailing folders of log files in real time.
Point a window at a folder and Tayler shows a combined, auto-scrolling view of
every matching log file in that folder.  As files grow, new lines appear
immediately; when a new log file is created in the folder, it is picked up
automatically.

Built with WPF on .NET 10 (Windows).

## Features

- One window per folder.  Open as many windows as you like, each watching a
  different folder.
- Combined view with two columns: the originating file name and the log line.
- Automatic tailing via `FileSystemWatcher` plus a periodic poll fallback, so
  rapid writes and network shares are both handled.
- New log files created in a watched folder start showing up automatically.
- Handles file truncation / rotation (the read offset resets) and reassembles
  partial lines that are split across writes.
- Configurable per window:
  - Glob pattern (one or more, separated by `;`, e.g. `*.log;*.txt`).  Default `*.log`.
  - Number of existing lines to show from each file on open.  Default `0`
    (show nothing existing, follow only new lines).
  - Watch subfolders (recursive) on or off.
  - Auto-scroll on or off.
- Remembers open folders across runs.  Close the app, reopen it later, and it
  resumes monitoring the same folders, including window size and position.
- Pause / resume following, and clear the current view, without affecting the
  files on disk.

## Window controls

Control | Purpose
------- | -------
Folder… | Choose the folder of log files to watch.
Pattern | Glob pattern(s) selecting which files to tail.
Lines | Existing lines to show from each file when first seen (0 = only new).
Subfolders | Watch nested folders recursively.
Auto-scroll | Keep the newest line in view.
Apply | Apply pattern / option changes and restart tailing.
Pause / Resume | Stop or resume following.
Clear | Empty the current view (files are untouched).
New Window | Open another window for a different folder.
Forget | Close this window and stop reopening this folder on next run.

Closing a window with the title-bar **X** keeps the folder remembered for next
launch.  Use **Forget** to remove a folder from the remembered set.

## Settings

Settings are stored as JSON at:

```
%APPDATA%\TaylerLogTailer\settings.json
```

Each remembered window records its folder, glob pattern, initial-lines count,
recursive and auto-scroll flags, and window bounds.

## Limits and safety

- Up to 4096 matching files are followed per window.  If a folder contains more,
  a notice is shown and the remaining files are not tailed.
- Recursive watching does not follow directory junctions or symbolic links, so
  enumeration stays within the chosen folder tree.
- Per-file memory is bounded: reads are chunked and the buffer for a single
  not-yet-terminated line is capped, so a very large append or a file with no
  line breaks cannot exhaust memory.
- Non-fatal conditions (access denied, watcher unavailable, file limit reached)
  are surfaced in the window status bar rather than failing silently.
- New content is detected by reading through to the end of each file rather than
  trusting the reported file length, so tailing keeps working on network shares
  where the cached length can lag behind appended data.  If the file watcher is
  interrupted (for example an internal-buffer overflow on a busy folder), the
  periodic poll continues regardless and a notice is shown.

## Status bar

The status bar shows the watched folder, the current row count, the number of
matched files being followed, the follow state (Following / Paused / Starting),
the time the most recent new line arrived ("last new HH:mm:ss"), and the most
recent notice if any.  The "last new" time makes a stalled source easy to spot:
if files are still being written but the time stops advancing, the source or
share is not delivering new content.

## Build and run

Requires the .NET 10 SDK (Windows).

```
dotnet build Tayler.slnx -c Release
dotnet run --project src/TaylerLogTailer -c Release
```

The compiled executable is produced at
`src/TaylerLogTailer/bin/Release/net10.0-windows/TaylerLogTailer.exe`.

## Installer

A Windows installer is built with [Inno Setup 6](https://jrsoftware.org/isinfo.php).
The installer is per-machine (installs to Program Files, requires administrator
elevation), x64-only, and **self-contained**: the .NET 10 runtime is bundled, so
no separate runtime install is needed on the target machine.

With Inno Setup 6 installed, run the helper script from the repository root:

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

This publishes a self-contained x64 build to `publish\win-x64`, then compiles
`installer\TaylerLogTailer.iss` into a setup executable under `dist\`
(for example `dist\TaylerLogTailer-1.0.0.0-setup-x64.exe`).  Both `publish\` and
`dist\` are build artifacts and are git-ignored.

The setup offers an optional desktop shortcut (unchecked by default) and always
creates a Start Menu entry.  The installer version comes from the published
executable's file version, which is derived from git tags by MinVer (see
Versioning below).

## Versioning

The assembly and installer version is derived automatically from git tags by
[MinVer](https://github.com/adamralph/minver), configured in
`Directory.Build.props`.  Tags follow the format `v{Major}.{Minor}.{Patch}`:

- **Major** - set manually when tagging, bumped for breaking changes.
- **Minor** - auto-incremented by the Version Bump workflow on each `dev` to
  `main` merge.
- **Patch** - set to the merged pull request number by the Version Bump workflow.

The `.github/workflows/version-bump.yml` Forgejo Actions workflow runs when a PR
into `main` is merged: it finds the latest tag, computes the next version, and
pushes a new `v{Major}.{Minor}.{Patch}` tag.  Builds between tags get a
pre-release suffix (for example `1.1.7-alpha.0.5`).  Building from a source tree
without git history produces a `0.0.0` development version.

## Project layout

```
Tayler.slnx                          Solution
Directory.Build.props                NuGet audit / build policy; MinVer versioning
.github/
  workflows/
    version-bump.yml                 Auto-tags a new version on each dev->main merge
src/TaylerLogTailer/
  App.xaml(.cs)                      App startup; reopens remembered folders
  Assets/
    app.ico                          Application / window icon (gecko-tail motif)
    watermark.png                    Faint greyscale backdrop behind the log view
  Models/
    LogRow.cs                        A single displayed log line (file + text)
    FolderConfig.cs                  Per-window persisted settings
    AppSettings.cs                   Root settings (set of windows)
  Services/
    SettingsService.cs               Load/save settings JSON
    FileTailer.cs                    Byte-offset tailing of one file
    FolderTailer.cs                  FileSystemWatcher + polling over a folder
  Views/
    FolderWindow.xaml(.cs)           The compact folder window UI
installer/
  TaylerLogTailer.iss                Inno Setup script (per-machine, x64, self-contained)
  build-installer.ps1                Publishes the app then compiles the installer
```
