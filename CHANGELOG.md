# 变更记录

## 开发期（2026-11-25）README 多端化：桌面端 / 手机端分节 + 手机实机截图

### 文档结构
- README 新增「两端一览」：桌面端 / 手机端各自面向谁、技术栈、当前状态。
- 「界面速览」拆为 `### 桌面端` 与 `### 手机端`；手机端用 7 张 Android
  模拟器实拍图（1080×2400，`<img width="200">`），桌面端同时补齐登录 / 设置 /
  深色台账三张原孤儿图。
- 「核心能力」拆为「系统级能力（两端共用）/ 桌面端 / 手机端」，避免读者误以为
  两端都支持 Excel 导入导出、或两端都支持拍照。
- 目录树补 `desktop/AGENTS.md`、`mobile/AGENTS.md` 与 `docs/images/mobile/`；
  技术基线、构建小节、当前进度、文档导航的端名统一为「手机端」。

### 截图
- 新增 `docs/images/mobile/`：登录 / 待办 / 项目 / 台账 / 同步 / 详情 / 新建
  共 7 张；用 AVD `Medium_Phone_API_37.0` 无头模拟器 + `adb install` +
  `screencap` 实拍，演示账号登录，全部为虚构演示数据。

### 工具与流程
- 同步升级 `readme` skill 到 V1.3.0：多端识别、多端骨架、移动端截图决策树、
  `check_readme.py --ends` 分端核验、`img_inventory.py` 排除移动端资源目录。

### 验证
- `check_readme.py . --ends desktop,mobile`：NG=0 WARN=0（桌面 15 图 / 手机 7 图）。
- `img_inventory.py . --skip-dir _build --skip-dir branding`：RESULT: OK。
- `python tools/desensitize_scan.py`：PASS。

## 开发期（2026-11-25）版本号规则：两端独立，默认只升 patch

- 根 `AGENTS.md`：版本号政策改为桌面端与手机端**各自独立**；每次迭代默认只升 patch
  （`0.0.1 → 0.0.2 → 0.0.3`），`0.0.9` 之后是 `0.0.10`，不自动进位成 `0.1.0`；
  只有用户明确要求才升 minor/major；只改哪端就只升哪端；根 README 徽章跟随桌面端。
- `desktop/AGENTS.md`：新增 1.1 节，明确唯一来源是
  `desktop/src/Directory.Build.props` 的 `<Version>`，界面经 `AppInfo` 读取。
- `mobile/AGENTS.md`：第 3 节补升级节奏与两处同改要求（`pubspec.yaml` + `app_info.dart`，
  `+N` 与 patch 同步递增）；交付清单的版本核对改为"与 `pubspec.yaml` 一致"，不写死 `0.0.1`。

## 开发期（2026-11-25）AGENTS.md 按端拆分：根总纲 + desktop + mobile

### 文档结构
- 根 `AGENTS.md` 收缩为总纲（约 200 行）：项目定位、事实来源、双端架构与同步边界、
  目录结构、数据模型红线、业务口径、跨端铁律、共享契约变更流程、通用交付清单、
  待确认、根级导航、数据脱敏。
- 新增 `desktop/AGENTS.md`：桌面技术栈、MVVM、Kaleidoscope 复用、WPF 界面 / 玻璃态 /
  圆角 / 白底、弹窗规范、Excel 无损与导出、Windows 运行时数据、桌面构建与验证、文件导航。
- 新增 `mobile/AGENTS.md`：Flutter 技术栈、Android/iOS 版本兼容口径（minSdk 24 /
  targetSdk 36 / iOS 15.0 / v0.0.1）、页面与交互、离线数据与 outbox、手机端构建与验证、
  文件导航。
- 根文件写明阅读路由：只改桌面读「根 + desktop」；只改手机读「根 + mobile」；
  改 `shared/` 两份都读。端内细节不再回流根文件。

### 引用同步
- 桌面代码 / 测试 / XAML 中 20 余处旧章节引用（如「AGENTS.md 第 7 节」）更新为
  `desktop/AGENTS.md 第 2 节` 或 `根 AGENTS.md 第 5/6 节`，避免拆分后指错位置。
- 修正 `Sinan.Controls.xaml` 里指向不存在「自绘控件清单」的旧引用，改为
  `desktop/AGENTS.md 第 3 节（控件复用）`。

### 验证
- `dotnet build desktop/Sinan.sln -c Debug`：0 error（30 条既有的 ImageSharp 漏洞警告）。
- `python tools/desensitize_scan.py`：PASS。

## 开发期（2026-11-25）手机端版本兼容口径锁定 + 版本号回到 v0.0.1

