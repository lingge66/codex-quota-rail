# Codex Quota Rail

Codex Quota Rail is a small Windows utility that attaches a non-activating quota rail to the outside top edge of the Codex desktop window. It shows **available** quota: 100% is green, then the rail transitions through yellow to red as quota runs out.

> This is an independent open-source project. It is not affiliated with or endorsed by OpenAI.

## Highlights

- Reads quota only through the official local Codex App Server protocol.
- Never scrapes the UI, clicks the Usage page, estimates quota, or reads chats.
- 22 px dual-window rail, with a 4 px compact mode when Codex is maximized.
- Stays visible at 52% opacity when Codex loses focus and becomes clear again on focus.
- Follows move, resize, minimize, restore, DPI changes, sleep/resume, network recovery, and Explorer restart.
- Tray controls for refresh, follow/pause, theme, reduced motion, autostart, logs, and manual update checks.
- Zero telemetry. Update checks only run after the user clicks **检查更新**.

The first release supports Windows x64 only. A future renderer can add a Codex pet without changing the quota source.

## Run from source

Requirements: Windows 10 2004 or later and .NET 8 SDK.

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet run --project src/CodexQuotaRail.App --configuration Release
```

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
