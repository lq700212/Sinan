# AGENTS.md — 司南 · P7391 安装调试问题闭环系统

> AI 助手在本项目开工前的强制前置阅读。优先级：本文件 > 项目内其他文档 > 通用最佳实践。
> 本文件由 HuaJiVision 项目的通用规范吸收改写而来，只保留对本项目有效的内容。

## 1. 项目定位

**司南（Sinan）** 是一套面向设备安装调试期的现场问题闭环系统，首个落地项目为 **P7391**。

- 总项目 `P7391` → 分项目 `P7391-01/-02/-03` → 设备（机台号）→ 问题点 → 四阶段推进。
- 现场用手机拍照记录，办公室用桌面端排期、追踪、导出。
- 替代"Excel 副本传来传去"：数据一份、过程留痕、逾期自动暴露、原表能进能出。

**当前阶段**：MVP 开发。桌面端 WPF 先行，Android 端随后；两端都以设计稿为准。

**只做软件本体，不做客户现场部署、不做 MES 对接、不做审批流。**

## 2. 事实来源与优先级

发生冲突时按以下顺序取真源，**不要凭记忆重写业务规则**：

| 优先级 | 真源 | 用途 |
|---|---|---|
| 1 | 本地私密原始表（`source/`，仅本机测试，已 gitignore） | 字段、版式、脏数据的最终依据（L0 无损） |
| 2 | `docs/ui_design_mockups.html` + `docs/_build/extra.css` | 双端界面、交互、颜色/间距/字号的唯一视觉依据 |
| 3 | `docs/调研与方案.md` | 业务规则、Excel 无损口径、架构与风险 |
| 4 | `shared/db/seed/issues.seed.json` | 36 条脱敏演示种子（双端首启一致） |
| 5 | `README.md` | 交付状态与对外说明 |

- 设计稿是单方案 **C 晨光现场 · 精致玻璃态**，明/暗双主题；不要再引入 A/B/D/E。
- 演示性数字必须标注"演示值"（尤其照片：12 张 / 6 条问题，历史 Excel 无附件）。
- 统计数字一律从数据库实时算，禁止在 XAML/代码里写死 57、30、13 这类结果值
  （设计稿里的数字是视觉占位，不是业务常量）。

## 3. 双端架构

```
┌──────────────────────────┐        局域网 HTTP/JSON        ┌──────────────────────┐
│ WPF 桌面端（Windows）      │ ◄───────────────────────────► │ Flutter 现场端（Android）│
│ 主库 SQLite（唯一真源）    │      配对码 + Bearer Token      │ 本地 SQLite 缓存      │
│ Excel 导入/导出（NPOI）    │      增量同步 + 照片上传         │ 离线优先 + outbox      │
│ 内嵌同步服务 :5180        │                               │ 拍照/扫码/推进阶段     │
└──────────────────────────┘                               └──────────────────────┘
```

- **不做双主库双向合并**。桌面端是主库；手机端是离线副本 + 待传队列。
- 手机首次通过桌面端"移动端配对"的 6 位码/二维码建立信任，之后可完全离线记录。
- 同一问题两端都改 → 服务端按 `version` 判冲突，手机端必须人工选择"保留手机 / 保留桌面"，
  禁止静默覆盖。
- 附件先存本地，联网后按队列补传；桌面端负责归档、查看、导出 ZIP + `manifest.csv`。
- 外网访问不属于 MVP：如需，后续用 frp / Cloudflare Tunnel 映射到同一服务，仍是一套库。

## 4. 技术栈（已定）

| 层 | 选型 | 约束 |
|---|---|---|
| 桌面端 | **.NET 10 + WPF**（`net10.0-windows`） | 本机只装了 .NET 10 SDK，不要降到 net8 以外的框架 |
| 桌面架构 | **MVVM**：View / ViewModel / Service / Repository | 不引重型框架；自写 `ObservableObject`/`RelayCommand` 或最小 Toolkit |
| 数据库 | **SQLite**（Microsoft.Data.Sqlite，WAL） | 单文件；`shared/db/schema.sql` 是唯一 DDL 真源 |
| Excel | **NPOI** | 必须同时支持 `.xls`（BIFF8）与 `.xlsx`；不要用只写 xlsx 的库 |
| JSON | System.Text.Json | 同步协议与配置统一用它 |
| 日志 | 自写滚动文件日志 | 不引 log4net；日志目录与数据库同根 |
| Android | **Flutter 3.50 + Dart 3.14 + MVVM + sqflite + shared_preferences** | 仅 Android 可构建（iOS 只做设计支持）；业务代码全部 Dart，Android 壳只保留 Flutter 模板的 `MainActivity` |
| 主题 | WPF 双 ResourceDictionary；Flutter `SinanPalette` 明/暗两套 Token | 只允许引用主题键，禁止在页面里写死色值 |