### Android
- `android/app/build.gradle.kts` 显式锁定 `compileSdk = 36` / `targetSdk = 36` /
  `minSdk = 24`（Android 7.0+），不再沿用 `flutter.minSdkVersion` 等默认值：
  Flutter 升级改默认值时，不会悄悄抬高最低版本把现场旧机踢出安装范围。
- `AndroidManifest.xml`（main）补 `INTERNET` 权限（原先只在 debug/profile 里，release 包没有）
  与 `usesCleartextTraffic="true"`（Android 9+ 默认禁明文，而同步服务是 `http://<IP>:5180`）。
- Android 没有 `maxSdk`：`<uses-sdk android:maxSdkVersion>` 已废弃且 Play 不认，
  装机上限由设备系统版本决定，App 只声明下限与适配目标。

### iOS
- 保持 `IPHONEOS_DEPLOYMENT_TARGET = 15.0`（Flutter 3.50 模板默认，iPhone 6s 及以后）。
- `Info.plist` 补 `NSCameraUsageDescription` / `NSPhotoLibraryUsageDescription`，
  拍照/选图不再被系统直接杀进程；补 `NSLocalNetworkUsageDescription` +
  `NSAllowsLocalNetworking`，放行局域网同步而不放开公网明文。
- 桌面显示名由 `Sinan Mobile` 改为「司南」，与 Android `android:label` 一致。

### 版本
- 手机端版本 `0.1.0+1` → `0.0.1+1`，与桌面端 v0.0.1 对齐；`lib/app_info.dart` 同步为 `0.0.1`。
  统一三段式语义化版本，不再出现 `0.01` 这类两段写法（Android/iOS 会按 0.1.0 解释）。
- `mobile/README.md` 增加「版本兼容」表：Android API 24–36、iOS 15.0+ 的取值与含义。

## 开发期（2026-11-25）桌面端 UI 冒烟扩展到弹窗与暗色 + README 实机截图刷新

### 后台静默冒烟（`tools/ui_smoke`）

- 从"6 页浅色截图"扩展到完整一轮：
  - **详情页**：切台账 → 台账默认选中首行 → 点「打开选中问题」（不构造 ViewModel 走捷径，
    走真实用户路径）→ 截图 → 返回；
  - **分项目页**：总览点 `P7391-01` 卡片 → 截图 → 返回；
  - **弹窗**：新建问题 / 开始导出，独立 HWND 用 `EnumWindows` 按进程 + 标题定位
    （模态弹窗期间 UIA 桌面根可能取不到子窗），截完 `WindowPattern.Close`；
  - **暗色轮**：点顶栏主题开关 → 重走 6 页 + 详情 + 分项目 → 切回浅色。
    不重建控件树，因为测的就是"用户点开关"这条真实换肤路径。
- 修两处跨进程竞态：登录窗句柄出现早于控件树可枚举（`EmployeeNoBox` 偶发找不到）、
  登录按钮 `Name`/`IsEnabled` 延迟填充（误判"密码没输进去"）。改成轮询到
  "有文字且可用"再点，超时 6–8 秒。
- 新增 `--write-test` 端到端写入验证：新建 → 落库 → 详情页 → 推进阶段 → 删除二次确认 → 软删除。
  配 `--data-dir` 指向临时库（`SINAN_DATA_DIR`），**不碰用户主库**。实测：
  - 序号 41 自动递补，详情页显示"第 41 项 / P7391-01 / 来源 手工新建"；
  - 阶段弹窗里改计划日不写变更原因 → 保存被拦、弹窗不关、错误提示就地展开；
    补原因后保存成功，摘要 `阶段 0/4 → 1/4`、状态自动推导 `待分派 → 设计中`、
    修改留痕 4 条（状态 / 阶段状态 / 计划日含原因 / 责任人）；
  - 删除确认：未输入问题号时按钮禁用、输入 `P7391-041` 后启用、删除后详情页标题消失。
- 端到端断言避开了两个假绿陷阱：不能用页面里任意「已完成」（四个阶段卡片的下拉选项
  里本来就有这个词），改为断言摘要「阶段 1/4」；保存后先确认弹窗真的关闭，
  否则后续断言会全部打在还开着的弹窗上。
- 圆角自检 67 行全 `通过`、0 WARN。

### 可访问性补充（无行为改动）

- 补 `AutomationId`：`NewIssueButton` / `RefreshButton` / `ThemeToggleButton` /
  `BackButton` / `OpenSelectedIssueButton` / `IssueTable` / `NewIssueSaveButton` /
  `NewIssueCancelButton`。图标按钮原本只有 ToolTip，读屏与自动化都只能按动态字符定位，
  这属于真实缺陷。
