# 变更记录

## 开发期（2026-11-27）手机端去掉启动加载页（手机 v0.0.5）

### 问题
- 冷启动先出现全屏「司南图标 + 转圈 + 正在读取本机数据…」。本地 SQLite 通常几百毫秒
  就绪，这一页既拖慢首屏又没有信息量，用户要求删掉。

### 手机端
- `main.dart` 删除 `_BootScreen`，首帧直接进登录页；数据库/种子仍在后台 `state.init()` 里加载，
  读完 `notifyListeners()` 自动刷新（登录页「已同步 N 条」会跟着更新）。
- 登录不依赖初始化：`AppState.login` 本来就是内存校验，不查库；极端情况下先登录会短暂
  显示空列表，初始化完成即刷新。
- 读库失败保留 `_LoadErrorScreen`（可读原因 + 重试），不做静默空页面。
- 删除 `AppState._loading` / `loading` getter——唯一使用者是被删掉的启动页。
- 登录页品牌图标换成系统启动图标（`logo-2-ladle` 司南磁勺：橙色地盘 + 白色磁勺 +
  半透明圆环）的圆形矢量版，与 Android 12 启动画面那一枚一致；账号输入框删除
  「账号」浮动标签，保留人形图标与读屏语义，输入区更干净。
- 版本 `0.0.4+4 → 0.0.5+5`：`pubspec.yaml` 与 `lib/app_info.dart` 同步；`aapt` 反查
  `versionName=0.0.5`、`versionCode=5`、`minSdk 24`、`targetSdk 36`、`INTERNET` 生效。

### E2E 工具（`tools/mobile_e2e/`）
- `ensure_shell` 增加前台包名检测：设备停在桌面/其它应用时主动 `am start` 拉起司南，
  不再盲目按 back；修复覆盖安装后首次运行的「项目准备失败」噪声。
- `--install` 后先启动应用再跑用例，避免包替换触发的进程重启与第一次点击竞争。
- `goto_tab` 已在目标页（且无弹层）时直接复用，减少重复点击与 dump。

### 验证
- `flutter analyze` 0 issue；`flutter test` 22 条全过；`flutter build apk --debug` 成功。
- 模拟器覆盖安装后冷启动截图：直接显示登录页，无「正在读取本机数据」。
- E2E `--cold --module smoke` PASS 2；全量 15 条 PASS 15 / FAIL 0，用时 155.6s
  （优化前 207.6s）。

## 开发期（2026-11-27）手机端 UI 回归脚本（版本不变：桌面 v0.0.8 / 手机 v0.0.4）

### 问题
- 手机端每次人工回归都要「adb 点击 + 截图 + 看图判断」，一轮十几分钟且容易漏步。
  需要一套可复用的真机 UI 用例脚本，改完哪个模块几十秒内就能跑对应回归。

### 新增工具（`tools/mobile_e2e/`，不改业务代码）
- 零侵入方案：Python 标准库 + adb + `uiautomator dump`，不引 `flutter drive`/`integration_test`，
  不需要每次重编译安装；Flutter 文本在 `content-desc`，脚本按文本/正则定位控件。
- `e2e/adb.py`：adb 自动发现、设备选择、shell/exec-out、截图、安装、启停应用。
- `e2e/ui.py`：无障碍树解析（`UiNode`/`UiTree`/`UiSession`）、文本/正则/类型查找、
  轮询等待、点击可点击祖先、ASCII 输入、清空输入、软键盘收起、底栏 tab 点击。
- `e2e/runner.py`：`@case` 用例注册、`Context` 高层动作（登录、切页、切项目、台账重置、
  断言、跳过）、失败留截图 + UI 树、JSON 报告；dump 结果缓存复用（单次 dump 约 2.2 秒，
  复用后冒烟从 54 秒降到 18 秒）。
- `run.py`：`--list / --module / --case / --tag / --cold / --mutating / --install / --device /
  --json / -v`；退出码 0 通过、1 失败、2 环境错误。
- 15 条只读用例：`smoke.*`（登录、四页）、`todo.summary`、`projects.overview /
  consistency / subproject / switch / empty`、`ledger.search / filter / detail`、
  `sync.status`、`settings.open`、`nav.refresh`、`issue.form`（只开表单后取消，不落库）。
- 数字一律从界面实时解析并互相对账（项目页问题/倒挂 = 台账 chip = 筛出条数），不写死演示值；
  空项目自动临时切到有数据的项目，跑完恢复用户原项目。

### 文档与卫生
- 新增 `tools/mobile_e2e/README.md`（环境、用例表、参数、新增用例、常见问题）。
- `mobile/AGENTS.md` 第 7 节补 UI 回归命令，第 8 节交付清单加「改过界面要跑对应模块」。
- `.gitignore` 追加 `tools/mobile_e2e/artifacts/`、`__pycache__/`、`*.pyc`。

