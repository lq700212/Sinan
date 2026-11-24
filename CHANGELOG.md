# 变更记录

## v0.0.1（2026-11-24）首个可运行版本：桌面端 9 页 + 6 弹窗落地，README 重写并修正 3 处失实宣称

### 文档

- **README 按门户标准重写**：改为徽章 + 一句话定位 + 实机截图图墙 + 核心能力 + 架构 mermaid
  + 目录树 + 技术基线 + 构建运行 + 进度 + 脱敏 + 文档导航的完整结构。
  所有数字均从工程文件与数据库抄取（15 张表、36 条问题 / 31 台机台 / 144 条阶段 /
  12 条演示附件 / 3 个分项目 / 2 个账号、NPOI 2.7.3、Dart 3.14、Flutter 规则回归 14 条）。
- **新增 `docs/images/` 13 张实机截图**（1440×900，含明/暗双主题）：
  登录、项目总览、工作台、问题台账、问题详情、统计报表、数据交换、分项目设备预览、
  系统设置，以及明暗对照的总览/台账、新建问题与开始导出两个弹窗。
  截图全部由真实程序链路驱动，演示数据来自种子库，非 P 图、非设计稿渲染。

### 修正 README 中 3 处与代码不符的宣称（不伪造纪律）

- **「局域网同步服务骨架（配对码 + Bearer + 增量 + 附件接口）」→ 标为未实现**。
  代码里不存在 `SyncServer.cs`、`HttpListener`、`Bearer` 任何痕迹；`LoginViewModel.ShowPairing`
  与 `ExchangeViewModel` 均如实提示"配对服务尚未启用"，`SyncPortDisplay` 写的是
  "规划端口 5180 · 仅局域网 · 当前未监听"。README 原先把规划写成了已完成。
- **「`desktop/tests/` xUnit」→ 标为待补**。该目录实际为空，无任何测试代码；
  Flutter 侧的 `mobile/test/rules_test.dart` 确有 14 条，未受影响。
- **「`shared/api/` 局域网同步契约」→ 从目录树移除**。该目录实际为空（git 不追踪空目录），
  不能当作存在的交付物列出。
- **「桌面端 11 个业务弹窗全部落地」→ 改为 6 个**。设计稿 11 个弹窗中桌面占 8 个，其中
  6 个是独立 `*Dialog.xaml`；`dlg-bind`（手机配对）因同步服务未实现，改为弹 Alert 如实说明，
  `dlg-subproject`（分项目）改为独立页面 `SubprojectPage.xaml`——形态不同但功能到位，
  不能笼统算作"11 个弹窗全部落地"。

### 版本口径统一

- 程序版本统一为 **v0.0.1**：`Directory.Build.props` 的 `<Version>` 从 `0.1.0` 改为 `0.0.1`，
  同时修正界面里两处写死的 `v1.0.0`（`ShellViewModel.VersionText`、`SettingsViewModel.SchemaVersion`）。
  原先是工程文件 `0.1.0` 与界面 `v1.0.0` 两套值，发布版必须唯一。

### 依赖安全：4 个已知漏洞修掉 3 个

发布前跑 `dotnet list package --vulnerable --include-transitive` 发现 4 个传递依赖漏洞
（此前增量构建跳过审计所以没暴露），在 `Sinan.Data.csproj` 显式提升修复：

| 包 | 原版本 | 漏洞 | 处置 |
| --- | --- | --- | --- |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | 高危 GHSA-2m69-gcr7-jv3q | 升 2.1.14，已修复 |
| `System.Security.Cryptography.Pkcs` | 8.0.1 | 高危 GHSA-846f-f764-76mx | 升 10.0.15，已修复 |
| `System.Security.Cryptography.Xml` | 8.0.2 | 高危 GHSA-37gx-xxp4-5rgx | 升 10.0.15，已修复 |
| `SixLabors.ImageSharp` | 2.1.10 | 中危 GHSA-rxmq-m78w-7wmc | **未修复，见下** |

