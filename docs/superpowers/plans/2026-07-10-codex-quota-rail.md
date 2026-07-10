# Codex Quota Rail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 构建一个 Windows x64 开源常驻工具，在 Codex 窗口边缘稳定展示真实的剩余可用额度。

**Architecture:** 使用 .NET 8、WPF 与 Win32 将额度数据、窗口跟踪和视觉渲染拆成独立项目。额度通过官方 Codex App Server 的 stdio JSONL 协议读取，先转换为稳定领域模型，再由不获取焦点的边缘轨渲染；应用层负责托盘、自启、设置、日志和生命周期。

**Tech Stack:** C# 12、.NET 8 WPF、Win32 P/Invoke、System.Text.Json、xUnit 2.9.3、Microsoft.NET.Test.Sdk 18.7.0、Coverlet 10.0.1、NSIS 3.12、Microsoft SBOM Tool 4.1.5、GitHub Actions。

## Global Constraints

- 首发平台固定为 Windows x64，目标框架为 net8.0-windows。
- 首版不得修改、注入、解包或重新打包 Codex 桌面应用。
- 额度只能来自官方 Codex App Server；不得使用网页抓取、OCR、模拟点击或本地估算。
- 用户界面全部使用“可用额度”：availablePercent = clamp(100 - usedPercent, 0, 100)。
- 51%–100% 为绿色，21%–50% 为黄到橙，1%–20% 为红色，0% 才启用跑马灯。
- 普通窗口使用顶部外侧 22px 双轨；无外侧空间或最大化时使用标题栏顶部 4px 紧凑模式。
- Codex 失焦但仍可见时透明度为 52%；最小化时边缘轨隐藏。
- 不读取聊天、项目、剪贴板或账号令牌；默认零遥测，日志必须脱敏。
- 后台空闲 CPU 接近 0%，常驻内存目标低于 80MB，冷启动目标低于 2 秒。
- 首版渲染器为 RailQuotaRenderer；IQuotaRenderer 必须允许二期新增 PetQuotaRenderer。
- 新增 .NET SDK、NuGet 测试包、NSIS 或 SBOM 工具前，执行者必须先提醒用户并取得安装依赖的许可。
- 不采用 WiX 或 Inno Setup：其当前商业使用条款不符合“任何人都可以用”的目标。
- 依赖版本固定在 Directory.Packages.props 和 dotnet-tools.json；不得使用浮动版本。
- 每个任务都使用 TDD、独立验证和独立提交；不得用跳过测试或弱化断言换取绿色构建。

---

## File Structure

~~~text
CodexQuotaRail.sln
global.json
Directory.Build.props
Directory.Packages.props
LICENSE
README.md
README.zh-CN.md
SECURITY.md
CONTRIBUTING.md
CHANGELOG.md
.config/dotnet-tools.json
.github/workflows/ci.yml
.github/workflows/release.yml
src/CodexQuotaRail.Core/
  CodexQuotaRail.Core.csproj
  Quotas/QuotaModels.cs
  Quotas/QuotaNormalizer.cs
  Quotas/QuotaColorScale.cs
  Rendering/IQuotaRenderer.cs
src/CodexQuotaRail.AppServer/
  CodexQuotaRail.AppServer.csproj
  Protocol/ProtocolModels.cs
  Protocol/JsonRpcConnection.cs
  Transport/IJsonLineTransport.cs
  Transport/ProcessJsonLineTransport.cs
  Discovery/CodexExecutableResolver.cs
  RateLimits/RateLimitSource.cs
  Resilience/BackoffSchedule.cs
src/CodexQuotaRail.Windows/
  CodexQuotaRail.Windows.csproj
  Interop/NativeMethods.cs
  Windows/TrackedWindowSnapshot.cs
  Windows/CodexWindowTracker.cs
  Overlay/OverlayPlacementCalculator.cs
  Overlay/OverlayWindowController.cs
  Startup/AutostartService.cs
src/CodexQuotaRail.App/
  CodexQuotaRail.App.csproj
  App.xaml
  App.xaml.cs
  Hosting/ApplicationHost.cs
  Rail/RailWindow.xaml
  Rail/RailWindow.xaml.cs
  Rail/RailViewModel.cs
  Rail/RailQuotaRenderer.cs
  Rail/QuotaBrushConverter.cs
  Tray/TrayIconService.cs
  Settings/AppSettings.cs
  Settings/JsonSettingsStore.cs
  Diagnostics/JsonLineLog.cs
  Updates/GitHubReleaseChecker.cs
  Resources/Theme.Dark.xaml
  Resources/Theme.Light.xaml
tests/CodexQuotaRail.Core.Tests/
tests/CodexQuotaRail.AppServer.Tests/
tests/CodexQuotaRail.Windows.Tests/
tests/CodexQuotaRail.App.Tests/
tools/FakeCodexAppServer/
  FakeCodexAppServer.csproj
  Program.cs
  Fixtures/healthy.json
  Fixtures/single.json
  Fixtures/unlimited.json
packaging/nsis/CodexQuotaRail.nsi
scripts/build-release.ps1
scripts/manual-qa.ps1
docs/architecture.md
docs/privacy.md
docs/troubleshooting.md
docs/release-checklist.md
~~~

## Task 1: Bootstrap the build and test foundation

**Files:**
- Create: CodexQuotaRail.sln
- Create: global.json
- Create: Directory.Build.props
- Create: Directory.Packages.props
- Create: .config/dotnet-tools.json
- Create: all project files listed under src, tests, and tools

**Interfaces:**
- Consumes: .NET 8 SDK version 8.0.414 or newer.
- Produces: a buildable solution with four production projects, four test projects, and one fake App Server tool.

- [ ] **Step 1: Verify or install the required SDK only after user approval**

Run:

~~~powershell
dotnet --info
~~~

Expected before installation in the current environment: output says No SDKs were found.

After explicit approval, run:

~~~powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget
dotnet --version
~~~

Expected: version 8.0.414 or newer in the 8.0 feature band.

- [ ] **Step 2: Create the solution and projects**

Run:

~~~powershell
dotnet new sln --name CodexQuotaRail
dotnet new classlib --name CodexQuotaRail.Core --output src/CodexQuotaRail.Core --framework net8.0
dotnet new classlib --name CodexQuotaRail.AppServer --output src/CodexQuotaRail.AppServer --framework net8.0
dotnet new classlib --name CodexQuotaRail.Windows --output src/CodexQuotaRail.Windows --framework net8.0
dotnet new wpf --name CodexQuotaRail.App --output src/CodexQuotaRail.App --framework net8.0
dotnet new xunit --name CodexQuotaRail.Core.Tests --output tests/CodexQuotaRail.Core.Tests --framework net8.0
dotnet new xunit --name CodexQuotaRail.AppServer.Tests --output tests/CodexQuotaRail.AppServer.Tests --framework net8.0
dotnet new xunit --name CodexQuotaRail.Windows.Tests --output tests/CodexQuotaRail.Windows.Tests --framework net8.0
dotnet new xunit --name CodexQuotaRail.App.Tests --output tests/CodexQuotaRail.App.Tests --framework net8.0
dotnet new console --name FakeCodexAppServer --output tools/FakeCodexAppServer --framework net8.0
dotnet sln add src/CodexQuotaRail.Core/CodexQuotaRail.Core.csproj
dotnet sln add src/CodexQuotaRail.AppServer/CodexQuotaRail.AppServer.csproj
dotnet sln add src/CodexQuotaRail.Windows/CodexQuotaRail.Windows.csproj
dotnet sln add src/CodexQuotaRail.App/CodexQuotaRail.App.csproj
dotnet sln add tests/CodexQuotaRail.Core.Tests/CodexQuotaRail.Core.Tests.csproj
dotnet sln add tests/CodexQuotaRail.AppServer.Tests/CodexQuotaRail.AppServer.Tests.csproj
dotnet sln add tests/CodexQuotaRail.Windows.Tests/CodexQuotaRail.Windows.Tests.csproj
dotnet sln add tests/CodexQuotaRail.App.Tests/CodexQuotaRail.App.Tests.csproj
dotnet sln add tools/FakeCodexAppServer/FakeCodexAppServer.csproj
~~~

Expected: all nine projects are added without warnings.

- [ ] **Step 3: Lock language, analyzers, packages, and repository tools**

Create global.json:

~~~json
{
  "sdk": {
    "version": "8.0.414",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
~~~

Create Directory.Build.props:

~~~xml
<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <DebugType>embedded</DebugType>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <NuGetAuditMode>all</NuGetAuditMode>
  </PropertyGroup>
</Project>
~~~

Create Directory.Packages.props:

~~~xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="coverlet.collector" Version="10.0.1" />
  </ItemGroup>
</Project>
~~~

Create .config/dotnet-tools.json:

~~~json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "microsoft.sbom.dotnettool": {
      "version": "4.1.5",
      "commands": ["sbom-tool"]
    }
  }
}
~~~

In each test project, replace package references with:

~~~xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="coverlet.collector">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
~~~

- [ ] **Step 4: Add project references and Windows targets**

Run:

~~~powershell
dotnet add src/CodexQuotaRail.AppServer/CodexQuotaRail.AppServer.csproj reference src/CodexQuotaRail.Core/CodexQuotaRail.Core.csproj
dotnet add src/CodexQuotaRail.Windows/CodexQuotaRail.Windows.csproj reference src/CodexQuotaRail.Core/CodexQuotaRail.Core.csproj
dotnet add src/CodexQuotaRail.App/CodexQuotaRail.App.csproj reference src/CodexQuotaRail.Core/CodexQuotaRail.Core.csproj
dotnet add src/CodexQuotaRail.App/CodexQuotaRail.App.csproj reference src/CodexQuotaRail.AppServer/CodexQuotaRail.AppServer.csproj
dotnet add src/CodexQuotaRail.App/CodexQuotaRail.App.csproj reference src/CodexQuotaRail.Windows/CodexQuotaRail.Windows.csproj
dotnet add tests/CodexQuotaRail.Core.Tests/CodexQuotaRail.Core.Tests.csproj reference src/CodexQuotaRail.Core/CodexQuotaRail.Core.csproj
dotnet add tests/CodexQuotaRail.AppServer.Tests/CodexQuotaRail.AppServer.Tests.csproj reference src/CodexQuotaRail.AppServer/CodexQuotaRail.AppServer.csproj
dotnet add tests/CodexQuotaRail.Windows.Tests/CodexQuotaRail.Windows.Tests.csproj reference src/CodexQuotaRail.Windows/CodexQuotaRail.Windows.csproj
dotnet add tests/CodexQuotaRail.App.Tests/CodexQuotaRail.App.Tests.csproj reference src/CodexQuotaRail.App/CodexQuotaRail.App.csproj
~~~

Set TargetFramework to net8.0-windows10.0.19041.0 in App and Windows projects. Set UseWPF and UseWindowsForms to true only in App.

- [ ] **Step 5: Prove the empty solution is healthy**

Run:

~~~powershell
dotnet tool restore
dotnet restore --use-lock-file
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
~~~

Expected: build succeeds with zero warnings and all template tests pass.

- [ ] **Step 6: Commit**

~~~powershell
git add CodexQuotaRail.sln global.json Directory.Build.props Directory.Packages.props .config src tests tools
git commit -m "搭建 .NET 解决方案与测试基础"
~~~

## Task 2: Implement quota domain normalization and visual state

**Files:**
- Create: src/CodexQuotaRail.Core/Quotas/QuotaModels.cs
- Create: src/CodexQuotaRail.Core/Quotas/QuotaNormalizer.cs
- Create: src/CodexQuotaRail.Core/Quotas/QuotaColorScale.cs
- Create: src/CodexQuotaRail.Core/Rendering/IQuotaRenderer.cs
- Create: tests/CodexQuotaRail.Core.Tests/QuotaNormalizerTests.cs
- Create: tests/CodexQuotaRail.Core.Tests/QuotaColorScaleTests.cs

**Interfaces:**
- Consumes: raw primary and secondary windows from the App Server adapter.
- Produces: QuotaDisplayState, QuotaWindowDisplay, RgbColor, IQuotaRenderer.Render.

- [ ] **Step 1: Write failing normalization tests**

Create tests/CodexQuotaRail.Core.Tests/QuotaNormalizerTests.cs:

~~~csharp
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.Core.Tests;