### 验证
- 模拟器 `emulator-5554`（1080×2400）全量 15 条：PASS 15 / FAIL 0 / SKIP 0，用时 205.6s。
- `--cold --module smoke`：冷启动停在登录页 → 自动登录 → 四页可达，PASS 2，用时 32.5s。
- 单用例 `--case ledger.filter`：PASS，10.5s。
- 本轮只新增测试工具与文档，未改 Dart/C# 业务代码，两端版本号不变。

## 开发期（2026-11-26）多总项目与切换（桌面 v0.0.8 / 手机 v0.0.4）

### 问题
- 数据库本来就按多项目设计（`projects` 表、`issues.project_id`），但全链路默认取第一条：
  桌面端各页面各查一遍 `GetMain()`，手机端查询不带项目条件。用户要求总项目能新增多个、
  两端都能来回切换，并明确切换入口放在总览头卡"进行中"标签右侧。

### 桌面端
- 新增项目上下文 `Sinan.App/Services/ProjectContext.cs`：当前项目唯一真源，选择持久化到
  `app_meta.current_project_id`，重启回到上次项目；切换后所有页面统一重建。
- `ProjectRepository` 新增 `ListProjects / Get / ListSummaries / CreateProject / CountMachines /
  WriteProjectChange`；`GetOverview / GetDashboard / GetReport / GetSubprojectDetail /
  LastImportDisplay` 支持 `projectId`；机台归组按项目号动态推导。
- 新建总项目自动创建 `01/02/03` 三个分项目，项目号按 `P + 4 位` 校验并拦截重复；
  建议号取已有 `P####` 最大值 +1。
- 总览头卡"进行中"右侧新增「切换项目 ⇄」胶囊按钮：弹窗列表点行即切换，底部提供"新建总项目"。
- 全部 ViewModel 按当前项目过滤：总览、工作台、台账、报表、分项目、详情、新建、数据交换、设置。
  台账问题号统一用 `项目号-三位序号`（`DisplayCode`），面包屑与标题同步跟随。
- Excel 导入预览/写库、导出范围与版式、同步上行项目归属均按当前项目；上行优先级
  `payload.project_id` → 机台号前缀推导 → 主项目兜底。
- `TextUtil` / `SubprojectRules` 泛化：支持任意 `P####` 项目号，保留 P7391 单参兼容入口与
  `P73913118 → 待确认` 旧口径。
- 新增弹窗：`ProjectSwitchDialog`（切换）、`ProjectCreateDialog`（新建）；离屏渲染回归补两个场景。
- 手机新建问题的 `seq_no` 改为**按总项目独立编号**：P7392 的第一条从 `001` 开始，
  不再继承 P7391 的 `040`；切换弹窗列表行补 `AccessibleName`（项目号 + 名称 + 规模 + 当前），
  读屏与自动化可逐行定位。

### 手机端
- `LocalDb` 升 `v4`：新增 `projects` / `subprojects` 表与 `issues.project_id`；升级旧库统一归演示项目；
  首启种子写入 `PRJ-P7391` + `SUB-P7391-01/02/03`（与桌面 id 一致，避免同步重复建档）。
- 新增 `models/project.dart`、`data/project_repository.dart`：项目列表、切换用实时规模、下行 upsert。
- `Rules` 泛化项目号/机台号/归组：`resolveSubproject/isMachineCode/machineSeq` 支持按项目号校验，
  保留 P7391 默认参数兼容既有测试；新增 `projectCodeOfMachine`。
- `IssueRepository` 的 `all/stats/machines/recentAttachments` 全部按 `projectId` 过滤；
  新建问题写 `project_id` 并入 outbox payload；下行 issue 按 `projectId/projectCode` 归属，
  老 change_log 缺项目字段时按机台前缀回推。
- `AppState` 增加项目列表、当前项目、`switchProject`；当前选择存 `sync_meta.current_project_id`。
  同步下行同时处理 `project` / `subproject`，同步完成后重载项目字典。
- 项目页 hero 顶栏新增「切换」入口（底部弹层，点行即切），切换后重载本项目现场照片与分项目数据；
  待办、设置、登录、机台选择、新建问题文案全部改绑当前项目号。
- 手机端不新建总项目：桌面端是主库，新项目创建后经同步下发。

### 验证
- 桌面 `dotnet build desktop/Sinan.sln -c Debug`：0 error / 0 warning；`dotnet test` 210 条全过
  （新增 2 条：多项目下手机新建问题按项目独立编号、连续新建总项目并自动建 01/02/03）。
- 手机 `flutter analyze` 0 issue；`flutter test` 22 条全过；`flutter build apk --debug` 生成。
- 版本：桌面 `0.0.8`、手机 `0.0.4+4`（两端各自独立，只升 patch）。

## 开发期（2026-11-26）局域网同步打通（桌面 v0.0.7 / 手机 v0.0.3）

### 问题
- MVP 唯一整块缺失是联机：桌面端没有监听服务，手机端 outbox 只在本机累积，
  设置页与同步中心都只能显示"未启用"。用户要求补齐项目剩余部分。

