# Codex Quota Rail macOS 原生版设计规格

- 状态：用户已确认
- 日期：2026-07-11
- 平台：macOS 13 及以上，Apple Silicon 与 Intel
- 技术路线：SwiftUI + AppKit
- 首发产物：未签名 Universal `.app.zip`

## 1. 结论

新增独立的 macOS 原生应用，不尝试运行或包装 Windows EXE。应用使用 SwiftUI 管理状态与设置界面，使用 AppKit 实现菜单栏、不可激活的透明边缘轨和窗口层级，使用 macOS Accessibility API 跟踪 Codex 窗口。额度仍来自本机官方 Codex App Server，不抓取屏幕、不读取聊天、不保存账号令牌。

仓库整体继续采用 MIT 许可证。macOS 源码、测试、构建脚本、默认主题、领哥品牌资源、CI 和文档全部纳入仓库；除明确列入第三方声明的内容外，均允许 Fork、修改、重新构建和分发。

## 2. 产品目标

- 保持 Windows 版已经确认的核心体验：22px 外侧额度轨、全屏 4px 紧凑轨、可用额度语义、失焦透明、点击详情和自动收回。
- 使用 macOS 原生菜单栏和系统设置能力，避免 Electron、WebView 和额外常驻运行时。
- 同一源码生成 `arm64` 与 `x86_64` Universal 应用。
- 用户可在运行时调整视觉和行为；开发者可在构建时替换品牌、网站、目标应用和默认配置。
- Fork 后无需私有服务或付费依赖，即可在 GitHub Actions 生成自己的 `.app.zip`。

## 3. 非目标

- 首发不提供签名、公证或 DMG；后续取得 Apple Developer 证书后再增加可选发布任务。
- 不修改、注入或重新打包 Codex.app。
- 不使用 OCR、屏幕截图、UI 文案抓取或模拟点击读取额度。
- 不提供动态加载任意二进制插件的机制，避免供应链与代码执行风险。
- 首发不实现宠物皮肤，只提供稳定的渲染协议和主题扩展点。
- 不把 Windows WPF/Win32 代码强行移植到 macOS，也不要求用户安装 .NET。

## 4. 用户体验

### 4.1 首次启动

1. README 指导用户把应用移动到 `/Applications`，然后通过 Finder 右键“打开”确认未签名应用。
2. 应用以菜单栏模式启动，不显示 Dock 图标。
3. 首次运行说明只需要“辅助功能”权限来读取 Codex 窗口位置、大小、最小化和焦点状态；不请求屏幕录制权限。
4. 用户可点击按钮打开系统辅助功能设置。授权后应用自动重新检测 Codex，无需重启。
5. 首次明确询问是否登录时启动，默认开启；用户可跳过并随时从菜单关闭。

### 4.2 边缘轨

- Codex 普通窗口：顶部外侧显示 22px 额度轨，宽度与窗口一致。
- Codex 全屏：切换为窗口顶部内侧 4px 紧凑轨，不显示详情和文字。
- Codex 最小化或退出：隐藏边缘轨，菜单栏仍保留。
- Codex 前台：100% 不透明度；失焦但可见：180ms 内降到 52%。
- 轨道只表达“可用额度”：100% 绿色满轨，额度降低时缩短并经过黄橙色，20% 以下变红。
- 22px 轨点击打开详情；点击外部、指针移出、Codex 移动、失焦或进入紧凑模式时自动收回。
- 面板不成为 Key Window，不改变 Codex 键盘焦点，也不出现在窗口切换器。
- 面板按 Codex 窗口号相对排序，不使用跨应用永久置顶；其他应用覆盖 Codex 时也覆盖边缘轨。

### 4.3 菜单栏

菜单栏状态项显示领哥图标和最低可用额度摘要。菜单包含：

- 当前连接状态与最近更新时间；
- 立即刷新；
- 暂停或恢复窗口跟随；
- 自动、深色、浅色主题；
- 减少动画；
- 登录时启动；
- 打开日志目录；
- 故障排查；
- 领哥个人网站；
- 检查更新；
- 退出。