public sealed class QuotaNormalizerTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(68, 32)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(120, 0)]
    public void ConvertsUsedToAvailable(int used, int expected)
    {
        var source = new RawQuotaWindow("5 小时", used, 300, 1_800_000_000, false);
        var result = QuotaNormalizer.NormalizeWindow(source);
        Assert.Equal(expected, result.AvailablePercent);
    }

    [Fact]
    public void MissingUsedPercentIsUnavailableNotFull()
    {
        var source = new RawQuotaWindow("5 小时", null, 300, null, false);
        var result = QuotaNormalizer.NormalizeWindow(source);
        Assert.Equal(QuotaWindowState.Unavailable, result.State);
        Assert.Null(result.AvailablePercent);
    }

    [Fact]
    public void OmitsMissingSecondaryWindow()
    {
        var source = new RawQuotaSnapshot(
            new RawQuotaWindow("5 小时", 40, 300, null, false),
            null,
            DateTimeOffset.UnixEpoch);
        var result = QuotaNormalizer.Normalize(source);
        Assert.Single(result.Windows);
    }
}
~~~

- [ ] **Step 2: Run tests and confirm the intended failure**

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.Core.Tests --filter QuotaNormalizerTests
~~~

Expected: FAIL because RawQuotaWindow and QuotaNormalizer do not exist.

- [ ] **Step 3: Implement immutable domain models and normalization**

Create QuotaModels.cs with these public contracts:

~~~csharp
namespace CodexQuotaRail.Core.Quotas;

public enum QuotaConnectionState { Connecting, Live, Stale, AuthenticationRequired, Unsupported, Unavailable }
public enum QuotaWindowState { Healthy, Notice, Critical, Exhausted, Unlimited, Unavailable }

public sealed record RawQuotaWindow(
    string Label,
    int? UsedPercent,
    long? WindowDurationMins,
    long? ResetsAtUnixSeconds,
    bool IsUnlimited);

public sealed record RawQuotaSnapshot(
    RawQuotaWindow? Primary,
    RawQuotaWindow? Secondary,
    DateTimeOffset ReceivedAt);

public sealed record QuotaWindowDisplay(
    string Label,
    int? AvailablePercent,
    TimeSpan? WindowDuration,
    DateTimeOffset? ResetsAt,
    QuotaWindowState State);

public sealed record QuotaDisplayState(
    IReadOnlyList<QuotaWindowDisplay> Windows,
    QuotaConnectionState Connection,
    DateTimeOffset? UpdatedAt,
    string? Message)
{
    public static QuotaDisplayState Waiting(string message) =>
        new(Array.Empty<QuotaWindowDisplay>(), QuotaConnectionState.Connecting, null, message);
}
~~~

Create QuotaNormalizer.cs:

~~~csharp
namespace CodexQuotaRail.Core.Quotas;

public static class QuotaNormalizer
{
    public static QuotaDisplayState Normalize(RawQuotaSnapshot snapshot)
    {
        var windows = new[] { snapshot.Primary, snapshot.Secondary }
            .Where(window => window is not null)
            .Select(window => NormalizeWindow(window!))
            .ToArray();
        return new QuotaDisplayState(windows, QuotaConnectionState.Live, snapshot.ReceivedAt, null);
    }

    public static QuotaWindowDisplay NormalizeWindow(RawQuotaWindow source)
    {
        if (source.IsUnlimited)
            return new(source.Label, null, Duration(source), Reset(source), QuotaWindowState.Unlimited);
        if (source.UsedPercent is null)
            return new(source.Label, null, Duration(source), Reset(source), QuotaWindowState.Unavailable);

        var available = Math.Clamp(100 - source.UsedPercent.Value, 0, 100);
        var state = available switch
        {
            0 => QuotaWindowState.Exhausted,
            <= 20 => QuotaWindowState.Critical,
            <= 50 => QuotaWindowState.Notice,
            _ => QuotaWindowState.Healthy
        };
        return new(source.Label, available, Duration(source), Reset(source), state);
    }

    private static TimeSpan? Duration(RawQuotaWindow source) =>
        source.WindowDurationMins is long minutes ? TimeSpan.FromMinutes(minutes) : null;

    private static DateTimeOffset? Reset(RawQuotaWindow source) =>
        source.ResetsAtUnixSeconds is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}
~~~

Create IQuotaRenderer.cs:

~~~csharp
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.Core.Rendering;

public interface IQuotaRenderer
{
    void Render(QuotaDisplayState state);
}
~~~

- [ ] **Step 4: Write and pass color anchor tests**

Create QuotaColorScaleTests.cs with exact anchors 100=(145,239,107), 51=(201,239,99), 21=(255,196,91), 1=(255,97,93), and verify values between anchors interpolate per channel.

Implement QuotaColorScale.cs:

~~~csharp
namespace CodexQuotaRail.Core.Quotas;

public readonly record struct RgbColor(byte R, byte G, byte B);

public static class QuotaColorScale
{
    private static readonly (int Percent, RgbColor Color)[] Anchors =
    [
        (0, new(255, 97, 93)),
        (1, new(255, 97, 93)),
        (21, new(255, 196, 91)),
        (51, new(201, 239, 99)),
        (100, new(145, 239, 107))
    ];

    public static RgbColor ForAvailable(int availablePercent)
    {
        var value = Math.Clamp(availablePercent, 0, 100);
        for (var index = 1; index < Anchors.Length; index++)
        {
            var upper = Anchors[index];
            var lower = Anchors[index - 1];
            if (value <= upper.Percent)
            {
                var ratio = (double)(value - lower.Percent) / (upper.Percent - lower.Percent);
                return new(
                    Lerp(lower.Color.R, upper.Color.R, ratio),
                    Lerp(lower.Color.G, upper.Color.G, ratio),
                    Lerp(lower.Color.B, upper.Color.B, ratio));
            }
        }
        return Anchors[^1].Color;
    }

    private static byte Lerp(byte start, byte end, double ratio) =>
        (byte)Math.Round(start + ((end - start) * ratio), MidpointRounding.AwayFromZero);
}
~~~

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.Core.Tests
~~~

Expected: all normalization and color tests pass.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.Core tests/CodexQuotaRail.Core.Tests
git commit -m "实现可用额度领域模型"
~~~

## Task 3: Build the JSONL JSON-RPC App Server client