### 契约与共享层（`shared/`）
- `shared/api/sync-api.md`：新增同步协议唯一真源（协议版本 `1`）——
  `GET /api/ping`、`POST /api/pair`、`POST /api/sync/push`、`GET /api/sync/pull`、
  `POST/GET /api/attachments/...`；上行结果 `applied / conflict / rejected / duplicate`。
- `shared/db/schema.sql`：`devices` 增加 `platform`、`user_id`；新增
  `sync_receipts(device_id, client_op_id, result_json, created_at)` 做上行幂等。
- 桌面 `Database.SchemaVersion` 升 `2`，启动时按 `PRAGMA table_info` 幂等补列；
  全新库直接按最新 DDL 建表。

### 桌面端
- `Sinan.Data/Sync/SyncModels.cs`：配对码、设备、上行操作/结果、增量变更 DTO。
- `Sinan.Data/Sync/SyncRepository.cs`：配对码生成与一次性消费、令牌 SHA-256 鉴权、
  设备上限 8 台、上行落库（新建 / 阶段 / 状态 / 评论）、回执幂等、按 `change_log.seq`
  增量拉取、附件落盘（`attachmentId` 幂等 + 可选 sha256 校验）。
  - 手机新建问题保留手机 `local-*` ID 作主键，`seq_no` 由桌面按当前最大数字序号 +1 重编三位；
  - 阶段/状态用 `baseVersion` 判乐观锁，落后返回 `conflict` + 桌面当前值；
  - 手机 `saveStage` 顺带的同状态操作直接 `applied`（no-op），不误报冲突。
- `Sinan.App/Services/SyncServer.cs`：内嵌 **Kestrel**（.NET 共享框架，非第三方包）监听
  `5180`，不用 HttpListener/HTTP.sys，非管理员也能绑定局域网 IP；启动失败只记日志，
  设置页如实显示原因。
- 组合根装配 `SyncRepository` + `SyncServer`，退出时释放；登录页「手机端配对」改为显示
  真实监听地址与服务状态；数据交换页删除未绑定的过时同步状态属性（不再出现“当前未监听”）。
- 手机端登录页底部读真实配对配置：已配对显示桌面端地址，未配对如实显示“离线”，
  不再写死“未配对”。
- 系统设置「移动端配对」卡改为真实可用：服务状态、监听地址、6 位一次性码（10 分钟倒计时）、
  已配对设备列表（在线按最近 2 分钟同步请求判定）、移除设备。
- 新增 `SyncRepositoryTests` 8 条：配对码一次性、手机新建重编序号、同 `clientOpId` 幂等、
  阶段/状态冲突、增量游标、附件幂等与 sha 校验、移除设备令牌失效。

### 手机端
- `LocalDb` 升 `v3`：`outbox` 增加 `base_version`；新增 `sync_meta`（地址/设备/令牌/游标/
  上次同步）与 `conflicts`（冲突原文 + 桌面当前值）。
- 新增 `data/sync_client.dart`：地址归一化、ping 校验协议、配对、上行、下行、照片上传，
  全部 8 秒超时；错误转可读 `SyncException`。
- `AppState` 增加同步编排：配对 → 上行（业务操作 → 照片）→ 按游标下行 → 结算队列；
  冲突写 `conflicts` 表；「保留手机」用桌面版本号重发，「保留桌面」用桌面快照覆盖本机。
- 同步中心重写为真实页面：地址/配对码输入、连接状态、立即同步、待传队列、冲突二选一、
  上次同步时间；不写假进度、不伪造"已同步"。
- **版本号语义修正**：手机本地写操作不再递增 `version`——它是主库版本号，
  本地递增会让上行 `baseVersion` 比桌面大，误报冲突。
- `saveStage` 不再顺带入队 `issue/status`：问题状态由主库按同一套 `StageRules` 推导，
  避免两端推导时序不同造成状态来回覆盖。

### 仓库卫生
- 修复根 `.gitignore` 的 `data/` 规则误伤：改为 `/data/`、`/source/` 锚定根目录。
  原规则会匹配任意层级的 `data` 目录，导致 `mobile/lib/data/`（本地库 / 仓储 / 同步客户端）
  与 `desktop/tests/Sinan.Tests/Data/`（4 个数据层测试）整目录被忽略、从未入库；
  别人 clone 后手机端无法构建、桌面测试缺文件。

### 验证
- `dotnet build desktop/Sinan.sln -c Debug`：0 error / 0 warning。
- `dotnet test`：208 条全过（新增 8 条同步数据层用例 + 1 条 HTTP 层集成用例）。
  HTTP 用例真实起 Kestrel、走 ping / 401 / 配对 / 上行幂等 / 下行游标 / 附件上传下载。
- 实机启动桌面端 + 本机 HTTP 脚本走完整链路：ping、配对、配对码单次有效、
  新建上行、幂等重发、下行快照、阶段上行、同状态 no-op、旧版本冲突并带回桌面值、
  照片上传与幂等、坏令牌 401，全部通过。
