# AGENTS.md — 司南 · Windows 桌面端

> 本文件只放 **Windows 桌面端（`desktop/`）** 的规则。项目定位、双端架构、数据模型红线、
> 业务口径、脱敏与编码铁律、版本号、待确认事项见根目录 `../AGENTS.md`，这里不重复。
>
> **只在改动 `desktop/` 时读本文件；改手机端请看 `../mobile/AGENTS.md`。**

## 1. 技术栈与目录

| 层 | 选型 | 约束 |
|---|---|---|
| 运行时 | **.NET 10 + WPF**（`net10.0-windows`） | 本机只装了 .NET 10 SDK，不要降到 net8 以外的框架 |
| 架构 | **MVVM**：View / ViewModel / Service / Repository | 不引重型框架；自写 `ObservableObject`/`RelayCommand` 或最小 Toolkit |
| 数据库 | **SQLite**（Microsoft.Data.Sqlite，WAL） | 单文件；`shared/db/schema.sql` 是唯一 DDL 真源 |
| Excel | **NPOI** | 必须同时支持 `.xls`（BIFF8）与 `.xlsx`；不要用只写 xlsx 的库 |
| JSON | System.Text.Json | 同步协议与配置统一用它 |
| 日志 | 自写滚动文件日志 | 不引 log4net；日志目录与数据库同根 |
| 主题 | 双 ResourceDictionary | 只允许引用主题键，禁止在页面里写死色值 |

```
desktop/
├─ Sinan.sln
├─ src/
│  ├─ Directory.Build.props  ← 司南工程的编译与版本约定（只作用于 src/）
│  ├─ Sinan.Core/            ← 领域模型、枚举、DTO、纯规则（无 UI / 无数据库依赖）
│  ├─ Sinan.Data/            ← SQLite 仓储、种子、Excel 通道（NPOI）
│  └─ Sinan.App/             ← WPF：App/Views/ViewModels/Services/Controls/Themes/Assets
├─ ThirdParty/               ← Windows 专用第三方依赖（git submodule）
│  ├─ Directory.Build.props  ← 令 Kaleidoscope.UI 在司南只产 net10.0-windows
│  └─ Kaleidoscope/          ← 公司级 WPF 复用库；Sinan.App 只引 Kaleidoscope.UI
└─ tests/Sinan.Tests/        ← xUnit：往返、统计口径、同步冲突
```

### 1.1 版本号（桌面端）

- 唯一来源：`desktop/src/Directory.Build.props` 的 `<Version>0.0.8</Version>`。
  改版本只改这一处；`Sinan.App` 通过程序集版本读取，界面由 `AppInfo` 显示 `v0.0.8`，
  不要在 XAML/代码里另写版本字符串。
- 升级节奏遵循根总纲：**默认只升 patch**（`0.0.1 → 0.0.2 → 0.0.3 → 0.0.4`），
  `0.0.9` 之后是 `0.0.10`，不自动进位；只有用户明确要求才动 minor/major。
- 只改桌面端时只升桌面版本，手机端版本保持不动；两端不要求同号。
- 改完 `dotnet build` 后，用程序集版本或界面 `AppInfo.VersionLabel` 核对三段版本。

## 2. MVVM 约定

- **View 只留 glue**：密码框回传、窗口拖动、关闭确认等；业务判断一律在 ViewModel。
- **ViewModel 不 `new Window`**：开窗/弹框走 `IDialogService`，导航走 `INavigationService`。
- **Repository 不出现在 View 里**：View 的 `DataContext` 只能是 ViewModel。
- **命令统一 `RelayCommand` / `AsyncRelayCommand`**，禁止在 code-behind 写 `async void` 业务。
- **线程**：数据库/Excel/HTTP 一律 `Task.Run` 或异步 API；回到 UI 线程用
  `Application.Current.Dispatcher`；禁止在 UI 线程做超过 50ms 的 IO。
- **错误**：ViewModel 捕获业务异常 → 用户可读提示 + 日志；不要弹原始堆栈。
- **可测试**：统计口径、状态推导、冲突判定放在 Core/Data，纯函数优先。

## 3. WPF 控件复用（Kaleidoscope）

- **先查再写**：新建控件/样式/转换器前，先查
  `desktop/ThirdParty/Kaleidoscope/Kaleidoscope.UI/README.md` 的「控件复用索引」；
  库内有对应物直接用，有近似物 `BasedOn` 再造。确无对应物才自绘，且必须走本主题 Token、
  不写死色值。