**Files:**
- Create: src/CodexQuotaRail.AppServer/Transport/IJsonLineTransport.cs
- Create: src/CodexQuotaRail.AppServer/Transport/ProcessJsonLineTransport.cs
- Create: src/CodexQuotaRail.AppServer/Protocol/ProtocolModels.cs
- Create: src/CodexQuotaRail.AppServer/Protocol/JsonRpcConnection.cs
- Create: tests/CodexQuotaRail.AppServer.Tests/JsonRpcConnectionTests.cs
- Create: tests/CodexQuotaRail.AppServer.Tests/FakeJsonLineTransport.cs

**Interfaces:**
- Consumes: newline-delimited JSON messages without a jsonrpc field.
- Produces: RequestAsync, NotifyAsync, and NotificationReceived.

- [ ] **Step 1: Write failing handshake and correlation tests**

Tests must prove:

- initialize request uses clientInfo name codex_quota_rail, title Codex Quota Rail, and assembly version.
- initialized notification is sent only after initialize succeeds.
- concurrent request IDs return to the correct caller when responses arrive out of order.
- malformed JSON raises a protocol error event without exposing raw secrets in the message.
- server errors preserve code and safe message.

Use this exact transport interface:

~~~csharp
namespace CodexQuotaRail.AppServer.Transport;

public interface IJsonLineTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
}
~~~

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.AppServer.Tests --filter JsonRpcConnectionTests
~~~

Expected: FAIL because JsonRpcConnection does not exist.

- [ ] **Step 2: Implement protocol messages and connection**

ProtocolModels.cs must define:

~~~csharp
using System.Text.Json;

namespace CodexQuotaRail.AppServer.Protocol;

public sealed record JsonRpcNotification(string Method, JsonElement Params);
public sealed record JsonRpcServerError(int Code, string Message);
public sealed class AppServerProtocolException(string message) : Exception(message);
public sealed class AppServerRequestException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
~~~

JsonRpcConnection must expose:

~~~csharp
public sealed class JsonRpcConnection : IAsyncDisposable
{
    public event EventHandler<JsonRpcNotification>? NotificationReceived;
    public event EventHandler<AppServerProtocolException>? ProtocolError;
    public Task StartAsync(CancellationToken cancellationToken);
    public Task InitializeAsync(Version version, CancellationToken cancellationToken);
    public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken);
    public ValueTask NotifyAsync(string method, object? parameters, CancellationToken cancellationToken);
    public ValueTask DisposeAsync();
}
~~~

InitializeAsync must send:

~~~json
{"method":"initialize","id":1,"params":{"clientInfo":{"name":"codex_quota_rail","title":"Codex Quota Rail","version":"0.1.0"}}}
{"method":"initialized","params":{}}
~~~

Do not set experimentalApi. Start a single background read loop, store pending requests in ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>, and create TCS instances with RunContinuationsAsynchronously.

- [ ] **Step 3: Implement process transport**

ProcessJsonLineTransport starts:

~~~text
codex app-server --listen stdio://
~~~

Set UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true. Read stdout with ReadLineAsync; send stderr only to the redacted diagnostic sink and never parse stderr as JSON.

- [ ] **Step 4: Verify protocol tests**

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.AppServer.Tests --filter JsonRpcConnectionTests
~~~

Expected: all handshake, correlation, malformed-response, cancellation, and disposal tests pass.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.AppServer/Protocol src/CodexQuotaRail.AppServer/Transport tests/CodexQuotaRail.AppServer.Tests
git commit -m "实现 App Server JSONL 客户端"
~~~

## Task 4: Read rate limits, discover Codex, and reconnect safely

**Files:**
- Create: src/CodexQuotaRail.AppServer/Protocol/RateLimitDtos.cs
- Create: src/CodexQuotaRail.AppServer/Discovery/CodexExecutableResolver.cs
- Create: src/CodexQuotaRail.AppServer/RateLimits/IRateLimitSource.cs
- Create: src/CodexQuotaRail.AppServer/RateLimits/RateLimitSource.cs
- Create: src/CodexQuotaRail.AppServer/Resilience/BackoffSchedule.cs
- Create: tests/CodexQuotaRail.AppServer.Tests/RateLimitSourceTests.cs
- Create: tests/CodexQuotaRail.AppServer.Tests/CodexExecutableResolverTests.cs

**Interfaces:**
- Consumes: account/read, account/rateLimits/read, and account/rateLimits/updated.
- Produces: SnapshotChanged and ConnectionChanged events carrying Core domain objects.

- [ ] **Step 1: Write failing DTO mapping and refresh tests**

Use official JSON fixtures containing primary.usedPercent, secondary.usedPercent, windowDurationMins, resetsAt, credits.unlimited, planType, and rateLimitsByLimitId.

Tests must prove:

- 68 used maps to RawQuotaWindow.UsedPercent=68 and later becomes 32 available.
- primary and secondary are both retained.
- missing secondary stays null.
- explicit unlimited maps to IsUnlimited=true.
- a rateLimits/updated notification triggers one fresh account/rateLimits/read instead of trusting a partial notification.
- no change notification still triggers a 60-second refresh using an injected TimeProvider.

- [ ] **Step 2: Implement DTOs and source contract**

IRateLimitSource:

~~~csharp
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public interface IRateLimitSource : IAsyncDisposable
{
    event EventHandler<RawQuotaSnapshot>? SnapshotChanged;
    event EventHandler<QuotaConnectionState>? ConnectionChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
}
~~~

DTO fields must be nullable where the protocol permits omission. Use JsonPropertyName attributes and never treat a missing integer as zero.

- [ ] **Step 3: Implement executable discovery**

CodexExecutableResolver order:

1. CODEX_QUOTA_RAIL_CODEX_PATH environment override, only if the resolved file exists.
2. Get-Command-compatible PATH search for codex.exe or codex.cmd.
3. Running Codex child process executable path when Windows permits access.
4. Installed OpenAI.Codex package resource path when it is executable by the current user.

Return a discriminated result:

~~~csharp
public abstract record CodexResolution
{
    public sealed record Found(string FileName, IReadOnlyList<string> PrefixArguments) : CodexResolution;
    public sealed record Missing(string UserMessage) : CodexResolution;
    public sealed record Unsupported(string UserMessage) : CodexResolution;
}
~~~