- 修 `StageDialog` 阶段结果列表项的无障碍名：`ListBoxItem` 原来没有
  `AutomationProperties.Name`，UIA 报的是类型名 `Sinan.App.ViewModels.StageStateOption`，
  四个选项对读屏全叫同一个名字（违反 AGENTS 9.1 第 6 条）。现在绑定 `Label`（未开始 /
  进行中 / 已完成 / 不适用）。这是端到端验证实际撞出来的，不是靠看代码发现。
- 测试工程关闭 xUnit 跨集合并行（`AssemblyInfo.cs`）：`TempDb.Dispose` 必须
  `SqliteConnection.ClearAllPools()` 才能删 WAL 文件，而这是进程级操作——
  并行时 A 组清池会把 B 组正在用的连接一起关掉，表现为随机 1 条失败且无法复现。
  串行后 3 轮连跑 199/199 全过，总时长仍约 10 秒。

### 文档与截图

- `docs/images` 13 张实机截图全部按新图标（司南磁勺）重出，另补详情页与分项目页暗色两张；
  登录页 56×56 完整徽标、顶栏 26×26 勺体均已在 4–6 倍放大下目检。
- README 同步：现场端页面清单由 6 页更正为全部页面、规则回归 14 → 22 条、
  桌面端 199 条自动化与 UI 冒烟从"待补"移到已完成、已知限制改为
  「相机扫码待真机接入」「附件上传随同步服务」「手机端登录态不持久化」。

## 开发期（2026-11-25）现场端（Flutter/Android）功能闭环 + 实机验证

### 页面与交互

- 五槽底栏（待办 / 项目 / ＋ / 台账 / 同步）落地，系统设置从顶栏账号菜单进入。
- 待办页：我的阶段任务、计划倒挂、本机待传、本机新建四张 KPI；补设计稿
  `m-home` 的分项目快捷 chips（`P7391 总览` 切项目 tab，分项目直达设备问题页）；
  离线角标按设计稿用 warn 色「离线缓存」。
- 项目页：hero 只统计**正式分项目**（`待确认` 是未归组桶，单独虚线卡片 `P7391-??`），
  分项目卡补倒挂徽标、压紧卡片高度（修 `BOTTOM OVERFLOWED` 2.3px）。
- 分项目页：机台卡倒挂徽标不再重复渲染（设计稿只在机台号后出现一次）。
- 新建问题：责任阶段初始 `in_progress`，其余阶段 `not_started`——修掉
  「状态=装配中但四阶段全未开始」的自相矛盾；「保存并继续」按文案语义
  保留机台、清空描述/措施/责任人/日期/照片，停留本页。
- 详情页：照片指标优先取真实附件数，无附件且种子有演示张数时才标「（演示）」，
  0 张不再标演示；缩略图角标改拍摄时间，不再显示 `scaled_xxx` 内部文件名。
- 设置页：「演示数据」改为「本机数据」（新建后会混入真实记录），文案区分
  36 条虚构种子与本机新建记录；版本号统一读 `lib/app_info.dart`，与 `pubspec.yaml` 一致。
- 玻璃卡顶缘高光改为两端淡出的渐变，暗色 alpha 由 0.5 压到 0.22——
  原实心白线在深底上像一根刺眼横杠，与"玻璃"相反。

### 数据与规则

- `attachments` 表（schema v2 + 迁移）、`AttachmentStore`（长边 1600px、质量 82）、
  新建问题进 `outbox`、同步页逐条展示待传队列并在新建后立即刷新。
- `Rules` 补 `isMachineCode`、`attachmentRelativePath`、`myStageTasks`、
  `attentionOrder`、`initialStageState`；`SubprojectSummary` 增 `inverted` 汇总。
- 相机扫码不引入 `mobile_scanner`：当前用「手输机台号 + 最近机台（31 台）」，
  界面明确写「相机扫码待真机接入」，不伪造扫码成功。
- 登录页不再预填密码：debug 包也必须真登录，任何人拿到安装包点一下就进系统不允许。

### 图标与构建

- Android 启动器图标换成与桌面端同一枚磁勺：五档 `ic_launcher.png` +
  自适应图标前景层（透明底，仅白勺与米色地盘环）+ 橙色背景 `#C5540A`。
- 修 Flutter 增量构建被 daemon 锁拖到 28 分钟的问题：查明是 `gradlew --stop`
  后全量重编 + `kotlin.incremental=false`，正常增量构建回到 12–35 秒。

### 验证

- `flutter analyze` 0 issue、`flutter test` 22 条全绿、
  `flutter build apk --debug --target-platform android-x64` 成功。
- 模拟器（Medium_Phone_API_37.0）实机走通：登录 → 待办 → chips 跳分项目 →
  新建问题（保存并继续）→ 台账搜索 `L001` → 详情阶段行 → 同步页队列 →
  项目页统计 → 设置页明暗切换 → 启动器图标。
