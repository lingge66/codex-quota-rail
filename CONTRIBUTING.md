# 参与贡献

感谢参与。提交前请先创建 Issue 说明行为变化，尤其是额度协议、窗口识别、安装、自启和隐私相关改动。

## 开发流程

1. 使用 Windows x64 与 .NET 8 SDK。
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

## 提交与 PR

- 一个提交只解决一个清晰问题。
- PR 说明应包含：做了什么、为什么、修改文件、验证方式、已知风险。
- 不提交日志、账号数据、截图中的邮箱/账号 ID、构建目录或私钥。