**ImageSharp 为什么留着**：漏洞覆盖 2.x 全系列（试过 2.1.13 仍命中），只有 3.x 以上才修；
而 NPOI 2.7.3 依赖 2.x 的 API，直接升 3.x 编译不过。升 NPOI 到 2.8.1 可彻底解决
（已改用 SkiaSharp），但 2.8.x 构建时要求 `<AcceptNPOIOSMFLicense>true</AcceptNPOIOSMFLicense>`
——OSMF 商业许可，需评估后再定。已核实 Sinan 的导入/导出代码**不调用 ImageSharp 任何 API**，
风险只在 NPOI 内部处理图片的路径，本项目不走该路径。

构建剩余警告全部是这一条 `NU1902` 审计提示（同一漏洞在多个 TFM/工程上重复计数），
**0 错误、0 代码警告**。

## 开发期（2026-11-24，并入 v0.0.1 首发）桌面端功能补全与界面口径整改

### 桌面端

- Excel 导入兼容三种表头布局（阶段组在上 / 阶段组在下 / 扁平表头），
  自动跳过表头下的“××原文”说明行，不再把说明行当数据。
- 修复导出 ZIP 缺 `manifest.csv`：README 的写入流未先释放，导致创建条目时异常。
- 种子 `due` 与 `raw_due` 40 处不一致，全部按原文重算；
  `P7391-010` 责任人原文 `许工/许工` 修正为 `许工`（同一阶段同一人只算一个任务）。
- 阶段保存后状态不再回退到“待分派”：前一阶段已完成或已跳过时必须前进。
- 详情页保存阶段后标题不再被改成“问题台账”。
- 写操作后壳层徽标、台账缓存、总览/工作台/报表缓存同步刷新，搜索与筛选保持。
- 干净首启验收：36 条问题 / 31 台机台 / 144 条阶段 / 12 条演示附件记录 /
  2 个账号 / 3 个分项目；`due_raw` 与 `due_date` 0 处不一致。
- Excel 往返：导出 36 条再导入，预览“可写入 0 行 / 无变化 36 / 冲突 0”。
- 报表责任人负载条按相对最高任务数换算（6 项 100% / 5 项 83% / 4 项 67%）；
  1440×900 下页面正常滚动，不是内容截断。复核：负载为程工 6 项、
  吴工/周工/蒋工/陈工各 5 项、其余 7 人各 4 项，与数据库逐条一致。
- 圆角口径整改（对齐设计稿 C）：胶囊不再写 `CornerRadius="99"`——WPF 会把横/纵半径
  分别钳到宽/2、高/2，宽按钮端头变椭圆"尖角"；统一改用 `Kaleidoscope.UI` 的
  `CapsuleRadiusConverter` 绑 `ActualHeight/2`。覆盖按钮、文本框、密码框、下拉框、
  Chip、导航项、细进度条、用户胶囊、底部 Dock 容器、状态徽标共 13 处；品牌印章 7、
  头像 11（50%）按设计稿保留。主题里删除 `Radius.Btn/Input/Chip` 三个失效键，
  只留卡片 20 / 内卡 16，并注明胶囊的生成方式。
- 修深色主题表格白块：`ListView` 默认模板的 `ScrollViewer` 用系统白底，列宽不足时右侧露白；
  `GridView` 自动补的尾部填充列头又退回 Aero2 白底模板，表头右端再露一块。
  `Grid.Table` 模板固定 `ScrollViewer.Background=Transparent`，并补无 key 的隐式
  `GridViewColumnHeader` 样式（`BasedOn=Grid.Header`）兜住填充列头。
- 复选框按设计稿 `.chk i` 对齐：16×16 → 14×14，圆角 5 → 4，勾线坐标重排。
- 滚动条统一细轨（6px，悬停 8px）：`Scroll.Thin` 原来是带 key 的孤儿样式、页面无人引用，
  实际全走 Aero 系统模板——白底轨道 + 直角箭头，深色下整条发亮。改为无 key 的隐式样式，
  Thumb 圆角按实际宽度取半（6→3、悬停 8→4，端头始终正圆），并补回 `Track` 的
  `PageUp/PageDown` 透明 RepeatButton，点轨道翻页不丢。