- **沉淀回写**：司南沉淀出的通用控件**回写到 Kaleidoscope 子模块**（在其独立仓库提交），
  司南仓库只升级子模块指针。
- **胶囊圆角用 `ui:KxCapsuleBorder`**（库 V2.4.1，替换 `<Border>` 标签即可，构造即绑半高；
  竖向窄条用 `Axis="Width"`），不要手写 `CornerRadius="99"`、也不要手抄半高 Binding。
  开发期圆角问题由 `KxRadiusAudit` 自动兜住（见 4.1 第 1 条），不靠记性。
- Windows 专用依赖一律进 `desktop/ThirdParty/`，根目录不出现只能被 Windows 使用的目录
  或构建属性文件。

## 4. 界面规范（来自设计稿 C）

- 桌面基准 `1440×900`；窗口最小 `1280×800`，内容可伸缩。
- 主色 `#D97706` / `#F59E0B`；强调青 `#0F766E`；浅底 `#FBF6EE`；暗底 `#1C140D`。
- 圆角三档：卡片 20（`Radius.Card`）、卡内 16（`Radius.CardInset`，同心=外半径−边距 4）、
  胶囊（设计稿 `--btnradius:99`，WPF 用库内 `KxCapsuleBorder`，一行标签自动半高）。
- 字体：`Microsoft YaHei UI`；数字用 `Consolas`。
- 一屏只保留 **1 个主操作**；顶栏可点 ≤4；侧栏 6 项；按钮 ≤10。
- 侧栏固定 6 项：项目总览 / 工作台 / 问题台账 / 数据交换 / 统计报表 / 系统设置。
- 状态色语义：绿=正常/已关闭，黄=临期/待确认，红=逾期/倒挂，青=信息/进行中。
- 所有列表必须有空状态；所有耗时操作必须有加载态；错误就地展开，不弹二级窗。
- 玻璃态只作用于页面内容区，不污染侧栏外壳；表格、`.card.inset` 保持不透明。
- 设计稿里的演示照片用占位图 + "演示"角标，不伪装成真实现场照片。

**玻璃态落地口径**：WPF 没有 CSS `backdrop-filter`，用三层近似，禁止用 `BlurEffect`
直接糊卡片内容（会把文字一起糊掉）：

1. 页面底层铺 3 个 `RadialGradientBrush` 柔光斑（琥珀 / 深青 / 暖橙）；
2. 卡片用半透明渐变底 + 1px 亮边 + 顶缘内高光；
3. 外层 `DropShadowEffect` 做暖色柔影；暗色主题换深棕半透明底 + 18% 白边。

`.card.inset`（卡套卡）与表格内部保持不透明，保证可读性。

### 4.1 圆角与裁剪

> 2026-11-24 实机整改沉淀：AIris 已踩过一遍，司南复现同一坑，以下每一条都是实测结论。

1. **胶囊不许写 `CornerRadius="99"`，也不要手抄半高 Binding**。WPF 会把横向/纵向半径**分别**
   钳到 `宽/2`、`高/2`：宽按钮上 99 变"横 99 / 纵 半高"，端头画成椭圆、收弧过急，看起来就是
   "尖的、橄榄球"。CSS 的 `border-radius:99px` 则会统一缩到 `min(宽,高)/2` 的正圆，所以设计稿
   99 不能照抄成数字。统一用库内 `KxCapsuleBorder`（替换 `<Border>` 标签即可，构造即绑
   `ActualHeight/2`；竖向窄条用 `Axis="Width"`），**不再靠"记得写对"**：Kaleidoscope.UI V2.4.1
   已把根因固化成控件，并提供 `KxRadiusAudit` 运行时自检——司南在 Debug 构建的首窗渲染与每次
   页面切换后自动扫描，命中写日志 WARN（`[圆角自检·…]`），开发期就能发现，不必等用户说"太尖"。
2. **"胶囊"是语义类别，不是某一组具体控件**。按钮、输入框、下拉框、Chip、导航项、细进度条
   都是胶囊；新增同类控件直接用 `KxCapsuleBorder`，不要再发明第三套圆角。
