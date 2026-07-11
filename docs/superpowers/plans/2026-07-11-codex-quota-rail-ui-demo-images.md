# Codex Quota Rail UI Demo Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 生成并交付 4 张真实桌面风格的 Codex Quota Rail UI 演示图，同时在 GitHub 中英文 README 中展示它们。

**Architecture:** 每张成品使用独立的内置图片生成调用，真实 Windows Codex 截图作为 UI 结构参考，领哥 LOGO 只作为品牌参考。生成结果逐张目视验收后保存到 `docs/assets/demo/`，再由 README 使用相对路径组成横图与方图展示区。

**Tech Stack:** 内置图片生成工具、PNG、GitHub Markdown/HTML、PowerShell 图像元数据检查、Git。

## Global Constraints

- 共交付 4 张 PNG：两张横图、两张 1:1 方图。
- 普通窗口额度轨必须在 Codex 顶部外侧，高度约 22px；最大化场景只能显示顶部约 4px 紧凑轨。
- 轨道长度和颜色表达剩余可用额度：51%–100% 绿色，21%–50% 黄至橙色，1%–20% 红色。
- 详情卡只出现在点击详情图；其他三张不得显示详情卡。
- 不添加 OpenAI 官方合作暗示、虚构功能、水印或无意义 UI 字符。
- 所有项目引用的最终图片必须保存在 `docs/assets/demo/`，不能只存在于默认生成目录。

---

### Task 1: 生成普通窗口横版主图

**Files:**
- Create: `docs/assets/demo/codex-quota-rail-normal-wide.png`

**Interfaces:**
- Consumes: 真实 Windows Codex 全窗口截图、领哥 LOGO、设计规格中的 90%/68% 状态。
- Produces: README 主视觉使用的横版 PNG。

- [ ] **Step 1: 调用内置图片生成工具**

使用 `ui-mockup` 提示词，要求深色 Windows Codex 普通窗口、窗口外侧 22px 双轨、`5 小时 可用 90%` 与 `本周 可用 68%` 两条绿色额度、克制暗绿环境光、无详情卡、无水印。

- [ ] **Step 2: 检查生成结果**

使用本地图片查看器确认窗口完整、额度轨在外侧、两条轨道接近满长、菜单与内容未被遮挡；若位置、颜色或中文错误，只针对该问题重新生成一次。

- [ ] **Step 3: 保存最终 PNG**

把选定结果复制为 `docs/assets/demo/codex-quota-rail-normal-wide.png`，使用 PowerShell 确认格式为 PNG、宽度大于高度且文件可解码。

### Task 2: 生成点击详情横图

**Files:**
- Create: `docs/assets/demo/codex-quota-rail-details-wide.png`

**Interfaces:**
- Consumes: 真实 Windows 详情卡截图、设计规格中的 78%/42% 状态。
- Produces: README 功能说明使用的横版 PNG。

- [ ] **Step 1: 调用内置图片生成工具**

使用 `ui-mockup` 提示词，要求深色 Windows Codex 普通窗口、外侧 22px 双轨、点击后从额度轨下方展开 `Codex 可用额度` 详情卡、78% 绿色与 42% 黄色、重置时间和最近更新时间、无水印。

- [ ] **Step 2: 检查生成结果**

确认详情卡宽度克制、没有占满屏幕、没有抢占标题栏、只出现一个详情卡；检查百分比、颜色和重置文字是否自洽。

- [ ] **Step 3: 保存最终 PNG**

把选定结果复制为 `docs/assets/demo/codex-quota-rail-details-wide.png`，确认格式为 PNG、宽度大于高度且文件可解码。

### Task 3: 生成低额度方图

**Files:**
- Create: `docs/assets/demo/codex-quota-rail-low-quota-square.png`

**Interfaces:**
- Consumes: 普通窗口额度轨结构、设计规格中的 18% 红色状态。
- Produces: X（Twitter）与 QQ 社群使用的 1:1 PNG。

- [ ] **Step 1: 调用内置图片生成工具**

使用 `ui-mockup` 提示词，要求 1:1 构图、Codex 普通窗口局部、外侧 22px 双轨、短周期 `可用 18%` 为短红轨、长周期保持绿色或黄色、无详情卡、无警报弹窗、无水印。

- [ ] **Step 2: 检查生成结果**

确认红色短轨只占约 18%，文字明确是“可用”而非“已用”，并且额度轨仍依附于 Codex 窗口外侧。

- [ ] **Step 3: 保存最终 PNG**

把选定结果复制为 `docs/assets/demo/codex-quota-rail-low-quota-square.png`，确认格式为 PNG、宽高相等且文件可解码。

### Task 4: 生成最大化紧凑方图

**Files:**
- Create: `docs/assets/demo/codex-quota-rail-compact-square.png`

**Interfaces:**
- Consumes: Windows Codex 最大化布局、4px 紧凑轨产品规则。
- Produces: X（Twitter）与 QQ 社群使用的 1:1 PNG。

- [ ] **Step 1: 调用内置图片生成工具**

使用 `ui-mockup` 提示词，要求 1:1 构图、最大化 Codex 窗口、屏幕最顶部约 4px 的绿色与黄色双额度紧凑线、没有标签、没有 22px 轨、没有详情卡、无水印。

- [ ] **Step 2: 检查生成结果**

确认画面表达最大化状态，紧凑线不遮挡标题栏和菜单，且只保留绿色与黄色细线。

- [ ] **Step 3: 保存最终 PNG**

把选定结果复制为 `docs/assets/demo/codex-quota-rail-compact-square.png`，确认格式为 PNG、宽高相等且文件可解码。

### Task 5: 集成 README 并完成交付验证

**Files:**
- Modify: `README.zh-CN.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Verify: `docs/assets/demo/*.png`

**Interfaces:**
- Consumes: Task 1–4 的 4 张最终 PNG。
- Produces: GitHub 可直接浏览的双语 UI 演示区和完整交付记录。

- [ ] **Step 1: 在双语 README 中加入演示区**

中文标题使用 `## UI 演示`，英文标题使用 `## UI demo`。使用两行两列 HTML 表格或居中图片块：第一行展示两张横图，第二行展示两张方图；所有 `src` 使用 `docs/assets/demo/<filename>.png` 相对路径，并提供准确的中英文 `alt` 文本。

- [ ] **Step 2: 更新更新日志**

在 `CHANGELOG.md` 的 `[Unreleased]` 文档条目中记录新增 4 张 Windows 额度轨演示图和双语 README 展示区。

- [ ] **Step 3: 验证文件与 Markdown**

运行 PowerShell 图像解码检查，预期 4 个文件均为 PNG；运行 `git diff --check`，预期退出码 0；调用 GitHub Markdown 渲染接口检查 README，预期 4 个相对图片路径全部出现在渲染结果中。

- [ ] **Step 4: 逐张打开最终成品**

使用本地图片查看器以原始细节打开 4 张图片，逐项对照设计规格的验收清单；任何一项失败都回到对应生成任务修正。

- [ ] **Step 5: 提交并推送**

仅暂存 4 张成品、双语 README、更新日志和本计划，提交信息使用 `添加额度UI演示效果图`。确认提交后工作区干净，再将 `main` 与 `feature/codex-quota-rail` 同步到同一提交并验证 GitHub 在线图片返回 HTTP 200。
