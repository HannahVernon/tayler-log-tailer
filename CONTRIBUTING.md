# Contributing to Tayler Log Tailer

Thanks for your interest in improving Tayler Log Tailer.

## Prerequisites

- Windows
- .NET 10 SDK
- An editor with WPF/XAML support (Visual Studio 2022+ or Rider recommended)

## Branch model

- `main` - release branch.  Protected.
- `dev` - integration branch.  Protected.
- Feature and fix work happens on `feature/xxx` or `fix/xxx` branches created
  off `dev`.

Open pull requests **into `dev`**.  `dev` is merged into `main` for releases.

```
git switch dev
git switch -c feature/my-change
# ...work...
git push -u origin feature/my-change
# open a PR targeting dev
```

Use `git switch` / `git restore` rather than `git checkout`.

## Coding standards

- Nullable reference types and implicit usings are enabled; keep the build
  warning-free.  NuGet audit is enforced (`Directory.Build.props`).
- Indentation and line endings follow `.editorconfig` and `.gitattributes`
  (4-space indent for C#, 2-space for XAML/project files).
- Keep changes focused: one logical change per PR.
- Comment only where the intent is not obvious from the code.

## Building and testing

```
dotnet build Tayler.slnx -c Release
```

The build must produce 0 errors and 0 warnings.  Manually verify tailing
behavior against a folder of sample log files before opening a PR.

## Adding dependencies

New dependencies must be permissively licensed (MIT/Apache-2.0/BSD),
security-vetted, and recorded in `THIRD-PARTY-NOTICES.md`.

## Pull request expectations

Fill in the pull request template completely, describe how you tested the
change, and update documentation when user-visible behavior changes.
