---
name: EarthquakeWarning for ClassIsland
description: 教室避险优先的原生 Avalonia 地震预警中心看板，常显本地影响、地图与接收证据。
colors:
  background: "#090C12"
  foreground: "#F3F5F8"
  caption: "#B4BECF"
  metadata: "#A4B3CB"
  publisher: "#9EABC1"
  button: "#242B37"
  button-checked: "#344051"
  panel: "#111722"
  map-background: "#111A27"
  divider: "#2B3445"
  evidence-match: "#7CE9F5"
  evidence-pending: "#FFBD66"
  safety-secondary: "#E2E8F2"
  weak-accent: "#65CBF3"
  weak-header: "#175577"
  weak-surface: "#102B3B"
  felt-accent: "#FFBD66"
  felt-header: "#885414"
  felt-surface: "#362813"
  damaging-accent: "#FF485A"
  damaging-header: "#C32335"
  damaging-surface: "#351019"
  cancelled-accent: "#CED8E8"
  cancelled-header: "#415064"
  cancelled-surface: "#1C2634"
  compact-pending: "#B36A16"
  map-p-wave: "#53D3E8"
  map-s-wave: "#FF6B79"
  map-local: "#7CE9F5"
typography:
  display:
    fontFamily: "Bahnschrift, Microsoft YaHei UI"
    fontSize: "154px"
    fontWeight: 700
    lineHeight: "184px"
  display-arrived:
    fontFamily: "Bahnschrift, Microsoft YaHei UI"
    fontSize: "86px"
    fontWeight: 700
    lineHeight: "184px"
  headline:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "36px"
    fontWeight: 700
  place:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "29px"
    fontWeight: 600
  intensity:
    fontFamily: "Bahnschrift, Microsoft YaHei UI"
    fontSize: "75px"
    fontWeight: 700
  metric:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "32px"
    fontWeight: 600
  guidance:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "31px"
    fontWeight: 700
  safety-step:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "39px"
    fontWeight: 700
  caption:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "17px"
  action:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "18px"
rounded:
  map-label: "3px"
spacing:
  compact: "6px"
  small: "8px"
  control-gap: "12px"
  panel-inset: "16px"
  content-inset: "20px"
  column-gap: "22px"
  page-edge: "28px"
components:
  alert-action:
    backgroundColor: "{colors.button}"
    textColor: "{colors.foreground}"
    typography: "{typography.action}"
    padding: "10px 20px"
  map-action:
    backgroundColor: "{colors.button}"
    textColor: "{colors.foreground}"
    padding: "5px 10px"
  evidence-toggle:
    backgroundColor: "{colors.button}"
    textColor: "{colors.foreground}"
    padding: "7px 12px"
  evidence-toggle-checked:
    backgroundColor: "{colors.button-checked}"
    textColor: "{colors.foreground}"
    padding: "7px 12px"
  local-panel:
    backgroundColor: "{colors.panel}"
    padding: "16px 20px"
    width: "342px"
  evidence-panel:
    backgroundColor: "{colors.panel}"
    padding: "16px"
    width: "282px"
  map-panel:
    backgroundColor: "{colors.map-background}"
---

# Design System: EarthquakeWarning for ClassIsland

## Overview

**Creative North Star: "教室避险优先的地震预警中心看板"**

原生 Avalonia 三列工作区：左侧优先给教室用户读取本地影响和预计到达时间，中央保留独立大地图，右侧常显接收证据，底部持续给出行动。科技感来自地图密度、稳定分区和信息层级；强烈警戒感由状态颜色与明确动作产生，不依赖闪烁或装饰仪表。

设置页沿用 ClassIsland / FluentAvalonia 的主题和原生设置控件。全屏使用专用深色表面、直线面板和三档警戒色；当预测不可用时，状态文字和参考位置替代貌似有效的预测数字与波圈。此文档记录已批准实现，原生无头渲染不能证明真实教室阅读、多屏投放或真实地震表现。

**Key Characteristics:**

- 本地到达时间、预估烈度与底部防护动作具有最高信息权重。
- 中央地图独立伸展，接收证据常显，图例和操作不遮住地图。
- 蓝、琥珀、红三档颜色与无滴、单滴、双滴声音保持对应。
- 待核验、分歧、过期、模型不支持、撤销与演练有明确身份和状态文字。
- 地图和接口证据是解释信息，传播波前明确标为估算示意。

## Colors

### Primary

三组动态警戒色来自 `Models/AlertSeverity.cs`，呈现状态来自 `Models/AlertViewModel.cs`；每组分别控制数字强调色、顶部警报带和底部行动区。