3. **设计稿本身多档**。`extra.css` 里 2/4/5/6/7/8/10/12/16/18/20/99 都有真实用途
   （复选框 4、品牌印章 7、头像 50%、卡片 20、内卡 16…）。照抄页面散值是对的，
   **禁止"统一成一套圆角"**；只统一语义，不统一数字。
4. **同心半径 = 外层半径 − 内外间距**。卡里的内卡用 16（20−4），别内外都写 20，否则
   视觉上"内卡比外卡还圆"。贴边破角时按父级实际半径裁，不要另给一个小圆角。
5. **`Border.CornerRadius` 不裁子元素**。它只画自己的边框和背景；子元素（图片、色块、
   表格）会照旧戳出直角。需要圆角裁子元素时，挂 `ui:KxRoundedClip`（或让子元素自身带同半径）。
6. **卡片带 `Fx.CardShadow` 时禁止挂 `KxRoundedClip`**。`KxRoundedClip` 的实现是 `Clip`，
   Clip 在 Effect 之后生效，会把 `DropShadowEffect` 的外扩柔影一起裁掉，玻璃态立刻变"贴纸"。
   正确做法：卡片本身不裁，只给**贴边的子元素**（图片、表头、底部条）单独挂裁剪。
7. **禁用 `ClipToBounds="True"` 代替圆角**：它裁的是矩形，四角还是直角，只是把溢出藏了。
8. **目检必须放大四角**。1440×900 的 1x 全页截图看不出 1–2px 的破角/漏白；用脚本把
   目标区域放大 3–6 倍逐张看，明/暗主题各一轮，重点看胶囊两端、卡片四角、表格右上角。

### 4.2 系统默认白底（表格 / 滚动条，深色主题必查）

- `ListView` 的默认模板里 `ScrollViewer` 背景取 `SystemColors.WindowBrush`（**永远是纯白**），
  列宽总和小于控件宽度时，右侧会露白；深色主题下整条发亮。
- `GridView` 还会自动补一个**尾部填充列**，它的列头是框架内部 new 的 `GridViewColumnHeader`，
  拿不到页面上给真实列设的 `HeaderContainerStyle`，会退回 Aero2 白底模板 —— 表头右侧再露一块白。
- 两条都要治：`Grid.Table` 模板里把 `ScrollViewer.Background` 固定 `Transparent`；
  并在 `Sinan.Controls.xaml` 里放一个**无 key 的隐式 `GridViewColumnHeader` 样式**
  `BasedOn="{StaticResource Grid.Header}"`，覆盖填充列头。
- 滚动条同理：`Scroll.Thin` 必须是**无 key 的隐式 `ScrollBar` 样式**。只给 `x:Key` 等于没有——
  页面里没人显式引用，全部退回 Aero 模板：白底轨道 + 直角上下箭头，深色下整条发亮。
  自绘滚动条模板时记得补 `Track.DecreaseRepeatButton/IncreaseRepeatButton`（透明 `RepeatButton`
  绑定 `ScrollBar.PageUpCommand/PageDownCommand`），否则点轨道翻页失效。

### 4.3 文本清晰度（ClearType / 分层窗口 / 小字号，2026-11-25 实机整改）

**根因**：WPF `AllowsTransparency=True`（分层窗口 `WS_EX_LAYERED`）的最终呈现面是
32bpp ARGB 位图，系统会**强制关闭 ClearType**，整窗文字退化为灰度抗锯齿。100% DPI 下
9~11.5px 中文会明显发虚（登录页数据文件路径 / 演示账号 / 数据目录是典型；换页面也一样）。

**落地口径**：司南主窗 `MainWindow`、登录窗 `LoginWindow`、全部 `ChromeDialogWindow`
弹窗一律 override `UseLayeredWindow => false`，关掉分层窗口，改走 `SetWindowRgn`
物理圆角裁剪（半径取 `Fn.ShellRadius=18`），恢复 ClearType。

- 代价一：圆角边缘是 1bit 硬裁剪（无抗锯齿），1x 下每级约 1px 阶梯；放大 3x 以上可见，
  正常观看距离可接受。
- 代价二：窗口外柔影被裁掉（弹窗 `Fx.WinShadow` 不再显示）。这是"文字清晰优先"的明确取舍，
  不要为了阴影再改回分层。
- 非分层窗口 `Background` 必须设主题底色（登录/主窗 `Brush.Bg`，弹窗基类
  `Brush.Fresh.WinBg`），否则四角/首帧会露系统白底。
