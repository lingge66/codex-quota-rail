# 发布检查表

## 自动化验证

- [x] `dotnet test --configuration Release --collect:"XPlat Code Coverage"`：197 项通过，4 份 Cobertura 文件已生成。
- [x] `dotnet build --configuration Release --no-restore`：0 警告，0 错误。
- [x] 更新检查测试覆盖固定仓库地址、User-Agent、预发布过滤和超时。
- [ ] NSIS 3.12 本机安装/卸载验证：当前 QA 机器未安装该依赖。

## Windows 行为

- [x] 真实 Codex 窗口外侧轨高度 22px，左右边界与 Codex 一致，轨道底边与窗口顶边精确相接。
- [x] 真实 Codex 最大化后切为 4px，恢复后回到 22px；最小化时隐藏，恢复时重新显示。
- [x] 第二实例在 5 秒内以退出码 0 结束，系统仅保留一个主实例。
- [x] 失焦 52% 与恢复 100% 由放置计算、窗口前景事件和动画测试覆盖；WPF 每像素透明窗口不暴露可供外部读取的固定 Alpha 值。
- [x] 睡眠/恢复、网络恢复、Explorer 托盘重建与 DPI 去重有自动化测试。
- [x] UI Automation 名称、非激活窗口、200% 弹层布局与减少动画有自动化测试。
- [ ] 两台不同 DPI 的物理显示器、真实睡眠与 Explorer 重启需在发布候选安装包上复验。

## 性能（2026-07-11，Windows 10.0.26100 x64）

| 指标 | 实测 | 门槛 | 结果 |
|---|---:|---:|---|
| 60 秒空闲 CPU | 0.203 秒 | ≤ 0.3 秒 | PASS |
| 工作集 | 156.2 MB | < 80 MB | FAIL |
| 私有内存 | 85.8 MB | 记录项 | INFO |
| 自包含候选版边缘轨可见 | 647 ms | < 2 秒 | PASS |
| 真实额度显示就绪 | 4,792 ms | 记录项 | INFO |

CPU 根因是全局 WinEvent 位置事件触发全桌面身份扫描；按已知 Codex 句柄过滤，并在文件名确认是 `Codex.exe` 后才执行 Authenticode 验证，修复前同场景 CPU 为 41.031 秒。静态 WPF 预览工作集基线已超过 100 MB，因此 80 MB 工作集门槛在当前 WPF 架构下不可达；发布时必须保留该已知限制，不能把私有内存替代为通过项。

## 隐私与发布

- [x] 零遥测；无 UI 抓取、OCR、模拟点击或额度估算。
- [x] 日志不记录令牌、聊天、项目内容、邮箱或账号 ID。
- [x] 真实 Codex 26.707.3748.0 验收中，UI Automation 确认存在额度百分比与“可用”文案，且没有“正在连接/不可用”；未记录实际额度值。
- [x] 当日日志共 4 行，敏感模式扫描命中 0。
- [x] 手动更新检查只访问固定 GitHub Release API，打开浏览器前再次确认。
- [x] 便携 ZIP（88,871,856 字节）、SHA-256 与 SPDX SBOM 已由 `build-release.ps1 -SkipInstaller` 生成并复核。
- [x] 自包含发布目录中的 `CodexQuotaRail.App.exe` 已在无 .NET 运行时依赖模式下启动，并成功拉起假 App Server 完成冒烟验证。
- [ ] Setup EXE 等待本机取得 NSIS 3.12 安装许可后编译；CI 已固定 3.12 并会生成安装器。
- [ ] 未签名构建名称和 Release 说明明确标出未知发布者风险。