- 手机端 `flutter analyze` 0 issue、`flutter test` 22 条全过、`flutter build apk --debug` 生成。
- 已知边界：外网访问、桌面端向下推送附件、扫码仍未做；冲突必须人工处理，不静默覆盖。

## 开发期（2026-11-26）数据交换页复刻 + 系统设置页修复（桌面 v0.0.6 / 手机 v0.0.2）

### 问题
- 用户并排目检后指出：登录页关闭按钮停在左右分栏中缝（根 `Grid` 分 52*/48* 两列，
  按钮未设列，默认落进第 0 列的品牌区右缘），未贴整窗右上角。
- 数据交换页（导入 / 导出）与设计稿 p3、p4 差距明显：卡头全部用了「底色 + 下边线」的
  band 风格（设计稿 `.card-h` 是透明底、无分隔线）；步骤条圆点 22px、连接线固定 26px、
  当前步无 3px 光晕；导出范围 / 输出格式的选项没有 `.rd` radio 圆点；导出页左列三张卡
  过高，选项卡被裁；右侧迷你表列宽固定，宽卡右侧留大片空白。

### 改动（`Views/LoginWindow.xaml`、`Views/ExchangePage.xaml`、`Themes/Sinan.Controls.xaml`、
`ViewModels/ExchangeViewModel.cs`）
- 登录页：关闭按钮补 `Grid.ColumnSpan="2"`，保持右上角 `Margin="0,8,10,0"`，不再压在中缝。
- 数据交换顶部：去掉「模式切换 + 最近导入 + 打开数据目录」整张卡片外壳，改为无卡横条——
  设计稿 app-main 的第一块内容就是步骤条，这里保持同样的视觉层级。
- 卡头统一改回设计稿 `.card-h`：透明底、无分隔线、图标 20×20 + 标题 12.5px 700；
  卡底条（`.card-f`）保留 CardWell 通栏，圆角按同心半径 13 → 19（外层 20 − 边框 1）。
- 步骤条按 `.steps` 重写：圆点 20px、当前步外套 26px `PrimaryDim` 光晕、当前步文字加粗变
  `Brush.Fg`、连接线用 `*` 列真正铺满（`margin 0 10px`），外层卡片 `padding 12px 16px`。
- 校验结果空状态重做：42×42 圆角图标框 + 「还没有校验结果」+ 一句说明，替换原来的一行小字；
  有预览时的「N 条提示 / N 条阻断」改绑 `Preview.WarningCount / ErrorCount`，不再写死「0 条」。
- 源文件卡：未选文件时只在卡内保留一个主按钮（原来卡头与空状态各一个，重复且违反
  「一屏一个主操作」）；已选文件后卡头显示「更换文件」。
- 导出范围 / 输出格式：ListBox 项模板补 `.rd` radio 圆环（14px，选中内点 6px 主色）；
  范围「有现场照片的问题」加「演示」徽标、版式「客户原表版式」加「推荐」徽标
  （`ScopeOption.Badge` / `LayoutOption.Badge`，空串不显示）。
- `Sinan.Controls.xaml` 新增 `Switch` 开关样式（设计稿 `.sw`：32×18 轨道 + 13px 圆点，
  选中 `PrimaryDim` 底 + `Primary` 边、圆点右移；禁用但保持开启时只压 0.8 透明度），
  替换选项区原来的「固定」chip；「覆盖原始 xls 文件」仍是禁用开关 + 「不支持」，
  不伪造未实现的能力。
- 导出页左列紧凑化（选项 padding 7,5、行距 4/8、卡头 margin 14,10,14,6），
  1440×900 下选项卡 5 行与说明文字完整可见；仍保留整列滚动（设计稿同样是 `overflow:auto`）。
- 右侧迷你表「存在问题 / 解决措施」改 `1.1* / 1*` + `MinWidth 150/140`，多余宽度由这两列
  吃掉，表格铺满预览卡；规则说明（回写 / 照片）移出滚动区、常驻卡底上方。
- 导入页两列比例按设计稿 `grid-import` 调为 1 : 1.25。

### 验证
- `dotnet build desktop/Sinan.sln -c Debug` 0 error / 0 warning；`dotnet test` 199 条全过。
- ui_smoke 全页截图（明/暗 19 张）：导入 / 导出页逐张目检，1440×900 下步骤条、卡头、
  选项圆点、迷你表对齐正常；四角 6× 放大复查步骤条卡角、列映射卡底条、预览卡底条，
  无破角/露白。
- 圆角自检 `[圆角自检·页面切换]` 全部「通过」，无新增 WARN。
- 功能缺口如实保留：导出范围 4 项（设计稿 5 项）、输出格式 3 项（设计稿 4 项）、
  选项 5 行（设计稿 6 行）、列映射「手动调整」/ 校验「导出校验报告」未实现，
  均未用假控件补齐。

