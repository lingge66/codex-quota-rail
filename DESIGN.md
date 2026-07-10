# Codex Quota Rail Design System

## 1. Atmosphere & Identity

Codex Quota Rail 是一个安静、精密的开发者仪表，视觉上像 Codex 窗口自带的一条状态边。它不抢注意力，只在额度降低或不可用时提高信号强度。标志性语言是“双频谱边缘”：两条细轨用长度和颜色同时表达剩余额度，深色、低噪声、无装饰性渐变。

## 2. Color

### Palette

| Role | Token | Light | Dark | Usage |
|---|---|---:|---:|---|
| Rail surface | `Rail.Surface` | `#F2F8F8F6` | `#F210100E` | 22px 边缘轨背景，含 95% 不透明度 |
| Detail surface | `Rail.DetailSurface` | `#FFFDFDFB` | `#FF171714` | 悬停详情卡 |
| Track base | `Rail.TrackBase` | `#FFE3E5DF` | `#FF2A2A27` | 未填充轨道 |
| Text primary | `Rail.TextPrimary` | `#FF171815` | `#FFF4F4EF` | 百分比、主状态 |
| Text secondary | `Rail.TextSecondary` | `#FF5F625B` | `#FFA8AAA2` | 标签、重置时间 |
| Border subtle | `Rail.BorderSubtle` | `#FFD7DAD2` | `#FF32322E` | 外框和分隔 |
| Healthy | `Quota.Healthy` | `#FF73C84F` | `#FF91EF6B` | 51%–100% |
| Notice | `Quota.Notice` | `#FFE99B28` | `#FFFFC45B` | 21%–50% |
| Critical | `Quota.Critical` | `#FFE24E4A` | `#FFFF615D` | 0%–20% |
| Unavailable | `Quota.Unavailable` | `#FF777A73` | `#FF858780` | 无数据、不支持 |
| Focus | `Rail.Focus` | `#FF5B9E40` | `#FFC9EF63` | 高对比键盘焦点，仅设置面板使用 |

### Rules

- 额度填充颜色由 Core `QuotaColorScale` 连续计算，锨点必须等于上表 Healthy / Notice / Critical。
- 颜色不单独承担语义；22px 模式同时显示百分比或“无限 / 暂不可用”。
- 禁止紫蓝装饰渐变、彩色光晕和无语义高亮。

## 3. Typography

### Scale

| Level | Size | Weight | Line Height | Usage |
|---|---:|---:|---:|---|
| Rail value | 11px | 600 | 14px | 百分比、“无限” |
| Rail label | 10px | 600 | 12px | `CODEX`、5 小时 / 本周 |
| Rail metadata | 9px | 400 | 11px | 重置倒计时 |
| Detail title | 13px | 600 | 18px | 详情卡标题 |
| Detail body | 12px | 400 | 17px | 详情卡数据 |

### Font Stack

- Primary: `Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif`
- Mono: `Cascadia Mono, Consolas, monospace`
- 边缘轨最多使用两种字体；`CODEX` 可用 Mono，其余使用 Primary。

## 4. Spacing & Layout

### Base Unit

常规间距基于 4px；22px 高度内允许声明过的 2px 半单位做光学对齐。

| Token | Value | Usage |
|---|---:|---|
| `Space.Half` | 2px | 轨道上下光学修正 |
| `Space.1` | 4px | 轨道与文本紧密间距 |
| `Space.2` | 8px | 双轨间隔、左右内边距 |
| `Space.3` | 12px | 详情卡内边距 |
| `Space.4` | 16px | 详情区块间隔 |

### Geometry

- 外侧边缘轨：高 22px，宽度跟随 Codex 窗口。
- 标题栏紧凑轨：高 4px，不显示文本。
- 外侧轨圆角：8px；紧凑轨圆角：0px。
- 轨道高度：4px；最小可见填充宽度：1px（仅剩余额度 > 0 时）。
- 窄宽度降级顺序：先隐藏重置时间，再隐藏轨道文字，始终保留轨道本身。

## 5. Components

### Rail Window

- **Structure**: non-activating transparent WPF Window → root Border → brand / quota tracks / reset status。
- **Variants**: `ExternalRail` 22px、`CompactTitleBar` 4px、`Hidden`。
- **Spacing**: `Space.Half`、`Space.1`、`Space.2`。
- **States**: focused 100% opacity、unfocused 52%、unavailable、unlimited、exhausted。
- **Accessibility**: 不获取键盘焦点；颜色外保留文本语义；详情卡不改变 Tab 顺序。
- **Motion**: 只动画 opacity / transform；减少动画时全部静态。

### Quota Track

- **Structure**: label → base track → proportional fill → value / reset metadata。
- **Variants**: primary、secondary、single centered、unlimited、unavailable。
- **States**: healthy、notice、critical、exhausted。
- **Accessibility**: 百分比与文本状态是主语义，颜色是辅助。

### Hover Detail

- **Structure**: non-focusable Popup → bordered detail surface → per-window rows → update time。
- **States**: hover delay 250ms、pointer leave close、no keyboard activation。
- **Accessibility**: 不抢占 Codex 键盘焦点，内容与主轨文案一致。

## 6. Motion & Interaction

| Type | Duration | Easing | Usage |
|---|---:|---|---|
| Focus opacity | 180ms | ease-out | 100% ↔ 52% |
| Hover delay | 250ms | linear delay | 打开详情卡 |
| Threshold shimmer | 1200ms | ease-in-out | 首次跨入 50% / 20% |
| Exhausted marquee | 12s | linear | 仅 0% 循环 |

### Rules

- 只动画 `Opacity` 和 `TranslateTransform.X`，不动画 Width / Height / Left / Top。
- `ReduceMotion=true` 时 shimmer、呼吸和 marquee 全部关闭，保留静态红色和文字。
- 正常额度不循环动画。

## 7. Depth & Surface

### Strategy: borders-only

- 边缘轨使用 1px `Rail.BorderSubtle`，不使用阴影。
- 详情卡使用同一边框和更高一级的 `Rail.DetailSurface`，不添加玻璃模糊或外发光。
- 层级来自表面色差、边框和空间，不来自装饰效果。
