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

## 0.1.0-rc.3 补充验收

日期：2026-07-11

- 便携 ZIP 已在合入详情收回、owner 附着、实时主题和领哥品牌图标后重新生成，大小 88,934,886 字节，SHA-256 为 `f3b1f49a77c82de6e5fd55195c83ca0de78cea837eca3dc44b186d2ba0ca6bde`。
- Release 全量 202 项测试通过，构建 0 警告、0 错误；覆盖托盘全部 11 个点击入口、原生鼠标消息和主题实时换肤。
- ZIP 内 EXE 的产品版本为 `0.1.0-rc.3`，可反向提取领哥 LOGO；源 ICO 的九档尺寸均可由 Windows 解码。
- 便携 EXE 在真实 `OpenAI.Codex` 包的 `ChatGPT.exe` 宿主上显示 2217×22 外侧轨，轨道 owner 等于 Codex HWND。
- 真实鼠标点击 22px 轨会创建详情弹层；指针移出或 Codex 失焦后弹层收回，仅保留轨道。
- 点击前后 foreground HWND 均保持为 Codex；详情 Popup 实测 280×139，移出 700ms 后关闭。
- 托盘选择浅色/深色后现有轨道立即从 `#ECECEA` 切换为 `#10100E`，无需重启。
- QA 启动不得使用 `Start-Process -WindowStyle Hidden`，该参数会按 Windows 语义强制隐藏 WPF 轨道；用户正常双击与开机自启不受影响。
- 安装器图标资源已接入 NSIS 脚本；本机仍因未安装锁定版本 NSIS 3.12 而无法编译 Setup EXE。

## 0.1.0-rc.4 补充验收

日期：2026-07-11

- 托盘新增“领哥个人网站”，固定打开 `https://lingge66.pages.dev/`，不接收用户输入的协议或网址。
- Release 全量 203 项测试通过；覆盖全部 12 个托盘点击入口及网站动作的精确 HTTPS 地址。
- 真实 Windows 托盘视觉检查通过：菜单项位于“故障排查”和“退出”之间，中文无截断，证据见 `rc4-tray-menu.png`。
- 实际点击后系统默认浏览器前台标题为“领哥 | 超级个体官网 - Google Chrome”，地址栏为 `lingge66.pages.dev`。
- 便携 ZIP 大小 88,937,314 字节，SHA-256 为 `e69b2ace82420181de67234b054d8ad3e32d4cf117dddcc686f40f9f7fdf5220`。