### 系统设置页修复（同日追补，`Views/SettingsPage.xaml`、`Views/SettingsPage.xaml.cs`、
`App.xaml`、`Themes/Sinan.Light.xaml`）
- 问题：①卡片头是透明底 + 无分隔线，与设计稿 `.card.fold .f-h` 的浅底 + 1px 底线不符；
  ②表单标签左对齐、与值之间空一大截；③数据源路径是一行裸文本，设计稿是胶囊只读框；
  ④左侧目录选中项没有 `.setnav a.on` 的 2px 主色左竖条、字号偏大 0.5px；
  ⑤「危险操作」3px 红条写 `CornerRadius="19,0,0,19"` 被 WPF 钳成椭圆（KxRadiusAudit WARN）；
  ⑥左侧目录不随内容滚动联动，用户滚到「提醒与逾期」时目录还亮着「数据与备份」；
  ⑦按 Alt 出现 WPF 系统焦点虚线框，整块右侧内容区被套一圈（ScrollViewer 获得键盘焦点）。
- 改动：
  - 卡头统一 `Set.CardHead`（`Brush.PanelSoft` 底 + `Brush.SepLine` 1px 底线 + 19px 上圆角），
    图标 20×20 `PrimaryDim` 底、标题 12.5px 700，与设计稿 `.f-h` 同档；
  - 行标签 `Set.Label` 统一 86px、右对齐、11px muted；数据源 5 行路径改为
    `views:KxCapsuleBorder` 胶囊（26 高、`Brush.Input` 底、1px 边、`Padding 9,0`），
    路径单行省略 + ToolTip 全路径；行距压到 6，5 行路径在 1440×900 内不撑高卡片；
  - 左目录项字号 11，条目模板 `BorderThickness` 常驻 `2,0,0,0`（默认透明、选中换
    `Brush.Primary`），只换画刷不改布局，等价设计稿 `inset 2px 0 0 var(--primary)`；
    补 `AutomationProperties.Name="{Binding Label}"`，6 项不再同名；
  - 「当前账号」与「在册人员」合并为「人员与角色」一张卡，「外观」并入「关于与许可」卡底，
    目录 6 项与右侧卡片一一对应，不再出现设计稿没有的独立「外观」卡；
  - 「危险操作」红条改 3px 直角 `Border` + 外层 `views:KxRoundedClip.Radius="19"` 裁剪：
    红条跟着卡片圆角收口，不再被 WPF 分别钳成椭圆，KxRadiusAudit 设置页 WARN 清零；
  - 目录 ↔ 滚动双向联动：点击定位改用 `StackPanel`（滚动内容）坐标系
    （`TransformToAncestor(ContentScroll)` 拿到的是内容坐标，加当前 offset 会滚过头），
    并设 300ms 抑制窗口，避免点到「关于与许可」滚到底后目录立刻跳回「危险操作」；
    手动滚动时反查最后一张卡顶 ≤ offset+16 的卡片回写高亮，滚到顶/底直接给首尾项；
  - `App.xaml` 用 `Style x:Key="{x:Static SystemParameters.FocusVisualStyleKey}"` +
    空 `ControlTemplate` 覆盖全局系统焦点视觉，按 Alt 不再出现虚线框；按钮/列表项原有
    `FocusVisualStyle="{x:Null}"` 保持不动，键盘焦点仍由 hover / selected / 主色描边反馈。
  - `Sinan.Light.xaml`：`Brush.PanelSoft` 由 `#94FFFAAF`（偏黄绿，色值笔误）按设计稿
    `rgba(255,250,242,.58)` 改为 `#94FFFAF2`，卡头浅底恢复暖白。
- 验证：`dotnet build` 0 error / 0 warning；`dotnet test` 199 条全过；ui_smoke 明/暗
  各一轮截图逐张目检，设置页卡头、右对齐标签、路径胶囊、目录竖条与设计稿一致；
  圆角自检 `[圆角自检·首屏渲染/页面切换]` 全部「通过」，无 WARN；路径胶囊两端、
  目录竖条、危险操作红条按 5× 放大目检无破角/露白。
- 说明：设置页比一屏长，点击靠下的「移动端配对 / 关于与许可」时页面只能滚到底
  （实测最大 offset 677 < 卡片内容坐标 872），这是页面长度决定的正常行为，不额外留白。

### 版本
- 桌面端：`0.0.5 → 0.0.6`（`desktop/src/Directory.Build.props`）；手机端保持 `0.0.2`。

## 开发期（2026-11-26）全站字号与模块比例对齐设计稿 + 四层柔光背景（桌面 v0.0.5 / 手机 v0.0.2）

### 问题
- 用户并排目检设计稿与实机后反馈两点：①背景渐变与设计稿不一致——设计稿左上暖橙、
  右上淡青、底部暖橙收口，实机光斑方向相反且半径只有 420–620px；②整屏字号与模块
  比例偏小，与设计稿并排看明显"缩水"，且不只是总览页。