- 最大化时 `ApplyRoundWindowRegion` 清除 Region（`SetWindowRgn(hwnd, IntPtr.Zero)`），
  还原时重挂；跨屏 DPI 变化走 `DpiChanged` 重算。**新窗口**沿用这套即可，不要自己写 Region 代码。

**配套文字规则**：

1. 窗口根统一 `UseLayoutRounding="True"`（布局取整，避免半像素定位）+
   `TextOptions.TextFormattingMode="Display"`（小字号按整数像素出字形）；大标题（`T.H1`）
   保留 `Ideal`，避免 19px 以上标题边缘发硬。
2. 明主题 `Brush.Muted` 已加深到 `#6B5C4B`（原 `#7D6E5E`），`Brush.Faint` 为 `#8F806C`；
   小字不要再往更浅调。暗主题 `Brush.Muted` 已有 7:1 对比度，不动。
3. **含文字的容器一律不挂 `Effect`**（2026-11-25 实测落地）：`DropShadowEffect` 会把
   "元素 + 整棵子树"先渲染到离屏位图再合成，位图面不支持 ClearType——同一个登录卡片，
   有 Effect 时文字亚像素色偏指标 1.32（灰度抗锯齿），摘掉后 5.84（与卡片外持平）。
   本项目已全部摘除：`Card` 样式、`Btn.Primary`（hover 改用 `Brush.PrimaryGradHover` 提亮）、
   `Nav.Item` 选中态、ComboBox 下拉 Popup、底部导航胶囊。登录页品牌印章（纯图形、无文字）
   保留 `Fx.PrimaryGlow`。**新增卡片/容器不要再挂 Effect**，柔影由页面底层柔光斑
   （`Brush.MeshA/B/C`）承担。
4. 可读正文 ≥ 10.5px，说明性文字 ≥ 10px；9 / 9.5px 只留给装饰性角标、缩略图序号等
   非阅读内容。登录页的路径、演示账号、数据目录已按此上调。
5. 阅读密集型内容（表格、表单、列表）放在 `Card.Inset` 上：`Brush.CardWell` 已改为
   **完全不透明**（明 `#FCF7EF` / 暗 `#2E2218`），保证表格文字走 ClearType。
   玻璃卡片（`Brush.GlassBg` 半透明）只承载标题、KPI 数字等大字号内容——实测半透明背景
   本身不阻止 ClearType（合成后背景不透明即可），但别把长段小字放进玻璃层。

### 4.4 页面高度与底部 Dock（2026-11-26 详情页整改）

主窗内容区是「页面 `*` + 底部悬浮 Dock `Auto`」两行，Dock 本身不覆盖页面。但页面若把
整块内容塞进一个整页 `ScrollViewer`，内容高度超过一屏时，首屏底部的操作行会停在 Dock
正上方并只露半截——视觉上就是"被导航条遮住"（详情页卡底「填写阶段」踩过）。

**落地口径**：主操作不放进整页滚动区。

- 详情页按设计稿 p2 固定一屏：根 `Grid` 行 = 摘要卡 Auto / 编辑区 Auto / 提示 Auto /
  主体 `*`；左卡 = 卡头 Auto / 内容区 `*`（内部 `ScrollViewer`）/ 卡底操作 Auto；
  右栏三张卡各自 `Auto` 并可整列滚动。1440×900 与 1280×800、编辑态展开下卡底按钮
  均常驻可见。
- 新增页面同理：先问"这一屏的主操作是什么"，把它固定在不滚动的行里，
  只让明细/列表滚；不要指望用户先滚到底才能点保存/推进。

## 5. 弹窗规范

1. **ViewModel 不 new Window**：一律走 `IDialogService`。新增弹窗在
   `Services/DialogRequests.cs` 加请求/结果 DTO，接口加一个方法，
   ViewModel 只认 DTO——换实现（改成内联面板）不动业务代码。
2. **弹窗根用 `ChromeDialogWindow`**（库内），标题栏/圆角/投影/主题键全部来自库，
   不在页面里重复实现。`Load(request)` 由 DialogService 在 `ShowDialog` 前调用，
   不给 ViewModel 做"半初始化"的无参构造。
3. **危险操作必须二次确认**：删除问题要手抄问题号（逐字比对，不匹配按钮置灰），
   并如实列出"影响几条留痕 / 几张照片"。
