# 0.1.0-rc.1 本机验收记录

日期：2026-07-11  
系统：Windows 10.0.26100 x64  
Codex：Microsoft Store 包 `OpenAI.Codex` 26.707.3748.0  
候选程序：`artifacts/publish/win-x64/CodexQuotaRail.App.exe`，自包含单文件

## 结果

| 检查 | 结果 | 证据 |
|---|---|---|
| 官方 App Server 真实额度 | PASS | 未设置假服务器覆盖；UI Automation 找到额度百分比和“可用”状态，不含“正在连接/不可用” |
| 外侧贴边 | PASS | 轨道 2217×22，左右边界与 Codex 一致，底边等于 Codex 顶边 |
| 最大化/恢复 | PASS | 最大化 4px，恢复 22px |
| 最小化/恢复 | PASS | 最小化隐藏，恢复显示 |
| 单实例 | PASS | 第二实例退出码 0，运行实例数保持 1 |
| 冷启动 | PASS | 647ms 出现边缘轨 |
| 真实额度就绪 | PASS | 4,792ms 出现额度文本 |
| 日志隐私 | PASS | 当日 4 行日志，令牌、账号 ID、邮箱、聊天、项目路径模式命中 0 |
| 60 秒空闲 CPU | PASS | 0.203 CPU 秒 |
| 工作集 <80MB | FAIL | 156.2MB；静态 WPF 基线已超过 100MB |
| NSIS 安装/卸载 | BLOCKED | QA 机未安装 NSIS 3.12，未擅自安装新依赖 |
| 双物理显示器/睡眠/Explorer 重启 | NOT RUN | 自动化适配器测试通过，仍需发布候选安装包物理复验 |

## 隐私处理

验收没有写入真实可用额度、重置时间、邮箱、账号 ID、令牌或 App Server 原始 JSON。为避免把账号相关界面带入 Git 历史，本轮不保存真实账号截图；视觉状态截图由无账号的预览/假服务器流程覆盖。
