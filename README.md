# Codex Quota Rail

Codex Quota Rail is a native Windows and macOS utility that attaches a non-activating quota rail to the outside top edge of the Codex desktop window. It shows **available** quota: 100% is green, then the rail transitions through yellow to red as quota runs out.

> This is an independent open-source project. It is not affiliated with or endorsed by OpenAI.

## Highlights

- Reads quota only through the official local Codex App Server protocol.
- Never scrapes the UI, clicks the Usage page, estimates quota, or reads chats.
- 22 px dual-window rail, with a 4 px compact mode when Codex is maximized.
- Stays visible at 52% opacity when Codex loses focus and becomes clear again on focus.
- Follows move, resize, minimize, restore, DPI changes, sleep/resume, network recovery, and Explorer restart.
- Tray controls for refresh, follow/pause, theme, reduced motion, autostart, logs, and manual update checks.
- LingGe branding is used consistently for the executable, tray, and installer icons.
- Zero telemetry. Update checks only run after the user clicks **检查更新**.
- The tray menu includes a fixed HTTPS shortcut to the LingGe personal website.
- The macOS app uses native SwiftUI and AppKit, with a menu bar item, Accessibility-based window tracking, and a Universal Intel/Apple Silicon build.

Supported platforms are Windows x64 and macOS 13 or later. A future renderer can add a Codex pet without changing the quota source.

[Download the Windows and macOS prerelease](https://github.com/lingge66/codex-quota-rail/releases/tag/v0.1.0-rc.5). Current builds are not commercially code-signed; verify the bundled SHA-256 first.

## Run from source

### Windows

Requirements: Windows 10 2004 or later and .NET 8 SDK.

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet run --project src/CodexQuotaRail.App --configuration Release
```

### macOS

Requirements: macOS 13 or later and Xcode 16 command-line tools.

```bash
swift test --package-path macos/Packages/CodexQuotaKit
swift build --package-path macos --configuration debug
bash macos/Scripts/build-universal.sh 0.1.0
```

See the [macOS install guide](docs/macos-install.md) and [fork customization guide](docs/macos-customization.md).

## Verify a release

Download the ZIP or installer, `SHA256SUMS.txt`, and `CodexQuotaRail.spdx.json` from the same GitHub Release. Then run:

```powershell
Get-FileHash .\CodexQuotaRail-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Until code signing is configured, Windows may show an unknown publisher warning. Verify the hash before running an unsigned build.

See [中文说明](README.zh-CN.md), [privacy](docs/privacy.md), [architecture](docs/architecture.md), and [troubleshooting](docs/troubleshooting.md).

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
