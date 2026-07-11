# 发布检查表

## 自动化验证

- [x] `dotnet test --configuration Release --collect:"XPlat Code Coverage"`：197 项通过，4 份 Cobertura 文件已生成。
- [x] `dotnet build --configuration Release --no-restore`：0 警告，0 错误。
- [x] 更新检查测试覆盖固定仓库地址、User-Agent、预发布过滤和超时。
- [ ] NSIS 3.12 本机安装/卸载验证：当前 QA 机器未安装该依赖。

## Windows 行为

- [x] 22px/4px 放置、移动、缩放、最小化与失焦透明度由单元测试和完整应用预览覆盖。
- [x] 睡眠/恢复、网络恢复、Explorer 托盘重建与 DPI 去重有自动化测试。
- [x] UI Automation 名称、非激活窗口、200% 弹层布局与减少动画有自动化测试。
- [ ] 两台不同 DPI 的物理显示器、真实睡眠与 Explorer 重启需在发布候选安装包上复验。

## 性能（2026-07-11，Windows 10.0.26100 x64）

| 指标 | 实测 | 门槛 | 结果 |
|---|---:|---:|---|
| 60 秒空闲 CPU | 0.203 秒 | ≤ 0.3 秒 | PASS |
| 工作集 | 156.2 MB | < 80 MB | FAIL |
| 私有内存 | 85.8 MB | 记录项 | INFO |

CPU 根因是全局 WinEvent 位置事件触发全桌面身份扫描；按已知 Codex 句柄过滤，并在文件名确认是 `Codex.exe` 后才执行 Authenticode 验证，修复前同场景 CPU 为 41.031 秒。静态 WPF 预览工作集基线已超过 100 MB，因此 80 MB 工作集门槛在当前 WPF 架构下不可达；发布时必须保留该已知限制，不能把私有内存替代为通过项。

## 隐私与发布

- [x] 零遥测；无 UI 抓取、OCR、模拟点击或额度估算。
- [x] 日志不记录令牌、聊天、项目内容、邮箱或账号 ID。
- [x] 手动更新检查只访问固定 GitHub Release API，打开浏览器前再次确认。
- [x] 便携 ZIP（88,871,856 字节）、SHA-256 与 SPDX SBOM 已由 `build-release.ps1 -SkipInstaller` 生成并复核。
- [x] 自包含发布目录中的 `CodexQuotaRail.App.exe` 已在无 .NET 运行时依赖模式下启动，并成功拉起假 App Server 完成冒烟验证。
- [ ] Setup EXE 等待本机取得 NSIS 3.12 安装许可后编译；CI 已固定 3.12 并会生成安装器。
- [ ] 未签名构建名称和 Release 说明明确标出未知发布者风险。
