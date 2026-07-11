# macOS 自定义搭建

macOS 版没有私有服务或运行时第三方依赖。Fork 仓库后可直接修改配置、资源和 Swift 扩展点。

## 品牌配置

编辑 `macos/Config/Branding.xcconfig`：

- `PRODUCT_NAME`：应用名称；
- `PRODUCT_BUNDLE_IDENTIFIER`：自己的反向域名标识；
- `LINGGE_WEBSITE_URL`：默认 HTTPS 网站；
- `UPDATE_REPOSITORY`：更新检查仓库；
- `DEFAULT_TARGET_BUNDLE_ID`：默认跟随应用。

发布脚本读取 `macos/App/Resources/Info.plist`。换品牌时同步修改显示名称、Bundle Identifier 与版权信息。替换 `src/CodexQuotaRail.App/Assets/LingGe.ico` 后，macOS 构建脚本会生成新的 `.icns`。

## 运行时设置

用户可在“设置…”中调整主题、减少动画、透明度、普通/紧凑轨高度、登录项、个人网站和目标 Bundle Identifier。自定义网站必须是绝对 HTTPS 地址。

设置保存在 `~/Library/Application Support/CodexQuotaRail/settings.json`，带 `schemaVersion`。无效范围、非 HTTPS 地址和空目标列表会恢复安全默认值；损坏文件会被隔离，不会静默覆盖。

## 主题

默认主题位于 `macos/Resources/Themes/`。颜色格式固定为 `#RRGGBBAA`。主题只包含数据，不允许脚本、表达式、远程图片或动态库。

## 构建

```bash
swift test --package-path macos/Packages/CodexQuotaKit
bash macos/Scripts/build-universal.sh 0.1.0-custom.1
```

构建脚本分别生成 `arm64` 与 `x86_64`，使用 `lipo` 合并、执行 ad-hoc codesign、验证 Info.plist，并通过 `ditto` 生成 ZIP 和 SHA-256。

GitHub Fork 无需 Secret 即可运行 `.github/workflows/macos-ci.yml`。正式面向普通用户大规模分发时，建议增加自己的 Apple Developer ID 签名与公证步骤，但不得移除无证书开源构建路径。

## 源码扩展

- `QuotaDataProviding`：接入其他额度数据来源；
- `TargetWindowTracking`：跟随其他原生应用；
- `RailView` 与 `QuotaDetailsView`：实现其他显示皮肤；
- `QuotaTheme`：增加内置主题。

社区扩展应作为源码 Target 或 Fork 编译，不从用户目录动态执行任意代码。