**WPF 玻璃态落地口径**：WPF 没有 CSS `backdrop-filter`，用三层近似，禁止用 `BlurEffect`
直接糊卡片内容（会把文字一起糊掉）：

1. 页面底层铺 3 个 `RadialGradientBrush` 柔光斑（琥珀 / 深青 / 暖橙）；
2. 卡片用半透明渐变底 + 1px 亮边 + 顶缘内高光；
3. 外层 `DropShadowEffect` 做暖色柔影；暗色主题换深棕半透明底 + 18% 白边。

`.card.inset`（卡套卡）与表格内部保持不透明，保证可读性。

## 5. 目录结构（根目录双项目，不混在一个 src 里）

```
Sinan/
├─ AGENTS.md                  ← 本文件
├─ README.md                  ← 对外说明 / 当前进度
├─ CHANGELOG.md               ← 每次交付追加
├─ desktop/                   ← ① Windows 桌面端（WPF 解决方案根）
│  ├─ Sinan.sln
│  ├─ src/
│  │  ├─ Directory.Build.props ← 司南工程的编译与版本约定（只作用于 src/）
│  │  ├─ Sinan.Core/          ← 领域模型、枚举、DTO、纯规则（无 UI / 无数据库依赖）
│  │  ├─ Sinan.Data/          ← SQLite 仓储、种子、Excel 通道（NPOI）
│  │  └─ Sinan.App/           ← WPF：App/Views/ViewModels/Services/Controls/Themes/Assets
│  ├─ ThirdParty/             ← Windows 专用第三方依赖（git submodule）
│  │  ├─ Directory.Build.props ← 令 Kaleidoscope.UI 在司南只产 net10.0-windows
│  │  └─ Kaleidoscope/        ← 公司级 WPF 复用库；Sinan.App 只引 Kaleidoscope.UI
│  └─ tests/Sinan.Tests/      ← xUnit：往返、统计口径、同步冲突
├─ mobile/                    ← ② 现场端（Flutter 工程根，Dart）
│  ├─ lib/                    ← 主题 Token / 页面 / 状态 / 数据层
│  ├─ assets/seed/            ← 与桌面端同一份 36 条种子
│  ├─ test/                   ← 纯规则回归（日期/状态/归组）
│  ├─ android/                ← Flutter Android 宿主壳（Kotlin 仅 MainActivity）
│  └─ ios/                    ← 仅设计支持，本机无法构建
├─ shared/                    ← 双端共享契约（唯一真源，两端都只读它）
│  ├─ db/schema.sql           ← SQLite DDL；桌面 Data 嵌入、Flutter sqflite 对照
│  ├─ db/seed/                ← 36 条样例种子（双端首启一致）
│  └─ api/                    ← 局域网同步 API 契约（配对/增量/附件）
├─ docs/                      ← 设计稿、调研、构建脚本、截图
├─ data/                      ← 本地私密解析样例（gitignore，不进交付）
├─ source/                    ← 本地私密原始表（gitignore，不进交付）
└─ tools/                     ← 脱敏扫描等可复用工具（涉真实资料的自测脚本不入库）
```

- 两端**各自是完整可打开的项目**：`desktop/Sinan.sln` 用 Visual Studio 打开，
  `mobile/` 用 Android Studio 或 VS Code 打开；不要出现"根目录一个巨型 sln 把 Dart 也塞进来"。
- 跨端共享只允许放在 `shared/`：数据库 DDL、种子、同步契约。
  **禁止**桌面端直接引用 Android 源码，反之亦然。
- **Windows 专用依赖一律进 `desktop/ThirdParty/`**（含 git submodule：Kaleidoscope），
  根目录不出现任何只能被 Windows 使用的目录或构建属性文件。
- **WPF 控件先查 Kaleidoscope.UI 复用索引，禁止重复造轮子**：宿主新建控件/样式/转换器前，
  先查 `desktop/ThirdParty/Kaleidoscope/Kaleidoscope.UI/README.md` 的「控件复用索引」；
  库内有对应物直接用，有近似物 `BasedOn` 再造。确无对应物才自绘，且必须走本主题 Token、
  不写死色值。司南沉淀出的通用控件**回写到 Kaleidoscope 子模块**（在其独立仓库提交），
  司南仓库只升级子模块指针。
