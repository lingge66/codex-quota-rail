# 参与贡献

感谢参与。提交前请先创建 Issue 说明行为变化，尤其是额度协议、窗口识别、安装、自启和隐私相关改动。

## 开发流程

1. Windows 开发使用 Windows x64 与 .NET 8 SDK；macOS 开发使用 macOS 13+ 与 Xcode 16 命令行工具。
2. 从 `main` 创建短生命周期分支。
3. 先写能复现问题或描述新行为的测试，再做最小实现。
4. UI 文案优先使用中文；不要引入遥测、网页抓取、OCR 或账号令牌读取。
5. 运行完整验证：

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

前端或窗口行为改动还需执行 `scripts/manual-qa.ps1`，并在 PR 中记录 Windows 版本、DPI 和实际 CPU/内存指标。

macOS 改动还需运行：

```bash
swift test --package-path macos/Packages/CodexQuotaKit
swift build --package-path macos --configuration debug
bash macos/Scripts/build-universal.sh 0.1.0-dev
bash macos/Scripts/verify-app.sh artifacts/macos/CodexQuotaRailMac.app
```

窗口行为改动必须在真实 Codex.app 上记录辅助功能授权、普通/全屏/最小化、Space、焦点、窗口覆盖和菜单栏行为。不要提交包含真实账号或额度值的截图。

## 提交与 PR

- 一个提交只解决一个清晰问题。
- PR 说明应包含：做了什么、为什么、修改文件、验证方式、已知风险。
- 不提交日志、账号数据、截图中的邮箱/账号 ID、构建目录或私钥。
