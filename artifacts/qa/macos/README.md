# macOS 验收记录

日期：2026-07-11

## 已完成证据

- GitHub Actions `macos-15` 使用 Apple Swift 6.1.2，`CodexQuotaKit` 27 项测试全部通过。
- arm64 与 x86_64 Release 均编译、链接成功，经 `lipo` 合并为 Universal Mach-O；脚本复核两个架构、Info.plist、ad-hoc 签名、ZIP 与 SHA-256。
- CI 启动真实 `.app` 的 `--rail-preview` 模式，进程持续存活且日志为空；`screencapture` 产出非空 PNG。
- 截图实测 22px 轨位于模拟 Codex 窗口标题栏外侧：不覆盖红绿灯或标题栏；“5 小时 / 本周”“可用 90% / 58%”中文、数字和进度均无截断。
- 详情预览实测显示两行额度、重置时间和更新时间；颜色与主轨一致。CI 最终只保留无系统权限提示污染的默认收起截图。
- Swift 6 严格并发、Accessibility Core Foundation 桥接、图标 ICNS、脚本可执行位与双架构打包问题均已由真实 runner 发现并回归通过。
- 同一提交的 Windows CI：Release 构建 0 警告、0 错误，203 项带覆盖率测试通过。

证据入口：

- macOS CI：`https://github.com/lingge66/codex-quota-rail/actions/workflows/macos-ci.yml`
- 开源仓库：`https://github.com/lingge66/codex-quota-rail`

## 仍需物理 Mac 复验

- 在用户自己的 Apple Silicon 与 Intel Mac 上首次打开、Gatekeeper 提示和辅助功能授权；
- 真实 Codex 窗口的移动、最小化、全屏、Space 切换和多显示器遮挡；
- 真实 Codex App Server 登录态与额度一致性；
- `SMAppService` 登录项在重启后的恢复行为。

Windows 环境和托管 runner 不能替代物理 Mac 上的 Gatekeeper、系统设置授权、Space 与真实 Codex 联调。以上限制不影响源码、双架构编译和预览渲染已验证的结论。
