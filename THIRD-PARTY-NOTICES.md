# Third-Party Notices

Tayler Log Tailer is built on the .NET 10 platform and the Windows Presentation
Foundation (WPF), both supplied by the .NET SDK / runtime under the MIT License
(Copyright (c) .NET Foundation and Contributors).

The application has **no additional third-party NuGet dependencies** beyond the
Microsoft.NET.Sdk and the WPF components included with the Windows .NET runtime.

If a third-party dependency is added in the future, record it here with:

Package | Version | Copyright holder | License
------- | ------- | ---------------- | -------
(none)  |         |                  |

## Build-time tooling

The Windows installer is produced with **Inno Setup 6** (Copyright (c) 1997-2026
Jordan Russell and Martijn Laan), used under the Inno Setup License (a permissive
modified-BSD-style license).  Inno Setup is a build-time tool only; none of its
code is redistributed inside the application itself.  The generated setup
executable includes the standard Inno Setup installer stub, as is normal for any
Inno-built installer.
