# Codex Quota Rail macOS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建 macOS 13+ 原生 SwiftUI + AppKit 菜单栏应用，在 Codex 窗口边缘显示真实可用额度，并发布 Intel/Apple Silicon Universal `.app.zip`。

**Architecture:** 领域模型、设置、协议客户端和窗口放置位于本地 Swift Package `CodexQuotaKit`；AppKit 外壳负责 Accessibility 窗口跟踪、`NSPanel`、`NSStatusItem` 与 `SMAppService`。Xcode 工程消费本地 Package，macOS GitHub Actions 执行测试、Universal 构建、ad-hoc 签名与 ZIP 校验。

**Tech Stack:** Swift 6、SwiftUI、AppKit、ApplicationServices、ServiceManagement、Foundation `Process`、XCTest、Xcode 16、GitHub Actions `macos-15`。

## Global Constraints

- 最低系统 macOS 13；同时生成 `arm64` 与 `x86_64`。
- 无运行时第三方依赖，不使用 Electron、WebView 或 .NET。
- 只申请辅助功能权限，不申请屏幕录制。
- 不通过 Shell 启动 Codex；不读取聊天、剪贴板、项目或令牌。
- 首发为未公证 Universal `.app.zip`，执行 ad-hoc codesign 并生成 SHA-256。
- 源码、测试、资源、CI 与文档使用现有 MIT 许可证，允许 Fork 定制。
- 所有生产逻辑先写失败测试，再写最小实现。

---

### Task 1: 建立 Swift Package 与额度领域模型

**Files:**
- Create: `macos/Packages/CodexQuotaKit/Package.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/QuotaModels.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/QuotaNormalizer.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/QuotaColor.swift`
- Create: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/QuotaNormalizerTests.swift`

**Interfaces:**
- Produces: `RawQuotaSnapshot`, `QuotaDisplayState`, `QuotaNormalizer.normalize(_:)`, `QuotaColor.rgb(for:)`。

- [ ] **Step 1: 写额度换算失败测试**

```swift
@Test func convertsUsedToAvailable() {
    let raw = RawQuotaSnapshot(primary: .init(label: "5 小时", usedPercent: 68, durationMinutes: 300, resetsAt: nil, unlimited: false), secondary: nil, receivedAt: .now)
    let state = QuotaNormalizer.normalize(raw)
    #expect(state.windows.map(\.availablePercent) == [32])
}

@Test(arguments: [(0, 100), (50, 50), (80, 20), (100, 0), (-1, 100), (101, 0)])
func clampsAvailable(_ used: Int, _ expected: Int) {
    #expect(QuotaNormalizer.availablePercent(from: used) == expected)
}
```

- [ ] **Step 2: 在 macOS runner 验证测试因类型缺失失败**

Run: `cd macos/Packages/CodexQuotaKit && swift test`

Expected: FAIL，提示 `RawQuotaSnapshot` 或 `QuotaNormalizer` 不存在。

- [ ] **Step 3: 实现不可变领域模型与连续颜色映射**

```swift
public enum QuotaWindowState: Sendable { case healthy, notice, critical, exhausted, unlimited, unavailable }
public struct QuotaWindowDisplay: Equatable, Sendable {
    public let label: String
    public let availablePercent: Int?
    public let resetsAt: Date?
    public let state: QuotaWindowState
}