- 落库核对：新问题 `L001` 归组 `P7391-01`、`assembly=in_progress` 其余三阶段
  `not_started`、`outbox` 一条 `create`、本机共 37 条。

## 开发期（2026-11-24，并入 v0.0.1 首发）应用图标三面换标 + 199 条自动化回归 + 后台静默 UI 冒烟

### 图标

- **应用图标定为 `logo-2-ladle`（司南磁勺）**，三面同时换标：
  1. exe 内嵌（`Assets/app.ico`，16–256 多帧）；
  2. 运行窗口 `Window.Icon`（`MainWindow` / `LoginWindow`）；
  3. 界面品牌位（顶栏 26×26 只画勺体，登录页 56×56 用完整版地盘环 + 磁勺，
     两者均为 `Viewbox + Canvas` 矢量绘制，颜色取主题 Token `#D97706→#F59E0B` + 青 `#0F766E`）。
- 删除 `logo-1-compass` 转换产物，保留 6 款 SVG 候选源；品牌位已 6 倍放大目检。

### 测试工程（新增 `desktop/tests/Sinan.Tests`）

- **199 条 xUnit 回归全绿**（逻辑 198 + UI 渲染 1），每个用例独立临时库，
  真实执行 `shared/db/schema.sql` + 36 条种子，不使用内存 SQLite。
- 覆盖：仓储 CRUD / 乐观锁 / 软删除 / 统计口径 / 状态推导 / 日期解析 /
  Excel 三种版式导出与往返 / 脏数据保留 / 导入冲突 / 权限与停用人员。
- 修复 3 个真实 bug：
  - `IssueRepository.LoadIssues` 先拼 SQL 后加参数，导致按状态/关键字/分项目筛选
    全部失效（始终返回全量）；
  - `RawDateParser` 不支持 `2026年3月8日` 的"月"分隔符；
  - Excel 多轮测试断言修正（`P7391399` 归属、NPOI 流重写、`only_new` 语义）。
- **新增 UI 离屏渲染测试** `Ui/UiRenderTests.cs`：单 STA 线程构造真实控件树，
  6 个主页面 × 明/暗双主题 + 登录窗 + 6 个业务弹窗全部出图，断言颜色数、
  页面独有文案与圆角自检；不创建可见窗口、不抢焦点。
  暗色渲染必须在切主题后重建窗口树——被摘出的游离元素收不到
  `Application.Resources` 字典替换通知，会截到"深底 + 浅卡片"假象。

### 后台静默 UI 冒烟（新增 `tools/ui_smoke`）

- 后台静默登录（UIA 设工号 + `AttachThreadInput` 设焦点 + `PostMessage` 输密码 +
  UIA Invoke 按钮），全程不调用 `SetForegroundWindow`，启动后还原原前台窗口。
- `PrintWindow(PW_RENDERFULLCONTENT)` 截图，窗口被遮挡也能截；登录 + 6 页导航 +
  圆角自检一条命令跑完，输出 `00-login` / `00-main` / `01~06` 七张截图。
- 导航改为每轮重抓 UIA 元素、按底栏 Tab 的 `AutomationId` 精确匹配，
  修复"点名称误命中顶栏快捷按钮"导致页面不切换的 bug；"项目总览"首屏不再误报字节相同。

### 界面与运行时修正

- **主题字典改程序集限定 pack URI**：`ThemeManager.Apply` 原先用相对 URI，
  在测试宿主/其它壳里会去错误程序集找 `Sinan.Light.xaml` 而报"找不到资源"。
- **窗口图标改 pack URI**：`Icon="/Sinan;component/Assets/app.ico"`，
  避免非产品进程解析不到相对资源。
- **版本号统一读程序集**：新增 `Services/AppInfo.cs`，登录页、主壳状态条、
  系统设置三处不再硬编码 `v0.0.1`，升 `<Version>` 后界面自动跟随。
- **问题台账搜索框补水印**：设计稿 p2 的「搜索问题 / 机台号 / 责任人…」灰字提示，
  WPF `TextBox` 无 placeholder，用叠层 `TextBlock`（仅空值时显示、不参与命中测试）实现，
  并补 `AutomationProperties.Name`；"打开选中问题"按钮本就按 `HasSelection` 禁用，核对无误。
- 移动端复查：`flutter analyze` 0 issue、`flutter test` 14 条全过（与桌面端测试互不影响）。

### 测试隔离修正

- UI 测试会把 `SINAN_DATA_DIR` 指向临时库，并**不泵 Dispatcher**：
  WPF 的 `Application` 构造函数把 `OnStartup` 排进队列，一旦泵消息就会执行组合根
  （重建真实库、`Show` 登录窗），首次实现时已实测触发并险些污染用户库。

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