| 本地预估烈度 | 文字标签 | 主题 | 固定离线报数节奏 |
| --- | --- | --- | --- |
| 小于 3 | 预计无感或微弱震感 | weak 蓝色组 | 报数，无滴声 |
| 大于等于 3 且小于 6 | 预计有明显震感 | felt 琥珀色组 | 报数，单滴 |
| 大于等于 6 | 预计强烈震感 | damaging 红色组 | 报数，双滴 |

这些名称是界面的估算分档，不是对实际破坏程度的保证。复核或模型不支持时以 felt 琥珀组呈现，撤销使用 cancelled 灰色组；二者均不显示可用的本地预测数值。证据“吻合”使用 `evidence-match`，其余状态使用 `evidence-pending`，并始终保留状态文字。CI 紧凑提示在未确认时使用 `compact-pending`，确认时使用相应严重度的 header 色。

### Secondary

地图用青色 P 波、粉红色 S 波、红色震中点和浅青色本地点。虚线连接震中与本地，白色轮廓保证点位辨认；这些颜色不改变事件严重度。

### Neutral

近黑背景、浅色正文、较暗辅助文本和细分隔线组织层级。左/右面板使用 `panel`，中央地图独立使用 `map-background`。按钮为中性深灰，详情切换的选中表面稍亮；底部次要说明使用 `safety-secondary`。

**The Estimated Impact Rule.** 严重度颜色由本地预估烈度决定，不能按震级直接套用红色主题。

## Typography

正文和标题使用 Microsoft YaHei UI，回退 Segoe UI。倒计时和本地烈度使用 Bahnschrift，回退 Microsoft YaHei UI；不将它描述为已经保证等宽的字体。

字号是 Avalonia 逻辑单位；前置机器令牌以 px 形式记录同一数值。全屏画布随后统一缩放，因此它们不是屏幕物理像素保证。

- 倒计时为 display，显示向上取整秒数或破折号；到达文字使用 display-arrived。两者位于高 190 的读数行，秒单位单独排版（27）。
- 顶部标题为 headline，旁边核验说明较小（20）；演练徽标为 18。
- 中央震中名称为 place，可换行，最大高度为 78，超出省略，悬停显示完整地名。
- 本地烈度单独使用 intensity，影响文字为 22；震级、距离、深度使用 metric，不与烈度争夺字号层级。
- 底部建议为 guidance，三步行动标题为 safety-step，行动详情为 16，次要建议为 17。
- 到达阶段为 22，接收证据标题为 23；接口名与状态为 16，参数为 15，展开详情为 14，发布机构与页脚为 13；地图外图例为 12。

不建立未实现的全局行高、字距或强制文本长度规范；设置页字号继续跟随宿主主题。

## Layout

全屏窗口无系统装饰、置顶，默认窗口和设计基准为 1600 × 900，最小窗口尺寸为 800 × 500。内容经过 `Viewbox Stretch="Uniform"` 等比缩放。`FitCanvas` 使用 `min(实际宽 / 1600, 实际高 / 900)` 作为缩放因子，再将画布宽高设为实际宽高除以该因子；宽屏扩展中央地图宽度，较高屏扩展中部高度，不把所有比例固定塞入留边的 16:9 画布。当前没有换列断点或手机布局。

画布分为顶部 88、中部剩余空间、行动带 136、页脚 34 四行。顶部左右内边距为 28、上下为 14。中部外边距为左/右 28、上 18、下 16，列间距为 22；左栏宽 342，中央伸展，右栏宽 282。

左栏按阶段、到达时间、预估说明、本地烈度、震级/距离/深度、经纬度排列，内边距为左/右 20、上/下 16。中央上方显示震中地名与发震时间，地图占剩余空间且最小高度为 250；图例、署名和地图按钮位于地图下方。右栏内边距为 16，接收路径与证据数量、详情开关、可滚动证据行及共享上游说明常驻独立区域。

行动带内边距为左/右 28、上/下 18，左侧建议区宽 390，右侧均分三步动作；图标和短动词共同支持远距离辨认。页脚显示模型估算提示与北京时间。预测状态变化不挤走中央地图和右侧证据。

设置页是一列纵向 `ScrollViewer`，分组内采用 `SettingsExpander` / `SettingsExpanderItem`。数值输入宽 150，来源选择宽 250，音色、地图地址、署名和命令输入宽 260；表单外观由宿主主题控制。

## Elevation & Depth

全屏界面以深浅表面、色带和细分隔线建立层次，没有自定义阴影或模糊背景。不要将未观察到的阴影令牌加入系统。按钮增加正文色的 2 单位可见焦点边框；详情切换有独立选中表面和边框。其余悬停、焦点与禁用行为延续原生控件主题，没有自定义动画。设置页继续使用宿主主题。

## Shapes

警报带、主区、地图和建议带是直接拼接的矩形区域，不包装为圆角卡片。地图裁剪超出区域的瓦片与波前。离线地名以小圆角深色底衬和碰撞避让提高可读性，圆角见 `rounded.map-label`。按钮圆角保留 Avalonia 主题默认值，不虚构插件专属半径。