“领哥个人网站”默认打开 `https://lingge66.pages.dev/`。运行时配置只接受绝对 HTTPS 地址。

## 5. 技术架构

### 5.1 仓库结构

```text
macos/
  Package.swift
  App/
  Config/
    Branding.xcconfig
    Defaults.json
  Resources/
    Assets.xcassets
    Themes/
  Packages/
    CodexQuotaKit/
      Package.swift
      Sources/
      Tests/
  Scripts/
    build-universal.sh
    verify-app.sh
```

根 Swift Package 负责原生 AppKit/SwiftUI 可执行 Target，本地 Package `CodexQuotaKit` 承载可复用逻辑。发布脚本把 SwiftPM 产物组装成标准 `.app`，避免提交易冲突的工程生成文件，也不引入运行时或项目生成器依赖。

### 5.2 组件边界

#### `MacCodexLocator`

- 通过 `NSWorkspace` 监听官方 Codex 启动、退出与激活。
- 默认目标 Bundle Identifier 列表来自 `Defaults.json`，可在设置中扩展。
- App Server 可执行文件发现顺序：显式开发者覆盖路径、当前 `PATH` 中经过规范化的 `codex`、运行中 Codex.app 包内的已知可执行候选。
- 不通过 Shell 拼接命令；只向 `Process.executableURL` 传入规范化的本地可执行路径和独立参数。
- 候选必须存在、可执行且不位于可被当前配置拒绝的目录；失败时显示明确的安装或更新指引。

#### `AccessibilityWindowTracker`

- 使用 `AXIsProcessTrustedWithOptions` 检查或请求辅助功能授权。
- 对 Codex 进程建立 `AXObserver`，监听主窗口、移动、缩放、最小化、焦点和销毁通知。
- 使用 PID、AX 窗口边界和 `CGWindowList` 元数据关联窗口号，不读取窗口像素。
- 多窗口时跟随最近激活的标准主窗口；事件丢失时使用低频校准，不进行高频轮询。
- 对外发布不可变 `TrackedMacWindowSnapshot`，不把 AX 对象暴露给渲染层。

#### `RailPanelController`

- 使用不可激活、透明、无标题栏的 `NSPanel`。
- `canBecomeKey` 与 `canBecomeMain` 始终为 `false`，集合行为包含全屏辅助与忽略窗口循环。
- 根据窗口快照计算普通、全屏、隐藏三种放置模式。
- 使用相对窗口号排序保持与 Codex 的 Z 序关系；无法证明相对排序安全时隐藏面板，不退化为全局置顶。
- SwiftUI `RailView` 只接收领域状态和设计令牌，不接触 Accessibility 或进程对象。

#### `CodexAppServerClient`

- 使用 `Process`、stdin 和 stdout 建立 JSONL JSON-RPC 连接，启动参数为 `app-server --listen stdio://`。
- 初始化后调用 `account/read` 和 `account/rateLimits/read`，监听 `account/rateLimits/updated`。
- 使用 Swift `actor` 串行管理请求 ID、挂起请求、读取循环、关闭和重连。
- 只解析额度所需字段，忽略未知字段；原始响应不写日志。
- 断线后保留最后有效值，并按 2、5、15、30、60 秒退避重连。

#### `QuotaDomain`

- `availablePercent = clamp(100 - usedPercent, 0, 100)`。
- 缺字段不伪造 100%；无限额度显示“无限”；次额度缺失时显示单轨。
- 与 Windows 版共用相同 JSON 契约测试夹具，保证跨平台展示语义一致。

#### `MenuBarController`

- 使用 `NSStatusItem` 与 `NSMenu`，动态更新状态、选中项和可用性。
- 菜单动作通过明确的命令枚举分发，所有可点击入口都有测试。
- 外部网址启动前执行 HTTPS 校验，使用 `NSWorkspace.open` 交给默认浏览器。

#### `SettingsStore` 与 `LaunchAtLoginService`

