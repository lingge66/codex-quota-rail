# 架构说明

## 数据流

`Codex App Server → AppServer 适配层 → Core 可用额度模型 → IQuotaRenderer → WPF 边缘轨`

- `CodexQuotaRail.AppServer` 发现本机 Codex 可执行文件，以 stdio JSONL 连接官方 App Server，读取 `account/rateLimits/read` 并处理推送。
- `CodexQuotaRail.Core` 将 `usedPercent` 转为 `availablePercent = clamp(100 - usedPercent, 0, 100)`，形成不依赖 UI 的领域模型。
- `CodexQuotaRail.Windows` 使用 WinEvent 与 Win32 查询识别 Codex 主窗口，计算外侧 22px 或内部 4px 的物理像素位置。
- `CodexQuotaRail.App` 组合生命周期、托盘、设置、日志、无障碍和 `RailQuotaRenderer`。

## 安全与隐私边界

App Server 子进程只通过标准输入输出通信。本程序不监听端口、不注入 Codex、不修改 Codex 安装、不读取网页、聊天、项目、剪贴板或凭据。日志只记录事件名、状态和已脱敏的错误类型。

## 稳定性

- App Server 断开后保留最后一次有效值并按 2、5、15、30、60 秒退避重试。
- 网络恢复和系统唤醒立即重置等待并完整刷新。
- Explorer 重启后重建托盘图标。
- 窗口事件按已知 Codex 句柄过滤，避免全桌面位置事件触发高 CPU 扫描。

## 扩展

额度展示依赖 `IQuotaRenderer`。后续可以新增 `PetQuotaRenderer`，复用同一额度源、窗口跟踪和设置系统，不需要改变数据协议。