- 胶囊实现从“转换器 + 长 Binding”改为库内 **`KxCapsuleBorder`**（Kaleidoscope.UI V2.4.1
  新增，一行标签，构造即绑半高）：模板 8 处 + 主窗 3 处 + 总览 2 处全部替换；竖向滚动条
  Thumb 用 `Axis="Width"`。语义不变（按钮/输入框/Chip/导航项/进度条），但新页面照抄模板
  即可，不必再记长绑定。
- 接入 **`KxRadiusAudit` 圆角自检**（Debug 构建）：主窗首屏渲染、每次页面切换（ContextIdle
  布局完成后）自动扫可视树，`CornerRadius > min(宽,高)/2` 的 Border 写日志 WARN。已用
  临时 124×30 / 99 的 Border 反向验证能抓到，删除后复测 0 命中。
- 修设置页主题文案不跟随：设置页 VM 被壳层 `_cache` 缓存复用，原来只在“本页按钮”
  里刷新“当前主题/切换到…”，从顶栏 ☀/☾ 切主题时文案停留在旧值。改为 `ShellViewModel`
  作为 `ThemeManager.Changed` 唯一订阅者，转发给缓存的 `SettingsViewModel.RefreshTheme()`
  （VM 自己不订阅静态事件，避免反复重建累积泄漏）。实机双路径验证文案与顶栏图标一致。
- 补齐设计稿缺失页 **「分项目 · 设备问题预览」**（设计稿 `data-page="project"`）：
  左侧设备清单（机台号 / 问题数与倒挂 / 照片 / 装配责任人），右侧选中设备的问题点
  卡片与照片条（演示占位）。数据来自既有的 `ProjectRepository.GetSubprojectDetail`，
  问题明细一次全表快照按机台分组，切设备零查询。总览页「查看问题 →」不再直接跳台账，
  先到这一页看清机台盘面；空状态、待确认归属、无照片分项目都有对应文案与中性色。
  - 补 `StageRules.InvertedDays`（倒挂天数纯函数，界面徽标与统计共用口径）。
  - 补 `Issue.HasPhoto/PhotoText/InvertedText/DesignPlanLine`、`MachineSummary.HasPhoto/
    HasInverted/AssemblyOwnersDisplay`：空串字段（责任人/日期原文）统一在模型里转破折号，
    XAML 的 `TargetNullValue` 拦不住空串。
  - 左侧设备表与总览卡片按钮补 `AutomationProperties.Name/AutomationId`：
    原来十行同名、四个按钮同名，读屏与自动化都定位不到具体设备。
- 补齐设计稿详情页三个弹窗（`dlg-stage` / `dlg-del` / `dlg-photo`）：
  - **更新阶段**：一个窗口填完当前这一段（阶段切换 + 阶段结果四选一 + 实际完成日 +
    责任人 + 调整计划日 + 变更原因 + 阶段备注）。三条纪律落到界面：改计划日则变更原因必填、
    倒挂红条按"保存后的计划"实时算（用 `StageRules.IsPlanInverted` 同口径）且只告警不阻断、
    选"不适用"自动把计划写成 `/`。保存走与阶段卡同一个 `SaveStage`，乐观锁 version 一致；
    `StageDraft` 新增 `ChangeReason`，仓储把它拼进"计划"那条审计的 new 值，留痕能直接读出"为什么改"。
  - **删除问题**：必须手抄问题号才能确认（逐字比对，不匹配则按钮置灰），并把"影响几条留痕 /
    几张照片"如实列出；删除走既有软删除，删完回台账而不是停在已作废的详情页。
    界面明确写"附件原文件保留"，不给做不到的勾选框（AGENTS 第 10 节第 11 条）。
  - **查看现场照片**：左大图 / 右元数据 / 底部上一张下一张；图片按需从磁盘读（`DecodePixelWidth=1600`
    与手机端压缩口径一致），演示附件或未同步时显示占位说明而不弹错误框。详情页附件区新增缩略图条。
  - 三个弹窗统一走 `IDialogService`（新增 `EditStage` / `ConfirmDeleteIssue` / `ViewPhotos`），
    ViewModel 不 new Window；请求/结果用 DTO（`Services/DialogRequests.cs`）穿过接口。