- **胶囊圆角用 `ui:KxCapsuleBorder`**（库 V2.4.1，替换 `<Border>` 标签即可，构造即绑半高；
  竖向窄条用 `Axis="Width"`），不要手写 `CornerRadius="99"`、也不要手抄半高 Binding。
  开发期圆角问题由 `KxRadiusAudit` 自动兜住（见 8.1 第 1 条），不靠记性。

运行时数据**不放在仓库**：

- 数据库：`%LOCALAPPDATA%\Sinan\data\sinan.db`（可用环境变量 `SINAN_DATA_DIR` 覆盖）
- 附件：`%LOCALAPPDATA%\Sinan\attachments\`
- 日志：`%LOCALAPPDATA%\Sinan\logs\`
- 导入归档：`%LOCALAPPDATA%\Sinan\archive\`

界面上显示的数据路径必须取实际值，不能抄设计稿里的 `D:\Sinan\data\sinan.db`。

## 6. 数据模型红线

完整 DDL 在 `shared/db/schema.sql`。以下规则不可改：

1. **双字段存储**：每个可能被人工改写的日期/人员字段同时存语义值和原文。
   - `due_date`（可空，用于计算）+ `due_raw`（原文，用于导出还原）。
   - `owner_text`（原文，含 `/` 多人）+ `owner_ids`（关联人员）。
   - 导出优先 `due_raw`，为空才格式化 `due_date`。
2. **`/` 与空白不同**：`/` = 不适用（`state=na`），空白 = 未填（`state=not_started`）。
   导入/导出/统计都必须保留这个区别。
3. **序号不重排**：`issues.seq_no` 保留原表序号（允许缺口，例如缺 28）。
4. **保留来源**：`source_batch_id` + `source_row` 必须能追溯到"哪个文件第几行"。
5. **机台号带后缀**：`P7391\d{3}(-\d)?`；`P73913118` 属已知笔误，保留原文并归入待确认，
   **不要静默改正**。
6. **人员停用不删除**：`users.active=0`，历史记录里的名字不能变成"未知"。
7. **问题四阶段单独建表**：`issue_stages(stage_code=plan|design|material|assembly)`，
   不塞进 `issues` 宽表。
8. **乐观锁**：所有可同步实体带 `version`；更新 `WHERE id=? AND version=?`，
   影响行数为 0 即冲突。
9. **审计**：状态、责任人、计划时间的变更写 `activity_log`，字段级 old/new。
10. **软删除**：问题删除写 `deleted_at`，不物理删除；导出默认排除。

**分项目归属规则**（演示数据，虚构项目 P7391，当前 36 条 / 31 台）：

- `P7391101–P7391110` → `P7391-01`（10 台 / 15 条）
- `P7391201–P7391208` → `P7391-02`（8 台 / 8 条）
- `P7391301–P7391312` → `P7391-03`（12 台 / 12 条）
- `P73913118` → 待确认（1 台 / 1 条），不自动归组

**状态机**：`待分派 → 方案中 → 设计中 → 备料中 → 装配中 → 待验证 → 已关闭`，
另有 `已取消`。阶段推进可自动推导状态，也允许人工覆盖；任何覆盖都留审计。

## 7. MVVM 约定

- **View 只留 glue**：密码框回传、窗口拖动、关闭确认等；业务判断一律在 ViewModel。
- **ViewModel 不 `new Window`**：开窗/弹框走 `IDialogService`，导航走 `INavigationService`。
- **Repository 不出现在 View 里**：View 的 `DataContext` 只能是 ViewModel。
- **命令统一 `RelayCommand` / `AsyncRelayCommand`**，禁止在 code-behind 写 `async void` 业务。
- **线程**：数据库/Excel/HTTP 一律 `Task.Run` 或异步 API；回到 UI 线程用
  `Application.Current.Dispatcher`；禁止在 UI 线程做超过 50ms 的 IO。
- **错误**：ViewModel 捕获业务异常 → 用户可读提示 + 日志；不要弹原始堆栈。
- **可测试**：统计口径、状态推导、冲突判定放在 Core/Data，纯函数优先。

## 8. 界面规范（来自设计稿 C）

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

### 8.1 圆角与裁剪（WPF 落地口径）

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

### 8.2 系统默认白底（表格 / 滚动条，深色主题必查）

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

## 9. Excel 无损验收（每次改动导入/导出必跑）

1. 问题条数导入前后一致（当前演示库 36 条）；
2. 标题、解决措施逐字一致；
3. 责任人原文一致（含 `/` 多人）；
4. 日期原文一致（`11.5` 不得变成 `2026-11-05`）；
5. `/` 与空白计数不混淆；
6. 机台号、序号逐字一致；
7. 行顺序按 `source_row` 一致；
8. 已知脏数据（`已下图`、`P73913118`）原样保留。

导出结构：三种版式由 `ExportRequest.Layout` 决定，**口径全部复用 `StageRules`**，
禁止在导出代码里另写一套倒挂/逾期判断：

| Layout | 内容 | 给谁看 |
|---|---|---|
| `original`（默认） | 客户原表版式：三行表头、两级列组、列宽、冻结、视觉合并 | 回给客户 |
| `standard` | 标准明细：一行一条，四阶段拆独立列 | 二次分析 |
| `summary` | 统计汇总：总览 + 责任人负载 + 阶段填写率 + 倒挂清单 | 协调会 |

导出范围由 `ExportRequest.Scope` 决定（`all` / `inverted` / `overdue` / `photos`），
只按库内已有字段过滤，不在导出时重算口径。附加页放附件清单/变更记录，不破坏原表版式。
默认 `.xlsx`，兼容 `.xls`。**同一分钟内多次导出不得互相覆盖**：撞名追加 `_2`/`_3`。

## 9.1 弹窗规范（本轮沉淀）

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

## 10. 铁律

1. **数据脱敏是最高红线**：真实项目号/机台号/人名/问题原文/原表文件名/截图，
   一律不得进入代码、文档、种子、界面与提交；详见第 15 节。
2. **文件编码 UTF-8**。禁止用 PowerShell `Out-File`/`Add-Content` 写含中文的文件；
   用 `write`/`edit` 工具或 Python `io.open(..., encoding='utf-8')`。
3. **终端 GBK 乱码是显示假象**。判断文件内容用严格解码或码位断言，禁止肉眼看码。
4. **改完必须构建**：`dotnet build desktop/Sinan.sln -c Debug` 0 error 才算完成；
   WPF 改动还要启动实机截图目检。SDK 工程新增文件漏编时删 `obj`/`bin` 整编。
5. **临时脚本用完即删**（`check_*.py`、截图脚本、调试输出）；`docs/_build/shots` 只留交付截图。
6. **不主动 commit/push**，除非用户明确要求；不把 `%LOCALAPPDATA%\Sinan` 数据、日志、附件入库。
7. **改动彻底**：本项目未上线、无兼容包袱。删旧逻辑不留新旧分支/回落开关，
   不向用户确认"要不要兼容"。
8. **注释要详细**：关键方法写清"做什么 + 为什么 + 从设计稿/原表哪里来"，中文 UTF-8。
9. **不伪造**：核验脚本 FAIL 就如实报告；不要把多方案差异度检查套到单方案上凑绿。
10. **统计口径只读语义字段**：逾期只用 `due_date`；`/`、`已下图`、空文本不参与逾期计算。
11. **照片/附件**：文件按 `attachments/{issueId}/{uuid}.jpg` 存；DB 只存相对路径；
    导出 ZIP 带 `manifest.csv`；删除问题不删附件原文件，等归档策略处理。

## 11. 构建与运行

```powershell
# 还原 + 构建（桌面端）
dotnet build desktop/Sinan.sln -c Debug