- 逐页量测后确认差异不是"整体统一偏小"：总览页 hero 标题 18（设计稿 19–20）、
  分项目卡数字 15（设计稿 16）、标签 9.5（设计稿 10.5）偏小；但工作台 KPI 数字 30
  （设计稿 22）、四阶段数字 30（设计稿 25）反而偏大，台账行高 41、详情阶段行高 50
  也比设计稿 32 / 40 更松。因此按"逐元素校准"处理，不做全局等比放大。

### 改动
- `Themes/Sinan.Light.xaml` / `Sinan.Dark.xaml`：背景柔光重写为设计稿 `extra.css`
  598–605 行的四层——左上 1100×700 琥珀 26%（暗 24%）、右上 900×640 深青 16%（20%）、
  底部 1000×720 暖橙 18%（14%）、整窗 135° 线性薄染（明 8%/5.5%，暗 9%/9%）；
  新增 `Brush.MeshTint`，径向画刷改用相对比例 Center/Radius，铺满即等价 CSS。
- `MainWindow.xaml` / `LoginWindow.xaml`：光斑从"主体区三个 Ellipse 手摆位置"改为
  跨全窗四个 `Rectangle` 依次叠放，顺序与 CSS 一致（tint → A → B → C），顶栏/底栏
  半透明透光；登录页同步铺四层，品牌区渐变对齐 `.login-brand`。
- 全局字号 Token（明暗同值）：H1 19→20、H2 12.5→13.5、Body 13.5→14、Small 11.5→12、
  Tiny 10.5→11；14 个页面/弹窗内写死的 FontSize 统一上调一档（KPI 大数字单独控制）。
- `OverviewPage.xaml`：hero 卡 padding 16,14、seal 44→52、标题 18→20、KPI 数字 19→20；
  分项目卡 padding 15,13、数字 15→17、标签 9.5→10.5；图表区高 424→440、左右比例
  2.95→2.9、donut 160→170；卡片间距 12→14。
- `DashboardPage.xaml`：焦点卡与 4 张 KPI 卡按设计稿放大（图标 38→42、KPI 数字 22→24），
  四阶段内卡数字 25→27、责任人条形行距与进度条加粗；数据待处理行高 30→32，
  整页在 1440×900 内刚好一屏（底部不再压到 Dock）。
- `IssueListPage.xaml`：筛选卡 padding 14,10、搜索/下拉/按钮高 30、chips 行距 6；
  行单元格 padding 8,2、标题 12.5，行高约 41→40。
- `IssueDetailPage.xaml`：摘要卡 padding 16,13、seal 50、标题行高 23、chips 行距 7；
  阶段行 padding 0,6、阶段备注卡 12,8，行高约 50→45。
- `Sinan.Controls.xaml`：表头高 36→32；Card 玻璃渐变沿用暖白 + 亮边三层，不加 Effect。

### 验证
- `dotnet build` 0 error / 0 warning；`dotnet test` 199 条全过。
- ui_smoke 全页截图（明/暗 19 张）：总览、工作台、台账、交换、报表、设置、详情、
  分项目、两个弹窗逐张目检；1440×900 与 1280×800 各看一次，详情页卡底主操作常驻可见。
- 圆角自检 `[圆角自检·…]` 全部"通过"，无 WARN。

### 版本
- 桌面端 `0.0.4 → 0.0.5`（`desktop/src/Directory.Build.props`）；手机端保持 `0.0.2`。

## 开发期（2026-11-26）详情页卡底操作被底部 Dock 遮挡修复（桌面 v0.0.4 / 手机 v0.0.2）

### 问题
- 问题详情页把整页（摘要卡 + 主体）放在一个 `ScrollViewer` 里，1440×900 下内容比可视区
  高约 50px；卡底「编辑主信息 / 填写阶段 / 删除」只露出一半、紧贴底部悬浮 Dock，
  看起来像被导航条压住。1280×800 与编辑态展开时更明显。

### 改动（`IssueDetailPage.xaml`，对齐设计稿 p2 的固定一屏结构）
- 根 `ScrollViewer + StackPanel` 改为 `Grid` 固定行：摘要卡 Auto、编辑区 Auto、
  就地提示 Auto、主体 `*`。
- 左卡按设计稿改为「卡头 Auto / 内容区 `*`（内部 `ScrollViewer`）/ 卡底操作 Auto」，
  「填写阶段」等操作常驻可见，阶段行 / 备注 / 规则在卡内滚动。
- 右栏倒挂 / 机台 / 留痕改为内部滚动（三行 Auto），小窗口与编辑态展开时不溢出。
- 删除右栏底部冗余的「数据来源」卡：源行/批次已在「修改留痕」的创建记录与摘要卡
  副标题中显示，重复展示会把右栏撑出滚动条。

### 验证
- `dotnet build` 0 error / 0 warning；`dotnet test` 199 条全过；脱敏扫描 PASS；
- ui_smoke 明/暗全页截图无回归；详情页在 1440×900、1280×800、明/暗主题、编辑态展开下
  逐张目检：卡底三个操作始终完整可见，左卡与右栏各自内滚，底部 Dock 不再遮挡；