## Components

### 警报操作与状态

顶部始终提供“静音”和“关闭 · Esc”。M 静音当前事件，按钮标签变为“已静音”；关闭和 Esc 关闭当前警报并停止其语音，监测继续，同一事件在当前处理周期不反复打开。静音说明在提示气泡中，不在全屏展示语音实现或音色配置。

演练以独立描边徽标持续显示“演练 · 模拟数据”，不依赖标题替代事件状态。复核状态标题为“震情正在复核”；模型不支持时显示“数据不足，暂不估算”，其他暂停状态显示“正在复核，倒计时暂停”。本地烈度和倒计时变为破折号，影响描述为“当地影响待核验”，不保留旧预测读数。首次进入暂停状态会自动展开报文详情；尚未确认的真实事件只在 CI 提示，不因此新开全屏。

预计剩余十秒以内显示“预计震动即将到达”；归零后切换为“预计震动已到达”和“到达”，继续提示就近避险，不将倒计时结束当成安全。撤销状态以灰色及“预警已撤销”呈现，提示撤销不代表现场一定安全；正常 provider 流程关闭活动警报并发出 CI 撤销提示，独立渲染的撤销状态不证明该屏必然停留。

### 底部三步行动

有感及强烈状态在到达前提示“立即就近避险”“不要等待倒计时结束”，到达后提示“继续就近避险”，震动结束后听从老师指挥有序撤离。伏地、遮挡、抓牢三步配以几何动作图标；微弱状态改为镇静、关注、准备。暂停或撤销仍保留防护步骤与相应状态说明，不显示空白行动区。

### 地图

默认在线 OSM 瓦片，允许配置 HTTPS XYZ 地址和署名。已加载的在线底图提供随缩放变化的地名；在线服务决定其语言与可见标签，不能保证所有比例尺都显示每个区县或街道。未加载在线瓦片时保留 Natural Earth 离线陆地、省级边界和地名。

离线地名在 Z2–3 显示国家级、Z4–6 显示省级、Z7–18 显示城市级，按重要度排序并避免重叠；不是完整的区县/街道离线地图。地图外图例显示传播示意、当前在线/离线状态、缩放和署名，不覆盖地图。

原生“放大”“缩小”按钮和滚轮改变缩放级别，左键拖动平移，“复位”恢复适合震中与本地的初始视图，悬停提示该含义。绝对缩放范围为 Z2–18；切换事件重置视图。仅在模型支持且事件已确认时绘制 P/S 传播圆环；其他状态移除波圈，震中点改用参考色并标“待核验震中”，图例改为“预测暂停 · 仅显示参考位置”。

### 核验证据

右栏始终显示接口/数据流、吻合/待核验/分歧/已撤销状态、震级与深度，不因折叠详情而隐藏证据行。D 或“报文详情 · D”展开地点、报次、发布机构与发震时间，再次操作显示“收起详情 · D”。新事件默认折叠，若进入分歧、过期、证据不足或模型不支持等暂停状态则自动展开；用户仍可手动收起。

摘要使用“接收路径”和“条证据”，底部持续说明“多个接口可能共享上游，不代表独立台站观测”。列表使用底部分隔线和纵向滚动，不能将行数或网关数量写成独立台站数。

### 设置与反馈

原生展开分组覆盖监测开关、来源选择、确认后全屏、接口连接详情、最近震情、自动定位/经纬度、提醒阈值、在线地图、语音及播报阈值、外部命令和演练。自动定位失败使用可关闭的 Warning InfoBar；核验机制说明使用不可关闭的 Informational InfoBar。

语音、命令关闭时，其相关阈值或命令字段禁用。提醒阈值与播报阈值分别配置；三档样式不改变这两个阈值的意义。演练按钮在运行时显示“停止演练”，演练屏蔽外部命令。

## Do's and Don'ts

### Do:

- **Do** 在到达时间、本地烈度和波前附近保留估算含义与当前状态文字。
- **Do** 保持中央地图和右侧接收证据独立可见，图例与操作放在地图外。
- **Do** 保留 D 详情、M 静音、Esc 关闭、地图复位和证据滚动等现有操作。
- **Do** 在暂停预测时移除有效数字和波圈，在预计到达后继续给出防护动作。
- **Do** 将宿主设置主题与全屏专用样式分开记录。

### Don't:

- **Don't** 把同一网关的不同数据流或 HTTP/推送计算成两个独立确认接口。
- **Don't** 将估算波前、三档影响名称或在线瓦片标签描述为台站实测或确定结果。
- **Don't** 添加快速闪烁、虚构台站、可信度百分比、伪装饰读数或预测结束时间。
- **Don't** 将原生无头截图与回归结果描述为真实教室、真实多屏或真实地震验收。