# 运行（开发期）
dotnet run --project desktop/src/Sinan.App/Sinan.App.csproj

# 数据层/Excel 回归（待 tests 落地后）
dotnet test desktop/tests/Sinan.Tests/Sinan.Tests.csproj -c Debug

# 现场端（Flutter，Android）
cd mobile
flutter pub get
flutter test
flutter build apk --debug
```

- 成功标准：`Sinan.App.exe` 生成、0 error、主窗能显示、数据库自动建表并种入 36 条样例。
- 现场端成功标准：`flutter analyze` 0 issue、`flutter test` 全过、
  `mobile/build/app/outputs/flutter-apk/app-debug.apk` 生成。
- 现场端构建走国内镜像：Gradle 腾讯云、Maven 阿里云；
  Flutter 资源与 pub 建议 `storage.flutter-io.cn`、`pub.flutter-io.cn`。
- 首次启动种子账号：`engineer / 123456`（工程师）、`admin / 123456`（管理员）。
  首次登录后应提示改密；演示阶段允许直接进入。
- 数据库 schema 变更：改 `shared/db/schema.sql` + 升 `schema_version` + 写迁移；两端（桌面仓储 / Flutter sqflite）同步改。
- 桌面端同步服务默认端口 `5180`，仅监听局域网；Windows 防火墙首次放行需用户确认。

## 12. 任务完成清单

每次交付前逐条确认：

- [ ] `dotnet build` 0 error；
- [ ] 实际启动程序，主流程点一遍（登录 → 总览 → 台账 → 详情 → 新建/推进）；
- [ ] 关键页面截图目检（含明/暗主题各一次）；Debug 日志里 `[圆角自检·…]` 为"通过"（有 WARN 就修）；
- [ ] 现场端 `flutter analyze` 0 issue、`flutter test` 全过、APK 生成（改过 Dart 时必跑）；
- [ ] 数据只来自数据库，演示值有标注；
- [ ] `python tools/desensitize_scan.py` 0 命中（真实数据未进入交付物）；
- [ ] Excel 改动跑第 9 节 8 条断言；
- [ ] `CHANGELOG.md` 追加本轮变更；
- [ ] 临时脚本/中间文件已清理；
- [ ] `README.md` 的进度与实际一致。

## 13. 待确认（不阻塞开发，先按默认值做）

| # | 事项 | 当前默认 |
|---|---|---|
| 1 | 现场网络形态 | 桌面端主库 + 局域网同步；外网后续穿透 |
| 2 | 登录权限粒度 | 管理员 / 工程师 / 只读三角色；MVP 只做页面门禁 |
| 3 | 导出格式 | 默认 `.xlsx`，保留 `.xls` 兼容 |
| 4 | 照片大小/数量 | 手机拍后压到长边 1600px，单张 ≤2MB |
| 5 | 问题族/共性标签 | 表结构预留 `category`，MVP 不做聚类界面 |
| 6 | 客户是否要甲方只读账号 | MVP 不做客户门户 |

## 14. 常用文件导航

| 文件 | 作用 |
|---|---|
| `docs/ui_design_mockups.html` | 双端设计稿（20 页 + 11 弹窗），浏览器打开 |
| `docs/_build/extra.css` | 设计 Token 与全部组件样式（颜色/间距/玻璃态） |
| `docs/调研与方案.md` | 业务与 Excel 无损方案 |
| `shared/db/schema.sql` | SQLite DDL 唯一真源（双端） |
| `shared/db/seed/` | 36 条样例、31 台机台、分项目映射 |
| `shared/api/` | 局域网同步 API 契约 |
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
| `desktop/src/Sinan.Data/Excel/` | 导入解析、导出渲染、往返校验 |

## 15. 数据脱敏（硬性要求，违反即返工）

> 客户真实数据只允许存在于本机 `source/`、`data/` 与本地敏感词清单中；
> **任何交付物都不得出现**。这一节优先级等同铁律。

1. **禁止出现的真实数据**：真实项目号、机台号、人名、问题标题、解决措施、
   原表文件名、现场照片、截图中的真实数据、日志样例、commit message。
2. **演示数据统一虚构**：总项目固定为 `P7391`，分项目 `P7391-01/02/03`，
   人员使用"张工 / 李工 / 王工…"等角色化假名；问题标题与解决措施必须随机生成，
   **禁止从真实表复制、改写或摘抄**。唯一真源：`shared/db/seed/issues.seed.json`。
3. **本地真实资料隔离**：`source/`、`data/`、含真实资料的自测脚本一律进 `.gitignore`；
   软件运行时不得读取这些目录；不得截图、录屏、粘贴进代码注释/文档/提交信息。
4. **设计稿与截图同步脱敏**：`docs/ui_design_mockups.html`、`docs/_build/p*.html`、
   `docs/_build/shots/` 只允许使用 P7391 虚构数据；改数据后必须重新截图，
   交付前逐张目检是否残留真实信息。
5. **提交前扫描**：运行 `python tools/desensitize_scan.py`（敏感词从本机
   `.sensitive.local` 读取，该文件不入库）；命中即返工，不讨论"只是注释/示例"。
6. **外部库隔离**：`desktop/ThirdParty/` 是公司通用库，不得把客户数据写入其源码、
   文档、测试或配置；库内示例数据与本项目无关。
7. **新功能同样适用**：Excel 导入样例、导出模板、截图脚本、演示附件、同步测试载荷，
   全部使用虚构数据；任何"先用真实数据跑通再删"的做法都视为违规。