Never download a binary and never fall back to scraping.

- [ ] **Step 4: Implement refresh and backoff**

BackoffSchedule returns 2, 5, 15, 30, and 60 seconds, then stays at 60 seconds. RateLimitSource resets the schedule after a valid snapshot, pauses retries while Windows reports suspended state, and refreshes immediately after resume or network availability.

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.AppServer.Tests
~~~

Expected: protocol, discovery, mapping, notification, timer, and backoff tests pass.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.AppServer tests/CodexQuotaRail.AppServer.Tests
git commit -m "接入 Codex 额度读取与重连"
~~~

## Task 5: Track the Codex window and calculate overlay placement

**Files:**
- Create: src/CodexQuotaRail.Windows/Interop/NativeMethods.cs
- Create: src/CodexQuotaRail.Windows/Windows/TrackedWindowSnapshot.cs
- Create: src/CodexQuotaRail.Windows/Windows/IWindowNativeApi.cs
- Create: src/CodexQuotaRail.Windows/Windows/CodexWindowTracker.cs
- Create: src/CodexQuotaRail.Windows/Overlay/OverlayPlacementCalculator.cs
- Create: tests/CodexQuotaRail.Windows.Tests/OverlayPlacementCalculatorTests.cs
- Create: tests/CodexQuotaRail.Windows.Tests/CodexWindowTrackerTests.cs

**Interfaces:**
- Consumes: WinEvent notifications and native window snapshots.
- Produces: TrackedWindowSnapshot and OverlayPlacement without referencing WPF types.

- [ ] **Step 1: Write failing placement tests**

Define pixel-only records:

~~~csharp
public readonly record struct PixelRect(int Left, int Top, int Width, int Height);
public sealed record TrackedWindowSnapshot(
    nint Handle,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiScale,
    bool IsVisible,
    bool IsMinimized,
    bool IsMaximized,
    bool IsForeground);
public enum OverlayMode { Hidden, ExternalRail, CompactTitleBar }
public sealed record OverlayPlacement(PixelRect Bounds, OverlayMode Mode, double Opacity);
~~~

Tests must assert:

- normal window with 22px free space places rail at Top-22 and opacity 1.0.
- unfocused visible window uses opacity 0.52.
- minimized or invisible window returns Hidden.
- maximized window returns CompactTitleBar at window Top with height 4.
- a normal window less than 22px from work-area top also uses CompactTitleBar.
- all values remain inside the monitor work area.

- [ ] **Step 2: Implement pure placement calculation**

~~~csharp
public static class OverlayPlacementCalculator
{
    public static OverlayPlacement Calculate(TrackedWindowSnapshot window)
    {
        if (!window.IsVisible || window.IsMinimized)
            return new(new(0, 0, 0, 0), OverlayMode.Hidden, 0);

        var opacity = window.IsForeground ? 1.0 : 0.52;
        var hasExternalSpace = window.Bounds.Top - window.WorkArea.Top >= 22;
        if (!window.IsMaximized && hasExternalSpace)
        {
            return new(
                new(window.Bounds.Left, window.Bounds.Top - 22, window.Bounds.Width, 22),
                OverlayMode.ExternalRail,
                opacity);
        }

        return new(
            new(window.Bounds.Left, window.Bounds.Top, window.Bounds.Width, 4),
            OverlayMode.CompactTitleBar,
            opacity);
    }
}
~~~

- [ ] **Step 3: Implement native abstraction and event tracker**

IWindowNativeApi wraps EnumWindows, GetWindowThreadProcessId, GetWindowRect, IsWindowVisible, IsIconic, GetForegroundWindow, MonitorFromWindow, GetMonitorInfo, and GetDpiForWindow.

CodexWindowTracker must:

- identify OpenAI Codex by signed package/process identity rather than title text alone;
- install out-of-context WinEvent hooks for foreground, show/hide, location change, minimize start/end, and move/size end;
- coalesce location events to one update per dispatcher frame;
- select the most recently active visible main window;
- release every hook on Dispose.

- [ ] **Step 4: Verify with fake native API**

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.Windows.Tests
~~~

Expected: placement, window selection, event coalescing, DPI, and disposal tests pass without needing a real Codex window.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.Windows tests/CodexQuotaRail.Windows.Tests
git commit -m "实现 Codex 窗口跟踪与贴边定位"
~~~

## Task 6: Build the non-activating WPF rail and renderer

**Files:**
- Create: src/CodexQuotaRail.App/Rail/RailWindow.xaml
- Create: src/CodexQuotaRail.App/Rail/RailWindow.xaml.cs
- Create: src/CodexQuotaRail.App/Rail/RailViewModel.cs
- Create: src/CodexQuotaRail.App/Rail/RailQuotaRenderer.cs
- Create: src/CodexQuotaRail.App/Rail/QuotaBrushConverter.cs
- Create: src/CodexQuotaRail.App/Resources/Theme.Dark.xaml
- Create: src/CodexQuotaRail.App/Resources/Theme.Light.xaml
- Create: tests/CodexQuotaRail.App.Tests/RailViewModelTests.cs
- Create: tests/CodexQuotaRail.App.Tests/QuotaBrushConverterTests.cs

**Interfaces:**
- Consumes: QuotaDisplayState and OverlayPlacement.
- Produces: a WPF rail that never takes keyboard focus and exposes hover details.

- [ ] **Step 1: Write failing view-model tests**

Tests must assert:

- two windows create two visible tracks;
- one window creates one centered track;
- unavailable displays “额度暂不可用” and never 100%;
- unlimited displays “无限” with no fake percent;
- compact mode hides labels but keeps two colored tracks;
- exhausted state enables marquee only when ReduceMotion=false;
- changing ReduceMotion immediately disables marquee.

- [ ] **Step 2: Implement view model contracts**

RailViewModel exposes:

~~~csharp
public sealed class RailViewModel : INotifyPropertyChanged
{
    public ReadOnlyObservableCollection<QuotaTrackViewModel> Tracks { get; }
    public string StatusText { get; private set; } = "正在连接 Codex";
    public bool IsCompact { get; private set; }
    public bool IsMarqueeActive { get; private set; }
    public bool ReduceMotion { get; set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public void Apply(QuotaDisplayState state, OverlayMode mode);
}
~~~

QuotaTrackViewModel contains Label, AvailablePercent, WidthFraction, Color, ResetText, State, and IsUnlimited.

- [ ] **Step 3: Build the XAML rail**

RailWindow requirements:

- WindowStyle=None, ResizeMode=NoResize, ShowInTaskbar=False, AllowsTransparency=True, ShowActivated=False.
- Root Border corner radius 8, background #F210100E in dark mode.
- A compact CODEX label, one or two proportional tracks, percent text, and reset countdown.
- Popup with Focusable=False and StaysOpen=False for hover details.
- External mode height 22; compact mode height 4 with all text collapsed.
- Use a single CompositionTarget.Rendering-coalesced position update rather than starting WPF animations for every window movement.

In OnSourceInitialized, add WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE. Do not add WS_EX_TOPMOST or WS_EX_TRANSPARENT.

- [ ] **Step 4: Implement themes, colors, and restrained animation**

QuotaBrushConverter converts Core RgbColor to frozen SolidColorBrush. Dark and light dictionaries contain only semantic resources. Threshold crossings run one 1.2-second shimmer; 0% starts a slow translate animation; ReduceMotion uses static red text and no storyboard.

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.App.Tests --filter "FullyQualifiedName~RailViewModelTests|FullyQualifiedName~QuotaBrushConverterTests"
dotnet build src/CodexQuotaRail.App --configuration Release
~~~

Expected: all view-model tests pass and WPF build has zero warnings.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.App/Rail src/CodexQuotaRail.App/Resources tests/CodexQuotaRail.App.Tests
git commit -m "实现额度边缘轨视觉组件"
~~~

## Task 7: Add tray, settings, single instance, autostart, and redacted logs

**Files:**
- Create: src/CodexQuotaRail.App/Settings/AppSettings.cs
- Create: src/CodexQuotaRail.App/Settings/JsonSettingsStore.cs
- Create: src/CodexQuotaRail.App/Tray/TrayIconService.cs
- Create: src/CodexQuotaRail.App/Diagnostics/JsonLineLog.cs
- Create: src/CodexQuotaRail.App/Hosting/SingleInstanceGuard.cs
- Create: src/CodexQuotaRail.Windows/Startup/AutostartService.cs
- Create: tests/CodexQuotaRail.App.Tests/JsonSettingsStoreTests.cs
- Create: tests/CodexQuotaRail.App.Tests/JsonLineLogTests.cs
- Create: tests/CodexQuotaRail.Windows.Tests/AutostartServiceTests.cs

**Interfaces:**
- Consumes: user tray actions and persisted JSON.
- Produces: versioned settings, HKCU autostart state, redacted rolling logs, and a single app instance.

- [ ] **Step 1: Write failing persistence and redaction tests**

AppSettings defaults:

~~~csharp
public sealed record AppSettings(
    int SchemaVersion = 1,
    bool StartWithWindows = true,
    bool ReduceMotion = false,
    ThemePreference Theme = ThemePreference.Automatic,
    bool FollowPaused = false);
~~~

Tests must verify atomic save via temporary file plus replace, corrupted JSON falls back to defaults while preserving the corrupt file with a .bad suffix, and migration rejects future schema versions with a visible error.

Logging tests must verify replacement of Windows user paths, account IDs, Authorization headers, accessToken, refreshToken, and command-line token values. Rotate at 5MB and retain three files.

- [ ] **Step 2: Implement per-user settings and logging**

Use:

~~~text
%LocalAppData%\CodexQuotaRail\settings.json
%LocalAppData%\CodexQuotaRail\logs\app-YYYYMMDD.jsonl
~~~

Do not serialize raw protocol responses. Each log event contains timestamp, level, event name, safe message, and exception type only.

- [ ] **Step 3: Implement autostart and single-instance behavior**

AutostartService writes only:

~~~text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
Name: CodexQuotaRail
Value: "<absolute exe path>" --background
~~~

SingleInstanceGuard uses named mutex Local\CodexQuotaRail and a named pipe CodexQuotaRail.Activate. A second instance sends activate and exits with code 0.

- [ ] **Step 4: Implement tray menu**

Menu items in Chinese:

- 状态与最近更新时间
- 立即刷新
- 暂停/恢复窗口跟随
- 主题：自动、深色、浅色
- 减少动画
- 开机时启动
- 检查更新
- 打开日志目录
- 故障排查
- 退出

All changes persist immediately. Exiting disposes tray, hooks, App Server process, pipe, and mutex in that order.

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.App.Tests
dotnet test tests/CodexQuotaRail.Windows.Tests
~~~

Expected: persistence, logging, registry abstraction, mutex, and tray-state tests pass.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.App/Settings src/CodexQuotaRail.App/Tray src/CodexQuotaRail.App/Diagnostics src/CodexQuotaRail.App/Hosting src/CodexQuotaRail.Windows/Startup tests
git commit -m "完善托盘设置与应用生命周期"
~~~

## Task 8: Compose the application and build a deterministic fake server

**Files:**
- Create: src/CodexQuotaRail.App/Hosting/ApplicationHost.cs
- Modify: src/CodexQuotaRail.App/App.xaml
- Modify: src/CodexQuotaRail.App/App.xaml.cs
- Create: tools/FakeCodexAppServer/Program.cs
- Create: tools/FakeCodexAppServer/Fixtures/healthy.json
- Create: tools/FakeCodexAppServer/Fixtures/single.json
- Create: tools/FakeCodexAppServer/Fixtures/unlimited.json
- Create: tests/CodexQuotaRail.App.Tests/ApplicationHostTests.cs
- Create: tests/CodexQuotaRail.AppServer.Tests/ProcessIntegrationTests.cs

**Interfaces:**
- Consumes: window tracker, rate source, renderer, settings, and app lifetime services.
- Produces: a running integrated app and a fake server usable without a real account.

- [ ] **Step 1: Write failing orchestration tests**

Tests must verify:

- host renders Waiting before the first snapshot;
- first snapshot is normalized then rendered on the WPF dispatcher;
- stale connection keeps last data and changes Connection to Stale;
- missing Codex hides overlay but leaves tray alive;
- shutdown is idempotent and disposes in reverse dependency order.

- [ ] **Step 2: Implement ApplicationHost**

ApplicationHost constructor takes interfaces only. StartAsync order:

1. load settings;
2. create tray;
3. start window tracker;
4. resolve/start rate source;
5. render waiting state;
6. subscribe to snapshots and window changes.

No async void except WPF event handlers; every event handler forwards to a Task-returning private method and logs failures.

- [ ] **Step 3: Implement fake App Server**

FakeCodexAppServer reads JSONL stdin and must:

- respond to initialize with userAgent, platformFamily=windows, and platformOs=windows;
- accept initialized notification;
- respond to account/read with a ChatGPT account fixture;
- respond to account/rateLimits/read from the selected fixture;
- optionally emit account/rateLimits/updated;
- support --fixture healthy, --fixture single, --fixture unlimited, and --disconnect-after-read.

Healthy fixture must include usedPercent 68 and 41 so the UI renders available 32% and 59%.

- [ ] **Step 4: Run process-level integration**

Run:

~~~powershell
dotnet test tests/CodexQuotaRail.AppServer.Tests --filter ProcessIntegrationTests
dotnet build tools/FakeCodexAppServer
$env:CODEX_QUOTA_RAIL_CODEX_PATH = (Resolve-Path tools/FakeCodexAppServer/bin/Debug/net8.0/FakeCodexAppServer.exe).Path
dotnet run --project src/CodexQuotaRail.App
~~~

Expected: rail renders 32% and 59% from the fake server; no real Codex credentials are read.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CodexQuotaRail.App/Hosting src/CodexQuotaRail.App/App.xaml src/CodexQuotaRail.App/App.xaml.cs tools/FakeCodexAppServer tests
git commit -m "贯通额度数据与窗口渲染"
~~~

## Task 9: Harden Windows behavior, accessibility, and performance

**Files:**
- Modify: src/CodexQuotaRail.Windows/Windows/CodexWindowTracker.cs
- Modify: src/CodexQuotaRail.Windows/Overlay/OverlayWindowController.cs
- Modify: src/CodexQuotaRail.App/Rail/RailWindow.xaml.cs
- Modify: src/CodexQuotaRail.App/Rail/RailViewModel.cs
- Create: tests/CodexQuotaRail.Windows.Tests/SystemTransitionTests.cs
- Create: tests/CodexQuotaRail.App.Tests/AccessibilityStateTests.cs
- Create: scripts/manual-qa.ps1

**Interfaces:**
- Consumes: suspend/resume, network availability, DPI, Explorer restart, theme, and animation settings.
- Produces: stable recovery and measurable performance evidence.

- [ ] **Step 1: Add failing system transition tests**

Cover:

- sleep pauses retry timers;
- resume performs immediate full refresh;
- Explorer restart recreates tray icon;
- network restore resets backoff and refreshes;
- DPI changes recalculate once without oscillation;
- foreground changes animate opacity for 180ms unless ReduceMotion=true;
- an unrelated foreground window keeps the rail behind it.

- [ ] **Step 2: Implement transition handling**

Subscribe to SystemEvents.PowerModeChanged, NetworkChange.NetworkAvailabilityChanged, WM_DPICHANGED, TaskbarCreated, and WinEvent foreground notifications through injectable adapters. Marshal all WPF work to Dispatcher. Dispose every static event subscription.

- [ ] **Step 3: Add accessibility behavior**

Automatic theme follows Windows AppsUseLightTheme. ReduceMotion defaults to the Windows client-area animation setting when the user has not explicitly chosen a value. Every color state includes percentage text and a status label. Popup text supports at least 200% scaling.

- [ ] **Step 4: Measure performance and prevent regressions**

manual-qa.ps1 samples the process for 60 idle seconds:

~~~powershell
$process = Get-Process CodexQuotaRail.App
$startCpu = $process.CPU
Start-Sleep -Seconds 60
$process.Refresh()
[pscustomobject]@{
  CpuSeconds = [math]::Round($process.CPU - $startCpu, 3)
  WorkingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 1)
}
~~~