- 数据交换页重构为设计稿的**导入四步向导 + 导出三选一**：
  - 导入：选择文件 → 列映射 → 校验预览 → 确认导入，当前步高亮、已完成的步可点回看；
    解析成功自动进第 3 步，写入成功进第 4 步。列映射页展示探测到的表头行与布局
    （`ImportPreview.HeaderRow/HeaderLayout`，由导入解析器填入）。
  - 导出：范围四选一（全部 / 仅倒挂 / 仅逾期 / 有照片）+ 版式三选一
    （客户原表版式 / 标准明细表 / **统计汇总**）+ 选项。后端 `ExportRequest` 新增
    `Layout` 与 `Scope`，并新增 `WriteSummarySheet`：总览、责任人负载、阶段填写率、
    倒挂清单四段，口径全部复用 `StageRules`。
  - **开始导出按钮固定在右栏底部**：1440×900 下三张卡会把内容顶出可视区，按钮留在滚动区里
    就得先滚到底才能点（自动化也判成 offscreen）。滚动区只放"看"的内容，主操作常驻可见。
  - 修同一分钟内多次导出互相覆盖：文件名撞名时追加 `_2` / `_3`，不再静默覆盖上一份。
  - 导出列表项补 `AccessibleName`（原来三项都叫 `LayoutOption`，读屏与自动化分不清）。
- 库同步修两个缺陷（Sinan 反哺，Kaleidoscope.UI V2.4.2）：
  - `TitleBar` 副标题原先硬编码 72% 白色，只在深色渐变标题条上看得见；Sinan 的浅米色标题条上
    几乎不可读。改为优先取宿主键 `Brush.Shell.TitleSubFg`，取不到回落原白色（老宿主零改动）。
  - `KxRadiusAudit` 不再检查 `KxCapsuleBorder`：它构造即绑半高，单字徽标（18×20，半径 10）
    必然"横向半径 > 宽/2"，但 WPF 钳到 9 画出来正是胶囊该有的样子。不排除的话每块单字徽标
    都报一条 WARN，把真问题淹掉（本轮实测 6 处全是这类误报）。

### 现场端（Flutter）

- 原地替换原 Kotlin + Compose 计划：`mobile/` 改为 Flutter 3.50 / Dart 3.14 工程，
  业务代码全部 Dart；`android/` 只保留模板 `MainActivity` 作为宿主壳。
- 主题 Token `SinanPalette` 明/暗双套，页面只引用 Token，不写死色值。
- sqflite 离线库（问题 / 阶段 / 评论 / outbox），首启种入与桌面端同一份 36 条虚构种子。
- 页面：登录、项目总览、问题台账（搜索/筛选）、问题详情（四阶段编辑、状态覆盖、
  评论、照片占位）、数据交换（待同步数、配对占位）、系统设置（主题/账号/数据）。
- 规则与桌面端同口径：日期原文解析、阶段推导状态、计划倒挂、逾期天数、机台归组。
- `flutter analyze` 0 issue；`test/rules_test.dart` 14 条全过。
- Android 应用名改为“司南”；Gradle 使用腾讯云镜像、Maven 使用阿里云镜像。
- `flutter build apk --debug` 成功：`mobile/build/app/outputs/flutter-apk/app-debug.apk`
  （约 180 MB，压缩前 226 MB；debug 含 arm64-v8a / armeabi-v7a / x86_64 三个 ABI）。
  Gradle 9.5 发行版与 Android 依赖均从国内镜像下载；Kotlin 增量缓存在插件编译时无法关闭，
  已在 `android/gradle.properties` 关闭 `kotlin.incremental`。
