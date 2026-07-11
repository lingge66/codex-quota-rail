# 更新日志

本项目采用 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 结构与语义化版本。

## [Unreleased]

## [0.1.0-rc.1] - 2026-07-11

### 新增

- Windows x64 外侧贴边额度轨，支持 22px 双轨和 4px 紧凑模式。
- 官方 Codex App Server 额度读取、缓存、退避与自动恢复。
- 可用额度渐变、52% 失焦透明度、托盘菜单、可选开机自启、主题和减少动画。
- 睡眠/网络/Explorer/DPI 恢复、零遥测日志和手动 GitHub 更新检查。

### 已知限制

- 首发仅支持 Windows x64。
- 未配置发布证书时，安装器会显示“未知发布者”。
- WPF 版本在当前 QA 机器上的空闲工作集高于 80 MB，详见发布检查表。
- 当前候选版尚未配置 Windows 代码签名证书，安装器会显示“未知发布者”。