Acceptance: idle CpuSeconds <= 0.3 over 60 seconds and WorkingSetMb < 80 on the QA machine. Record the actual result in docs/release-checklist.md.

- [ ] **Step 5: Run all automated tests**

~~~powershell
dotnet test --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults
dotnet build --configuration Release --no-restore
~~~

Expected: zero warnings, all tests pass, Cobertura coverage files are produced.

- [ ] **Step 6: Commit**

~~~powershell
git add src tests scripts/manual-qa.ps1
git commit -m "强化系统恢复与无障碍体验"
~~~

## Task 10: Package, document, and automate releases

**Files:**
- Create: LICENSE
- Create: README.md
- Create: README.zh-CN.md
- Create: SECURITY.md
- Create: CONTRIBUTING.md
- Create: CHANGELOG.md
- Create: docs/architecture.md
- Create: docs/privacy.md
- Create: docs/troubleshooting.md
- Create: docs/release-checklist.md
- Create: packaging/nsis/CodexQuotaRail.nsi
- Create: scripts/build-release.ps1
- Create: .github/workflows/ci.yml
- Create: .github/workflows/release.yml
- Create: src/CodexQuotaRail.App/Updates/GitHubReleaseChecker.cs
- Create: tests/CodexQuotaRail.App.Tests/GitHubReleaseCheckerTests.cs

**Interfaces:**
- Consumes: Release build output and GitHub Releases metadata.
- Produces: portable ZIP, per-user installer EXE, SHA-256 file, SPDX SBOM, bilingual docs, and CI evidence.

- [ ] **Step 1: Add MIT license and exact project notices**

LICENSE uses the standard MIT text with copyright 2026 LingGe. README files state:

- 非 OpenAI 官方产品 / Not affiliated with or endorsed by OpenAI.
- Only official Codex App Server data is used.
- No tokens, chats, projects, or clipboard are collected.
- Windows x64 is the only supported first-release platform.
- Source and release verification commands.

Third-party notices list xUnit, Coverlet, Microsoft Test SDK, Microsoft SBOM Tool, and NSIS with their licenses.

- [ ] **Step 2: Test and implement manual update checking**

GitHubReleaseChecker uses an injected HttpClient and requests:

~~~text
https://api.github.com/repos/lingge66/codex-quota-rail/releases/latest
~~~

It runs only after the user clicks 检查更新, sets a descriptive User-Agent, times out after 10 seconds, compares semantic versions without prerelease auto-selection, and opens the browser only after user confirmation. Tests use a fake HttpMessageHandler and never call GitHub.

