---
name: EarthquakeWarning for ClassIsland
description: 原生 Avalonia 教室地震预警，以预计到达时间、当地震感和核验证据为中心。
colors:
  background: "#090C12"
  foreground: "#F3F5F8"
  caption: "#B4BECF"
  metadata: "#A4B3CB"
  publisher: "#9EABC1"
  button: "#242B37"
  map-background: "#111A27"
  divider: "#2B3445"
  evidence-status: "#FFBE72"
  weak-accent: "#65CBF3"
  weak-header: "#175577"
  weak-surface: "#102B3B"
  felt-accent: "#FFBD66"
  felt-header: "#885414"
  felt-surface: "#362813"
  damaging-accent: "#FF485A"
  damaging-header: "#C32335"
  damaging-surface: "#351019"
  compact-pending: "#B36A16"
  compact-confirmed: "#D92838"
  map-p-wave: "#53D3E8"
  map-s-wave: "#FF6B79"
  map-local: "#7CE9F5"
typography:
  display:
    fontFamily: "Bahnschrift, Microsoft YaHei UI"
    fontSize: "142px"
    fontWeight: 700
    lineHeight: "160px"
  headline:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "42px"
    fontWeight: 700
  place:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "32px"
    fontWeight: 600
  metric:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "58px"
    fontWeight: 700
  guidance:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "31px"
    fontWeight: 600
  caption:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "18px"
  action:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "18px"
rounded:
  map-label: "3px"
spacing:
  compact: "6px"
  small: "8px"
  medium: "12px"
  large: "20px"
  column-gap: "32px"
  page-edge: "36px"
components:
  alert-action:
    backgroundColor: "{colors.button}"
    textColor: "{colors.foreground}"
    typography: "{typography.action}"
    padding: "12px 22px"
  map-action:
    backgroundColor: "{colors.button}"
    textColor: "{colors.foreground}"
    padding: "6px 12px"
  map-panel:
    backgroundColor: "{colors.map-background}"
    height: "330px"
---

# Design System: EarthquakeWarning for ClassIsland

## Overview

原生 Avalonia 预警工作区，延续已确认的科技感、强烈警戒感和高信息密度。深色背景承载大号到达时间，震中、本地烈度、避险提示和接口证据共同解释当前预警。界面颜色随本地预估烈度切换，不能将所有事件都画成红色警报。

设置页沿用 ClassIsland / FluentAvalonia 的主题和原生设置控件；全屏警报使用本文件提取的专用配色与字号。这里记录当前实现，不声称已完成截图、真实设备或音频听感验证。

**Key Characteristics:**

- 到达时间和本地预估烈度具有最高信息权重。
- 蓝、琥珀、红三档颜色与无滴、单滴、双滴声音保持对应。
- 单源待核验、参数分歧、撤销、演练都有明确文字。
- 地图和接口证据是解释信息，传播波前明确标为估算示意。

## Colors

### Primary

三组动态警戒色来自 `Models/AlertSeverity.cs`；每组分别控制数字强调色、顶部警报带和底部建议区域。

| 本地预估烈度 | 文字标签 | 主题 | 固定离线报数节奏 |
| --- | --- | --- | --- |
| 小于 3 | 无感或微弱震感（预估） | weak 蓝色组 | 报数，无滴声 |
| 大于等于 3 且小于 6 | 有感无破坏（预估） | felt 琥珀色组 | 报数，单滴 |
| 大于等于 6 | 有感有破坏（预估） | damaging 红色组 | 报数，双滴 |

这些名称是界面的估算分档，不是对实际破坏程度的保证。接口核验状态独立于烈度主题，证据行的状态文本使用 `evidence-status`。CI 紧凑提示另有待核验与确认色，不使用全屏三档配色。

### Secondary

地图用青色 P 波、粉红色 S 波、红色震中点和浅青色本地点。虚线连接震中与本地，白色轮廓保证点位辨认；这些颜色不改变事件严重度。

### Neutral

近黑背景、浅色正文、较暗辅助文本和细分隔线组织层级。地图面板独立使用深蓝背景；按钮为中性深灰，不覆盖动态警报带。

**The Estimated Impact Rule.** 严重度颜色由本地预估烈度决定，不能按震级直接套用红色主题。

## Typography

正文和标题使用 Microsoft YaHei UI，回退 Segoe UI。倒计时使用 Bahnschrift，回退 Microsoft YaHei UI；不将它描述为已经保证等宽的字体。

字号是 Avalonia 逻辑单位；前置机器令牌以 px 形式记录同一数值。全屏画布随后统一缩放，因此它们不是屏幕物理像素保证。

- 倒计时为 display：最大层级，显示向上取整秒数、到达或破折号，秒单位单独排版（28）。
- 顶部标题为 headline，旁边核验说明较小（22）。
- 震中名称为 place，可换行，最大高度为 96。
- 烈度与震级共用 metric；烈度继承严重度强调色，震级使用正文色。
- 底部避险建议为 guidance，次要建议字号为 21。
- 图表标题与到达阶段为 24；接口名为 19；参数、详情为 16；发布机构和语音状态为 14。