4. **"保存前必须知道"的提示不进滚动区**：倒挂红条、就地错误固定在表单下方常驻；
   放滚动区里会被后面的输入框挤出可视区，用户没滚到底就点了保存，然后什么也没发生。
5. **主操作常驻可见**：导出这类主按钮固定在面板底部，滚动区只放"看"的内容
   （1440×900 下内容会顶出可视区，按钮留在滚动区里就得先滚到底才能点）。
6. **列表项必须给 `AccessibleName`**：默认名是类型名，同类多项全同名，
   读屏与自动化都定位不到（导出范围/版式、设备表行、总览卡片按钮都踩过）。
7. **三选一优先用 `ListBox + SelectionItemPattern`，不要自绘 `RadioButton` 模板**：
   自绘 RadioButton 模板在 UIA 里 `IsOffscreen` 返回 unknown，`click`/`select` 都会
   actionability 超时——读屏用户与自动化都点不到。ListBox 的 ItemContainerStyle 一样能做
   卡片式外观，还自带选中语义（导入"写入范围"、导出"范围/版式"都是这套）。
8. **弹窗里的数字必须来自真实数据**：导入确认的五格计数、导出的"将包含"逐条，
   全部由预览/查询结果填入，不允许照抄设计稿的演示值（`36 条`、`D:\Sinan\outbox\` 都是占位）。
   条目数随数据变化时，那一块要放进 `ScrollViewer`，不要靠调高固定窗口高度硬塞。

## 6. Excel 无损与导出

### 6.1 无损验收（每次改动导入/导出必跑）

1. 问题条数导入前后一致（当前演示库 36 条）；
2. 标题、解决措施逐字一致；
3. 责任人原文一致（含 `/` 多人）；
4. 日期原文一致（`11.5` 不得变成 `2026-11-05`）；
5. `/` 与空白计数不混淆；
6. 机台号、序号逐字一致；
7. 行顺序按 `source_row` 一致；
8. 已知脏数据（`已下图`、`P73913118`）原样保留。

### 6.2 导出结构

三种版式由 `ExportRequest.Layout` 决定，**口径全部复用 `StageRules`**，
禁止在导出代码里另写一套倒挂/逾期判断：

| Layout | 内容 | 给谁看 |
|---|---|---|
| `original`（默认） | 客户原表版式：三行表头、两级列组、列宽、冻结、视觉合并 | 回给客户 |
| `standard` | 标准明细：一行一条，四阶段拆独立列 | 二次分析 |
| `summary` | 统计汇总：总览 + 责任人负载 + 阶段填写率 + 倒挂清单 | 协调会 |

导出范围由 `ExportRequest.Scope` 决定（`all` / `inverted` / `overdue` / `photos`），
只按库内已有字段过滤，不在导出时重算口径。附加页放附件清单/变更记录，不破坏原表版式。
默认 `.xlsx`，兼容 `.xls`。**同一分钟内多次导出不得互相覆盖**：撞名追加 `_2`/`_3`。

## 7. 运行时数据（Windows）

运行时数据**不放在仓库**：

- 数据库：`%LOCALAPPDATA%\Sinan\data\sinan.db`（可用环境变量 `SINAN_DATA_DIR` 覆盖）
- 附件：`%LOCALAPPDATA%\Sinan\attachments\`
- 日志：`%LOCALAPPDATA%\Sinan\logs\`
- 导入归档：`%LOCALAPPDATA%\Sinan\archive\`

界面上显示的数据路径必须取实际值，不能抄设计稿里的 `D:\Sinan\data\sinan.db`。

## 8. 构建与验证

```powershell
# 还原 + 构建
dotnet build desktop/Sinan.sln -c Debug

# 运行（开发期）
dotnet run --project desktop/src/Sinan.App/Sinan.App.csproj

# 数据层/Excel/UI 渲染回归（当前 210 条）
dotnet test desktop/tests/Sinan.Tests/Sinan.Tests.csproj -c Debug

# 后台静默 UI 冒烟（截图 + 圆角自检，不抢焦点）
dotnet run --project tools/ui_smoke/UiSmoke.csproj -c Debug -- --launch --restart

# 端到端写入验证（新建→详情→删除，独立临时库）
dotnet run --project tools/ui_smoke/UiSmoke.csproj -c Debug -- --launch --restart `
  --no-pages --write-test --data-dir "$env:TEMP\sinan-e2e"
```