- [ ] **Step 3: Create NSIS 3.12 per-user installer**

CodexQuotaRail.nsi must include:

~~~text
Unicode True
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\CodexQuotaRail"
SetCompressor /SOLID zlib
~~~

Main section copies the self-contained win-x64 publish output, writes an uninstaller, creates a Start Menu shortcut, and registers Add/Remove Programs under HKCU. A default-selected section named “开机时启动（推荐）” writes the same HKCU Run value used by AutostartService. Uninstall removes app files, shortcuts, uninstall registration, and the Run value but preserves settings/logs unless the user selects “同时删除本地设置”.

- [ ] **Step 4: Create deterministic release script**

scripts/build-release.ps1 must:

~~~powershell
$ErrorActionPreference = 'Stop'
dotnet restore --locked-mode
dotnet test --configuration Release --no-restore
dotnet publish src/CodexQuotaRail.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o artifacts/publish/win-x64
Compress-Archive artifacts/publish/win-x64/* artifacts/CodexQuotaRail-win-x64.zip -Force
& makensis.exe /DVERSION=$env:RELEASE_VERSION packaging/nsis/CodexQuotaRail.nsi
dotnet tool restore
dotnet tool run sbom-tool generate -b artifacts -bc . -pn CodexQuotaRail -pv $env:RELEASE_VERSION -ps LingGe -nsb https://github.com/lingge66/codex-quota-rail
Get-FileHash artifacts/CodexQuotaRail-win-x64.zip, artifacts/CodexQuotaRail-Setup.exe -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } |
  Set-Content artifacts/SHA256SUMS.txt
~~~

The script fails if RELEASE_VERSION is missing, any test fails, NSIS is not exactly 3.12, an expected file is absent, or git status is dirty.

- [ ] **Step 5: Add CI and tag release workflows**

ci.yml runs on pull_request and push to main using windows-latest:

- setup .NET 8.0.x;
- restore locked dependencies;
- build Release with warnings as errors;
- test with coverage;
- upload test results only when tests fail.

release.yml runs only for tags matching v*:

- repeats build and tests;
- installs NSIS 3.12 and verifies makensis /VERSION;
- runs build-release.ps1;
- signs EXE/MSI only when signing secrets exist;
- always emits unsigned artifact names explicitly when no certificate exists;
- uploads ZIP, installer, SHA256SUMS, SBOM, and third-party notices to a draft GitHub Release.

Do not create or push the GitHub repository until the user explicitly authorizes that external action.

- [ ] **Step 6: Verify packaging locally**

Run:

~~~powershell
$env:RELEASE_VERSION = '0.1.0'
./scripts/build-release.ps1
Get-Content artifacts/SHA256SUMS.txt
~~~

Expected: ZIP, Setup EXE, SHA256SUMS.txt, and an SPDX SBOM exist. Install and uninstall in a disposable Windows user profile; confirm autostart is selected by default and settings can be preserved or removed.

- [ ] **Step 7: Commit**

~~~powershell
git add LICENSE README.md README.zh-CN.md SECURITY.md CONTRIBUTING.md CHANGELOG.md docs packaging scripts .github src/CodexQuotaRail.App/Updates tests/CodexQuotaRail.App.Tests
git commit -m "完善开源文档与发布流程"
~~~

## Task 11: Drive the finished application through real Codex

**Files:**
- Modify: docs/release-checklist.md
- Modify: CHANGELOG.md
- Create: artifacts/qa/README.md
- Create: artifacts/qa/*.png

**Interfaces:**
- Consumes: a real logged-in Windows Codex installation and release candidate.
- Produces: observable evidence that the product works on its matching surface.

- [ ] **Step 1: Run the complete verification suite**

~~~powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
$env:RELEASE_VERSION = '0.1.0-rc.1'
./scripts/build-release.ps1
~~~

Expected: all commands pass, no warnings, and release artifacts are regenerated from a clean tree.

- [ ] **Step 2: Verify real account data**

With Codex logged in through ChatGPT:

1. Start the release candidate.
2. Record the official Codex Usage page usedPercent values.
3. Verify the rail shows 100-usedPercent for primary and secondary.
4. Verify labels, duration, reset time, unlimited, and single-window behavior.
5. Save screenshots with account identifiers cropped or blurred.

Do not record tokens, raw JSON, email addresses, or full account IDs.

- [ ] **Step 3: Verify window behavior**

Check and record PASS/FAIL for:

- move and resize;
- maximize to 4px mode and restore to 22px;
- minimize and restore;
- focus loss to 52% opacity;
- another window covering Codex;
- two monitors with different DPI;
- 125%, 150%, and 200% scaling;
- sleep/resume;
- Codex restart;
- Explorer restart;
- single-instance activation.

- [ ] **Step 4: Verify failure behavior**

Use the fake server and controlled process termination to verify:

- last valid values remain with “几分钟前更新”;
- retry sequence follows 2, 5, 15, 30, 60 seconds;
- missing account requests login without collecting credentials;
- unsupported server requests Codex update;
- malformed data shows unavailable, never fake 100%;
- recovery performs a full refresh.

- [ ] **Step 5: Verify installation and privacy**

Install using Setup EXE, confirm the explicit default-selected autostart option, reboot or sign out/in, verify tray startup, uninstall while preserving settings, reinstall, then uninstall while removing settings. Inspect logs to prove no tokens, chats, project paths, emails, or account IDs are present.

- [ ] **Step 6: Record performance and visual evidence**

Run scripts/manual-qa.ps1. Record actual idle CPU, working set, cold start time, and quota update latency in docs/release-checklist.md. Save screenshots for healthy, notice, critical, exhausted, compact, stale, light, and dark states.

- [ ] **Step 7: Final documentation and commit**

Update CHANGELOG.md with 0.1.0-rc.1 behavior and known unsigned-publisher risk. Update artifacts/qa/README.md with machine, Windows build, Codex version, DPI, commands, and PASS/FAIL table.

~~~powershell
git add docs/release-checklist.md CHANGELOG.md artifacts/qa
git commit -m "记录首版真实环境验收结果"
git status --short
~~~

Expected: final status is clean. Do not tag, push, create a repository, or publish a release without separate explicit user authorization.