不建立未实现的全局行高、字距或强制文本长度规范；设置页字号继续跟随宿主主题。

## Layout

全屏窗口无系统装饰、置顶，默认窗口尺寸为 1540 × 920，最小窗口尺寸为 800 × 500。内容固定在 1540 × 920 逻辑画布内，通过 `Viewbox Stretch="Uniform"` 等比缩放；当前没有断点换列或手机布局。

画布分为顶部 100、中部剩余空间、底部 98 三行。顶部左右内边距为 36、上下为 18。中部外边距为左/右 36、上 26，左列宽 500，左侧内容右边留 32 的列间空隙；右列占余宽。

左列按震情、到达时间、烈度/震级、距离/深度、事件/语音状态排列。右列顶部地图高 330，下方为接口核验标题和可纵向滚动证据列表。底部建议带左右内边距为 36、上下为 20。

设置页是一列纵向 `ScrollViewer`，分组内采用 `SettingsExpander` / `SettingsExpanderItem`。数值输入宽 150，来源选择宽 250，音色、地图地址、署名和命令输入宽 260；表单外观由宿主主题控制。

## Elevation & Depth

全屏界面以深浅表面、色带和细分隔线建立层次，没有自定义阴影或模糊背景。不要将未观察到的阴影令牌加入系统。设置页与按钮的悬停、焦点和禁用外观来自宿主原生控件主题，本插件没有另行规定颜色或动画。

## Shapes

警报带、主区、地图和建议带是直接拼接的矩形区域，不包装为圆角卡片。地图裁剪超出区域的瓦片与波前。离线地名以小圆角深色底衬和碰撞避让提高可读性，圆角见 `rounded.map-label`。按钮圆角保留 Avalonia 主题默认值，不虚构插件专属半径。

## Components

### 警报操作与状态

顶部始终提供“静音”和“关闭警报 · Esc”。静音是当前事件的单向操作，标签变为“已静音”；关闭和 Esc 关闭当前警报并停止其语音，监测继续，同一事件在当前处理周期不反复打开。它们使用原生按钮焦点、悬停和键盘行为，没有自定义动效。

演练标题持续显示“地震预警演练”。撤销显示“预警已撤销”，活动警报关闭并发出 CI 撤销提示。已经打开的事件出现参数分歧时，标题为“震情参数待复核”，阶段文字提示暂停到达播报，读数显示破折号，语音停止。尚未确认的事件只在 CI 显示待核验提示，不新开全屏。

### 地图

默认在线 OSM 瓦片，允许配置 HTTPS XYZ 地址和署名。已加载的在线底图提供随缩放变化的地名；在线服务决定其语言与可见标签，不能保证所有比例尺都显示每个区县或街道。未加载在线瓦片时保留 Natural Earth 离线陆地、省级边界和地名。

离线地名在 Z2–3 显示国家级、Z4–6 显示省级、Z7–18 显示城市级，按重要度排序并避免重叠；不是完整的区县/街道离线地图。图例显示当前在线/离线状态、层级和署名。

原生“放大”“缩小”按钮和滚轮改变缩放级别，左键拖动平移，“震中与本地”恢复适合两个位置的初始视图。绝对缩放范围为 Z2–18；切换事件重置视图。P/S 波圆环是按时间更新的传播示意，页面保留“非台站实测”等说明。

### 核验证据

逐行保留接口/数据流、吻合/分歧/已撤销状态、震级与深度、地点与报次、时间、发布机构。接口和发布机构分别呈现，标题旁持续说明“接口可能共享同一上游”。列表使用底部分隔线和纵向滚动，不能将行数或网关数量写成独立台站数。

### 设置与反馈

原生展开分组覆盖监测开关、来源选择、确认后全屏、接口连接详情、最近震情、自动定位/经纬度、提醒阈值、在线地图、语音及播报阈值、外部命令和演练。自动定位失败使用可关闭的 Warning InfoBar；核验机制说明使用不可关闭的 Informational InfoBar。

语音、命令关闭时，其相关阈值或命令字段禁用。提醒阈值与播报阈值分别配置；三档样式不改变这两个阈值的意义。演练按钮在运行时显示“停止演练”，演练屏蔽外部命令。

## Do's and Don'ts

### Do:

- **Do** 在到达时间、本地烈度和波前附近保留估算含义与当前状态文字。
- **Do** 保留静音、关闭、Esc、地图复位和证据滚动等现有操作。
- **Do** 将宿主设置主题与全屏专用样式分开记录。

### Don't:

- **Don't** 把同一网关的不同数据流或 HTTP/推送计算成两个独立确认接口。
- **Don't** 将估算波前、三档烈度名称或在线瓦片标签描述为台站实测或确定结果。
- **Don't** 添加快速闪烁、伪装饰读数，或声称此文档已证明截图与真实播放效果。
