# macOS 验收记录

## 当前自动化证据

- Swift 源文件已通过 Tree-sitter Swift 语法树扫描，未发现语法错误节点。
- Swift Package 清单、JSON、YAML、Info.plist 与 Bash 脚本已完成本地结构校验。
- Windows 基线 203 项 .NET 测试保持通过。
- `.github/workflows/macos-ci.yml` 会在 `macos-15` 上执行 Swift 测试、双架构构建、ad-hoc 签名、解包复验和 SHA-256 校验。

## 发布前必须补齐

- macOS runner 的真实 Swift 编译与测试结果；
- Apple Silicon 真机启动、辅助功能授权、22px/4px 切换、最小化、Space、失焦和窗口覆盖；
- Intel 真机或 Intel runner 冒烟；
- 真实 Codex App Server 额度一致性；
- 菜单栏、设置、登录项、领哥网站和详情自动收回的截图与操作记录。

Windows 环境不能替代 AppKit、Accessibility、ServiceManagement 与 Gatekeeper 的真实表面验收。未取得这些证据前，不把 macOS 版本描述为已发布候选。