- 使用版本化 Codable 设置模型，持久化到 `~/Library/Application Support/CodexQuotaRail/`。
- 损坏配置隔离备份并恢复安全默认值，不覆盖用户主题文件。
- 使用 `SMAppService.mainApp` 注册或取消登录项，并根据系统状态显示“已启用、等待批准、未启用”。

## 6. 开源与自定义

### 6.1 运行时设置

普通用户无需重新编译即可调整：

- 自动、深色、浅色和自定义主题；
- 前台与失焦透明度；
- 普通轨高度、紧凑轨高度和圆角；
- 额度阈值颜色与减少动画；
- 登录时启动；
- 个人网站 HTTPS 地址；
- 允许跟随的 Codex Bundle Identifier。

配置使用带 `schemaVersion` 的 JSON。所有数值有安全范围，颜色和网址无效时回退到默认值并给出诊断。

### 6.2 构建时品牌

`Branding.xcconfig` 集中定义：

- 产品名称；
- Bundle Identifier；
- 默认网站；
- 更新仓库地址；
- 默认目标应用标识；
- 版本与构建号。

LOGO、菜单栏模板图标和应用图标位于 `Assets.xcassets`。文档给出替换尺寸、模板渲染和许可要求。Fork 用户不需要修改 Swift 源码即可完成换名、换图标和换网站。

### 6.3 源码扩展点

- `QuotaDataProviding`：额度数据来源协议；
- `QuotaRendering`：额度视觉渲染协议；
- `TargetWindowTracking`：目标窗口跟踪协议；
- `ThemeLoading`：主题加载协议。

默认实现只加载仓库内编译的 Swift 类型与数据主题，不从用户目录动态执行代码。宠物皮肤可作为新的仓库内渲染 Target 或社区 Fork 实现。

### 6.4 可复现构建

- `macos/Scripts/build-universal.sh` 使用 Xcode 附带的 Swift 工具链分别构建两种架构并生成 Universal `.app`。
- 构建脚本验证 Mach-O 同时包含 `arm64` 与 `x86_64`，然后执行 ad-hoc codesign。
- 使用 `ditto` 生成保留资源分叉与权限的 `.app.zip`。
- 输出 SHA-256、版本信息和第三方声明。
- GitHub Actions Fork 后无需仓库 Secret 即可运行未签名构建。

## 7. 隐私与安全

- 仅申请辅助功能权限，不申请屏幕录制、通讯录、相册、麦克风或摄像头权限。
- 不启用 App Sandbox，因为应用需要启动本地 Codex App Server 和观察另一个应用窗口；该选择必须在 README 中解释。
- 不读取聊天、项目文件、剪贴板、账号 ID 或令牌。
- 默认零遥测；日志只记录状态、事件名、错误类别和脱敏路径。
- 不监听 TCP 端口；App Server 只走子进程标准输入输出。
- 环境覆盖路径只用于开发者配置，解析符号链接后校验，不通过 Shell 执行。
- 自定义 URL 仅允许 HTTPS；更新检查只在用户主动点击时访问固定仓库 API。
- 主题 JSON 仅解析数据，不允许脚本、表达式或远程资源。

## 8. 异常与恢复

- 未安装或未启动 Codex：边缘轨隐藏，菜单显示“等待 Codex”。
- 未授权辅助功能：不反复弹窗，菜单提供“打开系统设置”。
- 未登录：提示先在 Codex 中登录，本应用不提供账号输入。
- App Server 不支持：显示版本不兼容并提供更新指引。
- 进程断开：保留最近有效额度并显示更新时间，按退避策略重连。
- 系统睡眠：暂停刷新；唤醒与网络恢复后立即重建连接。
- Codex 窗口切换 Space、全屏或退出：关闭详情并重新计算面板位置。
- 配置损坏：保存损坏副本，加载安全默认值并记录非敏感错误。
- 重复启动：把命令转发给已有实例后退出，不创建第二个状态项或边缘轨。

## 9. 测试策略

### 9.1 Swift 单元测试

