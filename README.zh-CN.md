# Codex 可用额度边缘轨

这是一个 Windows 与 macOS 常驻小工具：它把一条不会抢焦点的额度轨贴在 Codex 桌面窗口顶部外侧，直接显示**可用额度**。100% 为绿色，额度降低时逐渐变黄，接近耗尽时变红。

> 本项目为独立开源项目，并非 OpenAI 官方产品，也未获得 OpenAI 背书。

## 主要能力

- 额度只读取本机官方 Codex App Server，不抓网页、不 OCR、不模拟点击、不本地估算。
- 普通窗口显示 22px 双额度轨；Codex 最大化或外侧空间不足时自动切换 4px 紧凑轨。
- Codex 失焦后继续显示但降至 52% 透明度，切回 Codex 后恢复清晰。
- 跟随移动、缩放、最小化、恢复、跨屏 DPI、睡眠恢复、网络恢复和 Explorer 重启。
- 托盘可立即刷新、暂停跟随、切换主题、减少动画、关闭开机自启、查看日志、手动检查更新，并可打开领哥个人网站。
- 应用文件、托盘和安装器统一使用领哥 LOGO。
- 零遥测；只有用户点击“检查更新”后才访问 GitHub API。
- 渲染接口已为后续“Codex 宠物显示额度”预留扩展点。
- macOS 版采用原生 SwiftUI + AppKit，支持 Intel 与 Apple Silicon Universal 应用、菜单栏、辅助功能窗口跟踪和系统登录项。

支持 Windows x64，以及 macOS 13 或更高版本。

[下载 Windows 与 macOS 预发布包](https://github.com/lingge66/codex-quota-rail/releases/tag/v0.1.0-rc.6)。当前构建未使用商业代码签名，请先核对随包 SHA-256。

## 从源码运行

### Windows

需要 Windows 10 2004 或更高版本、.NET 8 SDK，并已登录 Codex 桌面应用。

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet run --project src/CodexQuotaRail.App --configuration Release
```

### macOS

需要 macOS 13 或更高版本、Xcode 16 命令行工具，并已登录 Codex 桌面应用。

```bash
swift test --package-path macos/Packages/CodexQuotaKit
swift build --package-path macos --configuration debug
bash macos/Scripts/build-universal.sh 0.1.0
```

产物位于 `artifacts/macos/CodexQuotaRail-macOS-universal.app.zip`。安装、首次打开和辅助功能授权见 [macOS 安装说明](docs/macos-install.md)。Fork 换名、换 LOGO、换网站和制作主题见 [macOS 自定义搭建](docs/macos-customization.md)。

## 验证发布文件

请从同一个 GitHub Release 下载程序、`SHA256SUMS.txt` 和 `CodexQuotaRail.spdx.json`，再核对：

```powershell
Get-FileHash .\CodexQuotaRail-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

配置代码签名之前，Windows 可能提示“未知发布者”。运行未签名版本前请先核对 SHA-256。

更多信息见[隐私说明](docs/privacy.md)、[架构说明](docs/architecture.md)、[故障排查](docs/troubleshooting.md)和[发布检查表](docs/release-checklist.md)。

## 许可证

MIT，详见 [LICENSE](LICENSE) 与 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