- 启动失败兜底：本机数据库初始化异常时显示“本机数据读取失败 + 原因 + 重试”，
  不再永远停在启动页。

### 文档

- `AGENTS.md` 技术栈与目录结构改为 Flutter / Dart，明确 `mobile/android/` 只是宿主壳。
- `AGENTS.md` 新增 8.1「圆角与裁剪（WPF 落地口径）」与 8.2「表格的系统默认白底」：
  99 变椭圆的根因、同心半径=外层−间距、`KxRoundedClip` 会裁掉卡片柔影、放大四角目检、
  GridView 填充列头的系统白底等实测结论，供后续页面改造直接对照。
- `AGENTS.md` 8.1 第 1 条改为“用库内 `KxCapsuleBorder` + Debug 期 `KxRadiusAudit` 自检”：
  根因不再靠纪律记，而是固化成控件与可执行审计（Kaleidoscope.UI V2.4.1）。
- Kaleidoscope 库同步反哺：新增 `KxCapsuleBorder` / `KxRadiusAudit`、补 `UiConverters.CapsuleRadius`
  单例；库 README「控件复用索引」加胶囊场景行与自检说明，库 AGENTS 铁律 7 补胶囊纪律。
- Kaleidoscope.UI V2.4.2（Sinan 第二轮反哺）：`TitleBar` 副标题改走键 `Brush.Shell.TitleSubFg`
  （原先硬编码白色，浅色标题条上不可读）；`KxRadiusAudit` 排除 `KxCapsuleBorder`
  （单字徽标必然触发误报）。司南明/暗两套主题各补该键。
- `AGENTS.md` 第 14 节文件导航补 `SubprojectPage.xaml`。
- `README.md` 进度、目录、构建说明同步更新。
- 补齐设计稿最后两个弹窗，桌面端弹窗族 11/11 完成：
  - **确认导入 / 冲突处置**（`dlg-import-conflict`）：顶部五格真实计数（将写入 / 按 ID 更新 /
    倒挂提示 / 文本计划 / 机台号异常）+ 冲突行**逐条**选"用原表覆盖 / 保留系统值" + 写入范围三选一。
    冲突行显示库内真实问题号（`P7391-013`）与"系统 / 原表"逐阶段差异，不再让用户盲选。
    逐条决策写回 `ImportRowPreview.TakeExcelOnConflict`，`Apply` 按"人选的 > 策略"优先级处置。
    "仅新增"下明确提示逐条勾选本次不生效（原来勾完发现全被跳过，像坏了）。
  - **开始导出**（`dlg-export`）：文件名可改、保存目录、工作表清单、"将包含"逐条按真实数据生成
    （没照片就不写照片那条）、导出后打开文件夹可选。文件名经 `SanitizeFileName` 剥路径与非法字符，
    只允许落在 `archive\exports`。
  - 导入解析补 `InvertedRowCount` / `TextPlanRowCount` / `MachineWarningCount` 与
    `ExistingIssueId`；`ImportPreview.ConflictRows` 供弹窗取数，口径全复用 `StageRules`。
- 本轮又修 3 个真实缺陷：
  ① 冲突列表用原表序号拼问题号（显示 `P7391-001`），与库内真实 ID（`P7391-006`）不符，
     用户拿去台账搜不到 → 改用 `ExistingIssueId`；
  ② 冲突摘要逐字段追加，出现"方案确定计划 12.2 / 方案确定责任人 朱工 等 8 项"这种同阶段拆开
     还被截断的读法 → 改为按阶段归并成"方案确定（朱工 12.2）"；
  ③ 写入范围用自绘 `RadioButton` 模板，UIA 拿不到可见性（读屏与自动化都点不到）
     → 改用 `ListBox + SelectionItemPattern`，与导出页"范围/版式"同一套交互。

### 待办

- 双端同步联调（配对码 + Bearer + 冲突人工选择）。
- 现场端拍照与附件上传。
- iOS 构建需 macOS。