- 圆角自检通过（历史遗留的「3px 进度条圆角」WARN 与本轮无关）。

### 版本
- 桌面端 `0.0.3 → 0.0.4`（`desktop/src/Directory.Build.props`）；手机端保持 `0.0.2`。

## 开发期（2026-11-25）桌面端文本清晰度整改（桌面 v0.0.3 / 手机 v0.0.2）

### 根因
- WPF 分层窗口（`AllowsTransparency=True`，`WS_EX_LAYERED`）会强制关闭 ClearType，
  整个窗口的文字退化为灰度抗锯齿；100% DPI 下 9~11.5px 中文明显发虚
  （登录页数据文件路径、演示账号、数据目录最典型，其他页面同样受影响）。
- 第二层根因：`DropShadowEffect` 会把"元素 + 整棵子树"先渲染到离屏位图再合成，
  位图面同样不支持 ClearType。实测同一登录卡片：有 Effect 时文字亚像素色偏指标 1.32，
  摘掉后 5.84（与卡片外持平）。两层叠加导致卡片内文字比卡片外更糊。

### 改动
- 主窗 / 登录窗 / 6 个业务弹窗：override `UseLayeredWindow => false`，关掉分层窗口，
  改走 `SetWindowRgn` 物理圆角（半径取 `Fn.ShellRadius=18`）恢复 ClearType；
  非分层窗口设主题底色，最大化时清除 Region、还原重挂、跨屏 DPI 变化重算。
- `Kaleidoscope.UI`：`ChromeBorderlessWindow` 新增 `UseLayeredWindow` 虚属性与非分层分支；
  `ChromeDialogWindow` 非分层分支补 `DpiChanged` 重算 Region、非分层时不再给骨架卡片挂
  `Fx.WinShadow`（投影本就被 Region 裁掉，挂 Effect 只会把弹窗文字位图化）。
- **全局摘除含文字容器的 Effect**：`Card` 样式、`Btn.Primary`（hover 改为
  `Brush.PrimaryGradHover` 提亮）、`Nav.Item` 选中态、ComboBox 下拉 Popup、底部导航胶囊；
  登录页品牌印章（纯图形、无文字）保留 `Fx.PrimaryGlow`。柔影由页面底层柔光斑承担。
- 明/暗主题补 `Brush.PrimaryGradHover`；`Brush.CardWell` 改完全不透明
  （明 #80FCF7EF→#FCF7EF，暗 #0AFFFFFF→#2E2218），表格/表单文字恢复 ClearType。
- 全局：窗口根加 `UseLayoutRounding=True` + `TextOptions.TextFormattingMode=Display`；
  明主题 `Brush.Muted` #7D6E5E→#6B5C4B、`Brush.Faint` #A89A88→#8F806C。
- 登录页：数据文件路径 10→10.5、演示账号 9.5→10.5、数据目录/首次登录提示 10→10.5、
  底部说明 10.5→11、品牌副标题/说明 11→11.5，数字标签 10.5→11。

### 验证
- `dotnet build desktop/Sinan.sln -c Debug` 0 error；
- `dotnet test desktop/tests/Sinan.Tests/Sinan.Tests.csproj -c Debug` 199 条全过；
- `tools/ui_smoke` 明/暗 20+ 张截图逐页目检：登录、总览、工作台、台账、数据交换、
  报表、设置、详情、分项目、新建/导出弹窗；四角 3x 放大目检，圆角自检通过；
- 像素级抽检：登录卡文字亚像素色偏 1.32→5.84、台账表格 0.62→5.40、暗色表格 5.54，
  与顶栏无 Effect 区域（11.51）同一量级，确认 ClearType 已恢复；
- 实机验证最大化/还原、1280×800 最小尺寸、弹窗开合，Region 圆角与布局均正常。

### 版本
- 桌面端 `0.0.2 → 0.0.3`（`desktop/src/Directory.Build.props`）；手机端保持 `0.0.2`。

## 开发期（2026-11-25）双端界面按设计稿逐页对齐（桌面 v0.0.2 / 手机 v0.0.2）

对照 `docs/ui_design_mockups.html`（单方案 C 晨光现场 · 精致玻璃态）逐页核对并重构，
统计数字全部实时查询，未实现的能力一律标注"未启用 / 待接入"，不放假开关与假同步状态。

### 桌面端（WPF）
- 全局壳层：玻璃态背景、侧栏与顶部胶囊、卡片圆角/间距/字号按 `docs/_build/extra.css` 统一。
- 项目总览：新增自绘 `DonutChart` 问题分布、分项目卡片、阶段填写率、照片分布；
  新增 `PaletteBrushConverter` 把 Token 颜色交给 XAML 绑定；数字来自 `ProjectRepository` 实时查询。
