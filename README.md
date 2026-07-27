# CDP Switcher

[![Build](https://github.com/dz-sh/cdp-switcher/actions/workflows/build.yml/badge.svg)](https://github.com/dz-sh/cdp-switcher/actions/workflows/build.yml)

CDP Switcher is a Windows app for switching isolated Chrome profiles behind
one stable local Chrome DevTools Protocol endpoint:

```text
127.0.0.1:9222
```

Each profile keeps its own browser data and sign-in state. CDP Switcher starts
a visible Chrome window so you can sign in and browse normally before using
the profile through CDP.

> [!NOTE]
> CDP Switcher is pre-release software.

## Download

Download the latest version from
[GitHub Releases](https://github.com/dz-sh/cdp-switcher/releases):

- `CdpSwitcher.exe` for a single-file app; or
- `CdpSwitcher-win-x64.zip` for the complete app directory.

The app requires Windows 10 version 1809 or later and Google Chrome. The
downloads are self-contained and do not require a separate .NET installation.

## Use

1. Add a profile.
2. Select **Activate** and confirm.
3. Sign in through the Chrome window that opens.
4. Connect your CDP client to `127.0.0.1:9222`.

Use **Stop** before activating another profile. Removing a profile keeps its
browser data unless you explicitly choose permanent deletion.

## Security

The CDP endpoint listens only on Windows loopback. A connected CDP client can
fully control the active profile, so activate only the profile you intend to
expose and stop it when finished.

## Build

Building from source requires the .NET 10 SDK on Windows:

```powershell
dotnet restore CdpSwitcher.slnx -p:Configuration=Release -p:Platform=x64
dotnet build CdpSwitcher.slnx -c Release --no-restore -p:Platform=x64
dotnet test tests/CdpSwitcher.Core.Tests/CdpSwitcher.Core.Tests.csproj `
  -c Release --no-build
```