public enum QuotaNormalizer {
    public static func availablePercent(from used: Int) -> Int { min(100, max(0, 100 - used)) }
    public static func normalize(_ snapshot: RawQuotaSnapshot) -> QuotaDisplayState {
        let windows = [snapshot.primary, snapshot.secondary].compactMap { source -> QuotaWindowDisplay? in
            guard let source else { return nil }
            if source.unlimited {
                return .init(label: source.label, availablePercent: nil, resetsAt: source.resetsAt, state: .unlimited)
            }
            guard let used = source.usedPercent else {
                return .init(label: source.label, availablePercent: nil, resetsAt: source.resetsAt, state: .unavailable)
            }
            let available = availablePercent(from: used)
            let state: QuotaWindowState = available == 0 ? .exhausted : available <= 20 ? .critical : available <= 50 ? .notice : .healthy
            return .init(label: source.label, availablePercent: available, resetsAt: source.resetsAt, state: state)
        }
        return .init(windows: windows, connection: .live, updatedAt: snapshot.receivedAt, message: nil)
    }
}
```

- [ ] **Step 4: 运行 Package 测试**

Run: `cd macos/Packages/CodexQuotaKit && swift test`

Expected: PASS，换算、无限、缺字段与阈值测试全部通过。

- [ ] **Step 5: 提交**

```bash
git add macos/Packages/CodexQuotaKit
git commit -m "实现 macOS 额度领域模型"
```

### Task 2: 实现设置、主题与开源定制配置

**Files:**
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/AppSettings.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/Theme.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/SettingsStore.swift`
- Create: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/SettingsTests.swift`
- Create: `macos/Config/Branding.xcconfig`
- Create: `macos/Config/Defaults.json`
- Create: `macos/Resources/Themes/default-dark.json`
- Create: `macos/Resources/Themes/default-light.json`

**Interfaces:**
- Produces: `AppSettings.validated()`, `Theme.validated()`, `JSONSettingsStore.load()/save(_:)`。

- [ ] **Step 1: 写无效设置与 HTTPS 失败测试**

```swift
@Test func rejectsNonHTTPSWebsite() {
    let settings = AppSettings(websiteURL: URL(string: "javascript:alert(1)")!)
    #expect(settings.validated().websiteURL == AppSettings.defaults.websiteURL)
}

@Test func clampsVisualValues() {
    let settings = AppSettings(focusedOpacity: 4, unfocusedOpacity: -1, railHeight: 500)
    let value = settings.validated()
    #expect(value.focusedOpacity == 1 && value.unfocusedOpacity == 0.2 && value.railHeight == 40)
}
```

- [ ] **Step 2: 运行并确认失败**

Run: `swift test --filter SettingsTests`

Expected: FAIL，设置类型不存在。

- [ ] **Step 3: 实现版本化 Codable 设置、原子保存和损坏文件隔离**

```swift
public struct AppSettings: Codable, Equatable, Sendable {
    public var schemaVersion = 1
    public var theme: ThemePreference = .automatic
    public var focusedOpacity = 1.0
    public var unfocusedOpacity = 0.52
    public var railHeight = 22.0
    public var compactHeight = 4.0
    public var websiteURL = URL(string: "https://lingge66.pages.dev/")!
    public var targetBundleIdentifiers: [String] = ["com.openai.codex"]
    public func validated() -> Self {
        var result = self
        result.focusedOpacity = min(1, max(0.2, result.focusedOpacity))
        result.unfocusedOpacity = min(1, max(0.2, result.unfocusedOpacity))
        result.railHeight = min(40, max(4, result.railHeight))
        result.compactHeight = min(8, max(1, result.compactHeight))
        if result.websiteURL.scheme?.lowercased() != "https" { result.websiteURL = Self.defaults.websiteURL }
        return result
    }
}
```

- [ ] **Step 4: 测试设置往返、迁移、损坏恢复和主题校验**

Run: `swift test --filter SettingsTests`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add macos/Config macos/Resources/Themes macos/Packages/CodexQuotaKit
git commit -m "支持 macOS 开源品牌与主题配置"
```

### Task 3: 实现 JSONL JSON-RPC 与额度 App Server 客户端