- 工作台：今日待办与风险清单按设计稿重排，KPI、阶段进度、负责人视图同步调整。
- 问题台账：筛选条、列宽、风险标签、行高按设计稿；详情入口与双击行为保留。
- 问题详情：改为只读摘要 + 四阶段只读行 + 「填写阶段」弹窗，四阶段仍单独建表，
  保存继续走乐观锁与审计，不在页面里另写状态推导。
- 分项目：设备表改 `ListBox` 自绘，机台分组、问题行与展开态对齐设计稿。
- 数据交换：保持「一个入口、两种模式」；导出范围 4 种、版式 3 种；
  设计稿里的「照片包 ZIP / 覆盖原文件」如实标注**不支持**。
- 统计报表 / 系统设置：左侧固定目录 + 右侧卡片纵向排列，滚动定位；
  危险操作卡说明 MVP 不提供一键清空。
- 登录页：补「演示账号：engineer / 123456 · admin / 123456」提示条（单行不换行）。

### 手机端（Flutter）
- 登录：文案改「工号登录」，补「未配对 · 数据先存本机」+ 离线 chip、
  「离线可用范围」卡（缓存条数实时取 `state.issues.length`）、版本页脚。
- 待办：第二行 KPI 改「今日已记录」（按 outbox 今日写入实时算）；
  待办卡补阶段日期、「距计划日已过 N 天」与「去处理 ›」；
  最近同步卡如实显示未同步，点「查看 ›」切到同步 tab。
- 台账：筛选 chips 带实时计数（全部 / 倒挂 / 文本 / 有照片 / 逾期）+ 分项目第二行；
  卡片改为「倒挂 / 逾期 + 机台 + 状态」+ 标题 + 措施摘要 + 设计/装配阶段摘要。
- 详情：四阶段压成四行只读列表，点行进「推进阶段」bottom sheet
  （阶段分段 + 阶段结果 + 责任人/计划 + 实际完成 + 备注）；
  倒挂用一句话说明「设计晚于装配 N 天」；现场记录显示「真实 / 总数 张」并标注含演示；
  底部固定「扫码 + 推进阶段」，扫码未接入时明确提示可用台账搜索或机台列表。
- 新建：机台卡已选态「已选择 / 重新选 ›」；字段顺序改为存在问题 / 解决措施 /
  责任阶段+责任人 / 计划完成+状态 / 现场照片；照片 tile 显示真实文件名、大小与「已压缩」；
  提示条使用真实待传条数（`pendingOutbox + 1`）。
- 机台：标题「扫码选机台」；列表从「最近机台」改为「全部机台」，
  每台补真实最近更新日期（`MAX(updated_at)`）；扫码位明确「相机扫码待真机接入」。
- 同步：按设计稿重组为上行队列 / 下行缓存 / 同步规则 / 提醒 / 上次同步 / 移动端配对；
  队列逐条显示真实 outbox（机台 + 问题 + 操作 + 时间），不伪造已连接、上传进度或上次同步时间。
- 项目 / 分项目：总览 KPI、分项目卡片、阶段填写率、机台分组与问题行按设计稿。
- 删除未被引用的 `overview_screen.dart`（项目 tab 实际使用 `ProjectListScreen`）；
  `Rules` 新增 `hasTextPlan()`、`invertedDays()`，`StageCodes` 新增 `shortLabel()`。

### 版本
- 桌面端 `0.0.1 → 0.0.2`（`desktop/src/Directory.Build.props`）。
- 手机端 `0.0.1+1 → 0.0.2+2`（`pubspec.yaml` + `lib/app_info.dart` 两处一致）。

### 验证
- `dotnet build desktop/Sinan.sln -c Debug`：0 error。
- `dotnet test desktop/tests/Sinan.Tests/Sinan.Tests.csproj -c Debug`：199 条全过。
  `UiRenderTests` 的页面独有文案 marker 跟随新界面更新（旧「优先处理清单」
  「四阶段完成率」已不存在，改为「数据体检」「计划时间分布」等），避免测试假红。
- `tools/ui_smoke`：登录页 + 全部页面明/暗截图生成，页面切换圆角自检均「通过」；
  修复导出弹窗截图（数据交换是「一个入口、两种模式」，须先切导出模式再点导出）。
- `flutter analyze`：0 issue；`flutter test`：22 条全过；`flutter build apk --debug` 成功。
- Android 模拟器（1080×2400）明/暗主题逐页目检：登录 / 待办 / 项目 / 分项目 / 台账 /
  详情 / 推进阶段弹窗 / 新建 / 机台 / 同步 / 设置。
- README 配图全部按新界面重截：桌面 15 张（`docs/images/`）、手机 7 张
  （`docs/images/mobile/`）；手机登录图原先误截成启动闪屏，已改为真实登录页。
- `check_readme.py . --ends desktop,mobile`：NG=0 WARN=0（桌面 15 图 / 手机 7 图）。
- `python tools/desensitize_scan.py`：通过（仅 P7391 虚构种子与角色化假名）。
- 临时脚本与截图已清理。

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