- 成功标准：`Sinan.App.exe` 生成、0 error、主窗能显示、数据库自动建表并种入 36 条样例。
- **改完必须构建**；WPF 改动还要启动实机截图目检。SDK 工程新增文件漏编时删 `obj`/`bin` 整编。
- 首次启动种子账号见根 `../AGENTS.md`（`engineer / 123456`、`admin / 123456`）。
- 数据库 schema 变更：改 `shared/db/schema.sql` + 升 `schema_version` + 写迁移；
  同步改桌面仓储与手机 `sqflite`（见根第 8 节）。
- 同步服务默认端口 `5180`，仅监听局域网；Windows 防火墙首次放行需用户确认。

## 9. 交付清单（桌面端）

在根目录通用清单之外，逐条确认：

- [ ] `dotnet build desktop/Sinan.sln -c Debug` 0 error；
- [ ] 实际启动程序，主流程点一遍（登录 → 总览 → 台账 → 详情 → 新建/推进）；
- [ ] 关键页面截图目检（含明/暗主题各一次）；Debug 日志里 `[圆角自检·…]` 为"通过"
      （有 WARN 就修）；
- [ ] 改了 Excel 导入/导出：跑 6.1 的 8 条断言；
- [ ] 改了界面：桌面 1440×900 与最小 1280×800 各看一次，四角放大目检；
- [ ] `dotnet test` 全过（当前 210 条）；
- [ ] 改了同步：桌面端 `SyncServer` 与 `shared/api/` 契约一致。

## 10. 常用文件导航（桌面端）

| 文件 | 作用 |
|---|---|
| `desktop/src/Sinan.App/App.xaml.cs` | 组合根：数据库初始化、服务装配、主窗启动 |
| `desktop/src/Sinan.App/ViewModels/ShellViewModel.cs` | 主壳导航、会话、主题（唯一 `ThemeManager.Changed` 订阅者） |
| `desktop/src/Sinan.App/Views/MainWindow.xaml.cs` | 窗口 glue + Debug 期圆角自检入口（`KxRadiusAudit`） |
| `desktop/src/Sinan.App/Views/SubprojectPage.xaml` | 分项目 · 设备问题预览（设计稿 p1 project） |
| `desktop/src/Sinan.App/Services/DialogRequests.cs` | 五个业务弹窗的请求/结果 DTO（阶段/删除/照片/导入确认/导出确认） |
| `desktop/src/Sinan.App/Views/StageDialog.xaml` | 更新阶段弹窗（设计稿 dlg-stage） |
| `desktop/src/Sinan.App/Views/DeleteIssueDialog.xaml` | 删除问题二次确认（设计稿 dlg-del） |
| `desktop/src/Sinan.App/Views/PhotoViewerDialog.xaml` | 查看现场照片（设计稿 dlg-photo） |
| `desktop/src/Sinan.App/Views/ImportConfirmDialog.xaml` | 确认导入 / 冲突逐条处置（设计稿 dlg-import-conflict） |
| `desktop/src/Sinan.App/Views/ExportConfirmDialog.xaml` | 开始导出（设计稿 dlg-export） |
| `desktop/src/Sinan.App/Views/ExchangePage.xaml` | 数据交换：导入四步向导 + 导出三选一 |
| `desktop/ThirdParty/Kaleidoscope/Kaleidoscope.UI/Views/KxCapsuleBorder.cs` | 胶囊圆角控件（一行标签，自动半高） |
| `desktop/ThirdParty/Kaleidoscope/Kaleidoscope.UI/Views/KxRadiusAudit.cs` | 圆角体检器（椭圆化圆角运行时报告） |
| `desktop/src/Sinan.App/Services/SyncServer.cs` | 局域网同步服务（配对/增量/附件） |
| `desktop/tests/Sinan.Tests/Ui/UiRenderTests.cs` | 离屏渲染回归：6 页 × 明暗双主题 + 登录窗 + 6 弹窗，不建可见窗口 |
| `desktop/src/Sinan.Data/Excel/` | 导入解析、导出渲染、往返校验 |
| `tools/ui_smoke/Program.cs` | 后台静默 UI 冒烟：登录 → 逐页/弹窗截图 → 圆角自检；`--write-test` 走新建→删除全链路 |
