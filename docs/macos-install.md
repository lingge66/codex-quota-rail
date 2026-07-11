# macOS 安装说明

## 系统要求

- macOS 13 或更高版本；
- Intel 或 Apple Silicon Mac；
- 已安装并登录官方 Codex 桌面应用，或 PATH 中存在官方 `codex` 命令。

## 安装

1. 从同一个 GitHub Release 下载 `CodexQuotaRail-macOS-universal.app.zip` 与 `SHA256SUMS-macOS.txt`。
2. 在终端执行 `shasum -a 256 CodexQuotaRail-macOS-universal.app.zip`，与校验文件比较。
3. 解压 ZIP，把 `CodexQuotaRailMac.app` 移到 `/Applications`。
4. 首次打开时在 Finder 中右键应用并选择“打开”，确认运行未签名应用。
5. 按首次设置页说明授予“系统设置 → 隐私与安全性 → 辅助功能”权限。
6. 明确选择是否登录时自动启动。

不要为了运行本应用而全局关闭 Gatekeeper。首发 ZIP 使用 ad-hoc 签名，没有 Apple 公证；这是选择无付费证书开源构建的已知限制。

## 权限用途

辅助功能权限只用于读取 Codex 窗口的位置、大小、最小化、全屏和焦点状态。应用不申请屏幕录制，不读取窗口像素、聊天、项目、剪贴板或账号令牌。

如果移动或替换了 `.app`，macOS 可能要求重新授权。先把应用放到 `/Applications` 再授权可减少重复提示。

## 卸载

1. 从菜单栏退出应用。
2. 在应用设置中关闭“登录时启动”，或在系统设置的登录项中关闭。
3. 删除 `/Applications/CodexQuotaRailMac.app`。
4. 可选删除 `~/Library/Application Support/CodexQuotaRail/` 中的设置和脱敏日志。
