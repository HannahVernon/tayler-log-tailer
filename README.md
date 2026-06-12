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

## Build and run

Requires the .NET 10 SDK (Windows).

```
dotnet build Tayler.slnx -c Release
dotnet run --project src/TaylerLogTailer -c Release
```

The compiled executable is produced at
`src/TaylerLogTailer/bin/Release/net10.0-windows/TaylerLogTailer.exe`.

## Project layout

```
Tayler.slnx                          Solution
Directory.Build.props                NuGet audit / build policy
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
```