**Files:**
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/JSONRPCModels.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/JSONLineTransport.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/CodexAppServerClient.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/CodexExecutableResolver.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/BackoffSchedule.swift`
- Create: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/AppServerClientTests.swift`
- Create: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/CodexExecutableResolverTests.swift`

**Interfaces:**
- Produces: `CodexExecutableResolving.resolve()`, `CodexAppServerClient.start()`, `refresh()`, `snapshots: AsyncStream<RawQuotaSnapshot>`。

- [ ] **Step 1: 写协议映射与路径安全失败测试**

```swift
@Test func mapsRateLimitResponse() throws {
    let data = Data(#"{"rateLimits":{"primary":{"usedPercent":68,"windowDurationMins":300}}}"#.utf8)
    let snapshot = try RateLimitMapper.map(data, receivedAt: .now)
    #expect(snapshot.primary?.usedPercent == 68)
}

@Test func resolverNeverUsesAShell() throws {
    let fileSystem = FakeFileSystem(executables: ["/opt/homebrew/bin/codex"])
    let result = CodexExecutableResolver(fileSystem: fileSystem, environment: ["PATH": "/opt/homebrew/bin"]).resolve()
    #expect(result == .found(URL(fileURLWithPath: "/opt/homebrew/bin/codex")))
}
```

- [ ] **Step 2: 运行并确认失败**

Run: `swift test --filter AppServerClientTests && swift test --filter CodexExecutableResolverTests`

Expected: FAIL，协议和解析器类型不存在。

- [ ] **Step 3: 实现 actor 客户端与宽容 Codable 映射**

```swift
public actor CodexAppServerClient {
    public func start() async throws
    public func refresh() async throws -> RawQuotaSnapshot
    public func stop() async
}

// Process arguments are always separate tokens:
process.executableURL = executableURL
process.arguments = ["app-server", "--listen", "stdio://"]
```

初始化顺序固定为 `initialize`、`initialized`、`account/read`、`account/rateLimits/read`；收到 `account/rateLimits/updated` 后合并刷新请求。关闭时终止读取任务、恢复全部 continuation 并回收子进程。

- [ ] **Step 4: 覆盖正常、未知字段、未登录、断线、超时与通知刷新**

Run: `swift test --filter AppServerClientTests`

Expected: PASS，无挂起任务和未回收进程。

- [ ] **Step 5: 提交**

```bash
git add macos/Packages/CodexQuotaKit
git commit -m "接入 macOS Codex App Server 额度协议"
```

### Task 4: 实现跨屏窗口模型与 Accessibility 跟踪器

**Files:**
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/TrackedMacWindowSnapshot.swift`
- Create: `macos/Packages/CodexQuotaKit/Sources/CodexQuotaKit/RailPlacementCalculator.swift`
- Create: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/RailPlacementCalculatorTests.swift`
- Create: `macos/App/WindowTracking/AccessibilityPermissionService.swift`
- Create: `macos/App/WindowTracking/MacCodexLocator.swift`
- Create: `macos/App/WindowTracking/AccessibilityWindowTracker.swift`

**Interfaces:**
- Produces: `TargetWindowTracking`, `TrackedMacWindowSnapshot`, `RailPlacementCalculator.calculate(snapshot:settings:)`。

- [ ] **Step 1: 写普通、全屏、最小化和负坐标失败测试**

```swift
@Test func placesExternalRailAboveWindow() {
    let snapshot = TrackedMacWindowSnapshot(frame: .init(x: -800, y: 100, width: 700, height: 600), screenFrame: .init(x: -900, y: 0, width: 900, height: 900), isMinimized: false, isFullScreen: false, isFocused: true, windowNumber: 42)
    #expect(RailPlacementCalculator.calculate(snapshot: snapshot, settings: .defaults).frame == .init(x: -800, y: 700, width: 700, height: 22))
}
```

- [ ] **Step 2: 运行并确认失败**

Run: `swift test --filter RailPlacementCalculatorTests`

Expected: FAIL，窗口类型不存在。

- [ ] **Step 3: 实现纯函数放置计算与 AppKit Accessibility 适配器**

```swift
public protocol TargetWindowTracking: AnyObject {
    var snapshots: AsyncStream<TrackedMacWindowSnapshot?> { get }
    func start() async
    func stop() async
}
```

适配器只读取 PID、角色、位置、尺寸、最小化和焦点；用 PID 与边界关联 `CGWindowList` 窗口号，不读取像素或窗口内容。

- [ ] **Step 4: 运行 Package 测试并执行 macOS 编译检查**

Run: `swift test --filter RailPlacementCalculatorTests`

Run: `xcodebuild -project macos/CodexQuotaRailMac.xcodeproj -scheme CodexQuotaRailMac -configuration Debug build CODE_SIGNING_ALLOWED=NO`

Expected: PASS / `** BUILD SUCCEEDED **`。

- [ ] **Step 5: 提交**

```bash
git add macos/App/WindowTracking macos/Packages/CodexQuotaKit
git commit -m "跟踪 macOS Codex 窗口与贴边位置"
```

### Task 5: 建立 AppKit 应用外壳、菜单栏与边缘轨 UI

**Files:**
- Create: `macos/CodexQuotaRailMac.xcodeproj/project.pbxproj`
- Create: `macos/App/CodexQuotaRailMacApp.swift`
- Create: `macos/App/AppDelegate.swift`
- Create: `macos/App/ApplicationModel.swift`
- Create: `macos/App/MenuBar/MenuBarController.swift`
- Create: `macos/App/Rail/NonActivatingPanel.swift`
- Create: `macos/App/Rail/RailPanelController.swift`
- Create: `macos/App/Rail/RailView.swift`
- Create: `macos/App/Rail/QuotaTrackView.swift`
- Create: `macos/App/Rail/QuotaDetailsView.swift`
- Create: `macos/App/Info.plist`
- Create: `macos/Resources/Assets.xcassets/Contents.json`
- Create: `macos/Resources/Assets.xcassets/AppIcon.appiconset/Contents.json`
- Create: `macos/Resources/Assets.xcassets/MenuBarIcon.imageset/Contents.json`

**Interfaces:**
- Consumes: `QuotaDisplayState`, `RailPlacement`, `AppSettings`。
- Produces: menu commands and a non-activating rendered rail.

- [ ] **Step 1: 写菜单命令与预览状态测试**

```swift
@Test func everyMenuCommandDispatches() {
    let recorder = MenuCommandRecorder()
    MenuCommand.allCases.forEach(recorder.dispatch)
    #expect(recorder.commands == MenuCommand.allCases)
}
```

- [ ] **Step 2: 运行并确认失败**

Run: `swift test --filter MenuCommandTests`

Expected: FAIL，命令类型不存在。

- [ ] **Step 3: 实现 `NSStatusItem`、不可激活 `NSPanel` 和 SwiftUI 轨道**

```swift
final class NonActivatingPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

enum MenuCommand: CaseIterable {
    case refresh, toggleFollow, themeAutomatic, themeDark, themeLight,
         toggleReduceMotion, toggleLaunchAtLogin, openLogs, troubleshoot,
         openWebsite, checkUpdates, quit
}
```

所有尺寸、颜色、字体和透明度来自 `AppSettings`/`Theme`；`--rail-preview` 注入固定额度，不启动真实 App Server。

- [ ] **Step 4: 运行测试、Debug 构建与预览截图**

Run: `swift test`

Run: `xcodebuild -project macos/CodexQuotaRailMac.xcodeproj -scheme CodexQuotaRailMac -destination 'platform=macOS' -configuration Debug build CODE_SIGNING_ALLOWED=NO`

Run: `open build/Debug/CodexQuotaRailMac.app --args --rail-preview`

Expected: 菜单和轨道可见，轨道不激活，详情自动收回，中文不截断。

- [ ] **Step 5: 提交**

```bash
git add macos/App macos/Resources macos/CodexQuotaRailMac.xcodeproj macos/Packages/CodexQuotaKit
git commit -m "实现 macOS 菜单栏与额度边缘轨"
```

### Task 6: 接通生命周期、开机启动、日志与外部动作

**Files:**
- Create: `macos/App/Services/LaunchAtLoginService.swift`
- Create: `macos/App/Services/ApplicationLog.swift`
- Create: `macos/App/Services/ExternalActionService.swift`
- Create: `macos/App/Services/SingleInstanceService.swift`
- Create: `macos/App/Onboarding/OnboardingView.swift`
- Modify: `macos/App/ApplicationModel.swift`
- Test: `macos/Packages/CodexQuotaKit/Tests/CodexQuotaKitTests/ExternalURLPolicyTests.swift`

**Interfaces:**
- Produces: `LaunchAtLoginControlling`, `ExternalActionHandling`, lifecycle recovery.

- [ ] **Step 1: 写 HTTPS、状态恢复与菜单动作失败测试**

```swift
@Test(arguments: ["http://example.com", "file:///tmp/a", "javascript:alert(1)"])
func rejectsUnsafeExternalURL(_ raw: String) {
    #expect(ExternalURLPolicy.allowed(URL(string: raw)!) == false)
}
```

- [ ] **Step 2: 运行并确认失败**

Run: `swift test --filter ExternalURLPolicyTests`

Expected: FAIL，策略不存在。

- [ ] **Step 3: 实现系统服务**

`SMAppService.mainApp` 负责登录项；`NSWorkspace.open` 只接收 `ExternalURLPolicy` 允许的 HTTPS；日志写入 Application Support 并脱敏；睡眠、唤醒与网络事件触发暂停或立即刷新。

- [ ] **Step 4: 运行测试与 Xcode 构建**

Run: `cd macos/Packages/CodexQuotaKit && swift test`

Run: `xcodebuild -project macos/CodexQuotaRailMac.xcodeproj -scheme CodexQuotaRailMac -destination 'platform=macOS' -configuration Debug build CODE_SIGNING_ALLOWED=NO`

Expected: PASS / `** BUILD SUCCEEDED **`。

- [ ] **Step 5: 提交**

```bash
git add macos/App/Services macos/App/Onboarding macos/App/ApplicationModel.swift macos/Packages/CodexQuotaKit
git commit -m "完善 macOS 生命周期与系统服务"
```

### Task 7: Universal 构建、CI 与发布校验

**Files:**
- Create: `macos/Scripts/build-universal.sh`
- Create: `macos/Scripts/verify-app.sh`
- Create: `.github/workflows/macos-ci.yml`
- Create: `.github/workflows/macos-release.yml`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `artifacts/macos/CodexQuotaRail-macOS-universal.app.zip` and `SHA256SUMS.txt`。

- [ ] **Step 1: 写构建校验脚本，使缺少架构时失败**

```bash
architectures="$(lipo -archs "$binary")"
grep -qw arm64 <<<"$architectures"
grep -qw x86_64 <<<"$architectures"
codesign --verify --deep --strict "$app"
```

- [ ] **Step 2: 在 macOS CI 先运行校验并确认尚无产物时失败**

Run: `macos/Scripts/verify-app.sh artifacts/macos/CodexQuotaRailMac.app`

Expected: FAIL，应用不存在。

- [ ] **Step 3: 实现构建脚本和无 Secret 的 Actions 工作流**

构建使用 `ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO CODE_SIGN_IDENTITY=-`，再以 `ditto -c -k --sequesterRsrc --keepParent` 打包。工作流上传 ZIP、SHA-256、测试结果和构建日志。

- [ ] **Step 4: 运行 GitHub Actions 并下载验证产物**

Expected: `swift test` 与 `xcodebuild` 通过；`lipo -archs` 同时输出 `arm64 x86_64`；ZIP 可解包且 ad-hoc 签名验证通过。

- [ ] **Step 5: 提交**

```bash
git add macos/Scripts .github/workflows/macos-ci.yml .github/workflows/macos-release.yml .gitignore
git commit -m "建立 macOS Universal 开源发布链路"
```

### Task 8: 文档、视觉验收与最终交付

**Files:**
- Modify: `README.md`
- Modify: `README.zh-CN.md`
- Modify: `CONTRIBUTING.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/architecture.md`
- Create: `docs/macos-install.md`
- Create: `docs/macos-customization.md`
- Create: `docs/macos-privacy.md`
- Create: `artifacts/qa/macos/README.md`

**Interfaces:**
- Consumes: final app, tests, CI artifacts.
- Produces: install/customization/privacy documentation and QA evidence.

- [ ] **Step 1: 写中英文安装与 Fork 定制说明**

必须包含：移动到 `/Applications`、Finder 右键打开、辅助功能授权、登录项、卸载、换 LOGO/Bundle ID/网站/主题、Universal 构建、未签名风险、哈希验证和零遥测边界。

- [ ] **Step 2: 在真实 macOS 运行匹配表面 QA**

验证普通 22px、全屏 4px、最小化、Space、失焦 52%、详情收回、菜单动作、真实额度、默认浏览器、登录项、其他应用覆盖和双架构启动；截图不得包含真实账号或额度值。

- [ ] **Step 3: 运行最终自动化验证**

Run: `swift test`

Run: `xcodebuild -project macos/CodexQuotaRailMac.xcodeproj -scheme CodexQuotaRailMac -destination 'platform=macOS' test CODE_SIGNING_ALLOWED=NO`

Run: `macos/Scripts/build-universal.sh 0.1.0-macos.1`

Run: `macos/Scripts/verify-app.sh artifacts/macos/CodexQuotaRailMac.app`

Expected: 全部通过，无警告；生成 Universal ZIP 与 SHA-256。

- [ ] **Step 4: 核对规格逐条覆盖并更新发布台账**

确认设计规格 12 节均有实现或明确的真机阻塞证据；禁止把 Windows 静态检查描述成 macOS 真机通过。

- [ ] **Step 5: 提交**

```bash
git add README.md README.zh-CN.md CONTRIBUTING.md CHANGELOG.md docs artifacts/qa/macos
git commit -m "补充 macOS 开源构建与验收文档"
```