- 可用额度换算、颜色阈值、无限与缺字段；
- JSON-RPC 编解码、未知字段、错误响应和通知；
- 退避、刷新合并、关闭与进程退出；
- 窗口放置、全屏、最小化、多屏负坐标；
- 设置迁移、无效主题、HTTPS 校验和品牌默认值；
- 菜单全部命令的分发。

### 9.2 集成测试

- Fake App Server 覆盖正常、单额度、无限、未登录、断线、无效 JSON 和协议不支持。
- Fake Accessibility Backend 覆盖移动、缩放、焦点、全屏、Space 和权限撤销。
- `--rail-preview` 使用固定假数据运行 UI，不要求真实账号。
- `--rail-preview` 与 macOS 自动化脚本验证菜单、设置和权限说明文案。

### 9.3 CI

- GitHub Actions `macos-15` runner 执行 Swift Package 测试、原生可执行 Target 编译和预览冒烟。
- Debug 与 Release 均启用严格并发检查和警告即错误。
- Release 构建验证 Universal 架构、Info.plist、资源、ad-hoc 签名和 ZIP 可解包。
- 上传 `.app.zip`、SHA-256、测试结果和构建日志摘要。

### 9.4 真机验收门槛

发布候选版必须在真实 macOS 与官方 Codex 上观察：

- 辅助功能授权流程可恢复；
- 普通窗口显示 22px 外侧轨并平滑跟随；
- 全屏切换 4px，退出全屏恢复 22px；
- 最小化、Space 切换和退出同步隐藏；
- 其他应用覆盖 Codex 时不会被边缘轨压住；
- 点击详情不抢走 Codex 键盘焦点并自动收回；
- 真实额度与 Codex 使用量一致；
- 登录项启停在系统设置中可见；
- Intel 与 Apple Silicon 至少各完成一次启动冒烟。

Windows 环境只能完成源码、协议夹具和静态检查，不能代替上述 macOS 真机验收。

## 10. 发布与文档

- 首发文件名：`CodexQuotaRail-macOS-universal.app.zip`。
- 同时生成 `SHA256SUMS.txt`、`THIRD-PARTY-NOTICES.md` 和构建元数据。
- README 明确说明这是非 OpenAI 官方工具、未签名风险、Finder 右键打开步骤和辅助功能权限用途。
- 提供中文与英文安装、源码构建、Fork 定制、主题制作、故障排查和隐私说明。
- 发布说明不得声称已经公证或通过 Gatekeeper 自动验证。
- 后续签名版作为可选 CI Job 增加，不改变开源构建路径。

## 11. 验收标准

1. 同一 ZIP 中的应用可在 macOS 13+ 的 Apple Silicon 与 Intel 上启动。
2. 用户不进入 Codex 使用量页面即可看到真实“可用额度”。
3. 普通、全屏、最小化、失焦、多窗口和 Space 行为符合本规格。
4. 应用只请求辅助功能权限，不抓屏、不读取聊天或令牌。
5. 菜单、主题、开机启动、详情收回和领哥网站入口均可用。
6. 全部自动化测试与 macOS CI 构建通过，并完成真机手动验收。
7. Fork 用户只改配置和资源即可生成自定义 Universal `.app.zip`。
8. 仓库包含完整源码、测试、构建脚本、许可证、校验值与中英文文档。

## 12. 已知风险与控制

- **未签名分发**：提供 Finder 右键打开说明和哈希校验，不建议用户关闭全局 Gatekeeper。
- **辅助功能授权与应用路径绑定**：要求先移动到 `/Applications` 再授权，升级保持 Bundle Identifier 和稳定路径。
- **Codex 内部可执行路径变化**：优先支持 PATH 中官方 CLI，桌面包候选集中配置并通过兼容测试维护。
- **跨进程窗口层级差异**：相对窗口排序必须真机验证；失败时隐藏而不是使用全局置顶。
- **App Server 协议变化**：宽容解析未知字段、固定契约夹具、显式不支持状态和手动更新指引。
- **Intel 构建逐步弱化**：CI 每次验证 `x86_64` slice，只有正式修改平台支持政策后才能移除。
- **社区主题安全**：主题只能包含声明式数据，不加载脚本、动态库或远程代码。
