# 司南 · 现场端（Flutter）

P7391 安装调试问题闭环系统的 Android 现场端。桌面端（`../desktop/`）是唯一主库，
本端是**离线副本 + 待传队列**：现场先写本机 SQLite，联网后按 outbox 补传。

## 环境

- Flutter 3.50 / Dart 3.14
- Android SDK 36（`flutter doctor` 可检查）
- 依赖：`sqflite`、`path_provider`、`shared_preferences`、`image_picker`、`flutter_localizations`

## 版本兼容（从 v0.0.1 起）

| 项 | 值 | 口径 |
|---|---|---|
| 手机端版本 | `0.0.2+2` | 与桌面端各自独立升版，只动第三位；唯一来源 `pubspec.yaml`，界面只读 `lib/app_info.dart` |
| Android 最低 | API 24 / Android 7.0 | `minSdk`，Flutter 3.50 引擎下限，主流现场手机都能装 |
| Android 目标 | API 36 / Android 16 | `targetSdk`，行为适配目标，**不是装机上限** |
| Android 编译 | API 36 | `compileSdk`，与 Flutter 引擎一致 |
| iOS 最低 | 15.0 | `IPHONEOS_DEPLOYMENT_TARGET`，iPhone 6s 及以后 |

- **Android 没有 `maxSdk`**：`<uses-sdk android:maxSdkVersion>` 是 Android 1.x 时代的历史字段，
  早已废弃且 Google Play 不认；能装到哪一版由设备厂商的系统版本决定，App 侧只声明
  `minSdk`（下限）和 `targetSdk`（适配到哪一版）。
- **iOS 也没有“最高版本”**：只有 deployment target 当下限，上限由 iOS 版本与硬件能力决定；
  如需限制硬件能力（例如必须带相机），用 `UIRequiredDeviceCapabilities`，不是版本号。
- 三个 Android SDK 值显式写在 `android/app/build.gradle.kts`，没有沿用 `flutter.minSdkVersion`
  默认值——避免 Flutter 升级时默认值漂移，悄悄抬高最低版本把现场旧机踢出安装范围。
- 局域网同步走 `http://<IP>:5180`（无 TLS）：Android 9+ 要 `usesCleartextTraffic`，
  iOS 要 `NSAllowsLocalNetworking` + `NSLocalNetworkUsageDescription`，两个清单都已配好。

## 构建

```powershell
# 国内镜像（可选，本机网络下必需）
$env:FLUTTER_STORAGE_BASE_URL = 'https://storage.flutter-io.cn'
$env:PUB_HOSTED_URL = 'https://pub.flutter-io.cn'

flutter pub get
flutter test          # 纯规则回归，22 条
flutter analyze       # 应为 0 issue
flutter build apk --debug
```

`image_picker` 必须用官方源安装：镜像缺 `image_picker_android`，会解析失败。
`flutter pub get` 前去掉 `PUB_HOSTED_URL` / `FLUTTER_STORAGE_BASE_URL` 两个环境变量即可。

Gradle 已配置腾讯云镜像，Maven 已配置阿里云镜像，见
`android/gradle/wrapper/gradle-wrapper.properties` 与 `android/settings.gradle.kts`。
iOS 工程保留但本机无法构建（需 macOS）。

## 图标

启动器图标与桌面端同一枚司南磁勺：五档 `ic_launcher.png`
（`android/app/src/main/res/mipmap-*`）+ 自适应图标
`mipmap-anydpi-v26/ic_launcher.xml`（橙色底 `#C5540A` + 透明前景只放白勺与地盘环）。
源图是桌面端 `desktop/src/Sinan.App/Assets/app.ico`，改图标时两端一起换。

## 目录

```
lib/
├── main.dart              ← 应用根：主题、登录态、语言
├── app_info.dart          ← 版本号唯一来源（与 pubspec 一致）
├── theme/
│   ├── tokens.dart        ← SinanPalette 明/暗 Token（唯一色值来源）
│   └── app_theme.dart     ← ThemeData
├── models/
│   ├── issue.dart         ← 问题 / 阶段 / 状态
│   ├── attachment.dart    ← 附件（本机文件 + 同步状态）
│   └── rules.dart         ← 日期解析、状态推导、倒挂、逾期、归组、我的待办
├── data/
│   ├── local_db.dart      ← sqflite 建表（v2 含 attachments）+ 首启种子
│   ├── attachment_store.dart ← 拍照/相册、压缩落盘（attachments/{issueId}/）
│   └── issue_repository.dart ← 查询、新建、附件、outbox、机台聚合
├── state/app_state.dart   ← 会话、主题、列表、统计、待传条数
├── widgets/
│   ├── common.dart        ← 玻璃卡片、Chip、阶段点、KPI、空状态
│   └── photo_tile.dart    ← 照片缩略图（缺失文件给明确占位，不伪装）
└── screens/
    ├── login_screen.dart      ← 工号登录（离线可用）
    ├── shell_screen.dart      ← 五槽底栏：待办 / 项目 / ＋ / 台账 / 同步
    ├── home_todo_screen.dart  ← 我的阶段任务、风险 Top3、扫码/拍照入口
    ├── project_list_screen.dart ← 总项目 P7391 + 分项目卡 + 阶段填写率 + 最近照片
    ├── subproject_screen.dart ← 分项目内按机台分组问题
    ├── issue_list_screen.dart ← 台账：搜索 / 筛选 / 倒挂逾期
    ├── issue_detail_screen.dart ← 详情、阶段推进、评论、真实附件 + 拍照
    ├── new_issue_screen.dart  ← 新建问题（离线落库 + 进 outbox）
    ├── machine_select_screen.dart ← 手输机台 + 最近机台（扫码待真机接入）
    ├── sync_screen.dart       ← 待传队列逐条、配对码入口、同步规则
    ├── photo_viewer_screen.dart ← 全屏看大图（左右切换 + 双指缩放）
    └── settings_screen.dart   ← 主题、账号、版本信息
```

## 数据口径

- 与桌面端同一份种子：`assets/seed/issues.seed.json`（36 条 / 31 台，虚构数据）。
- 日期双字段：`due_raw` 存原文，`due_date` 由原文解析；`/`、`已下图`、空白不参与逾期。
- 阶段状态：`not_started` / `in_progress` / `done` / `na`；问题状态由四阶段推导。
- 新建问题：责任阶段初始为 `in_progress`（选了阶段就代表正在做，即使责任人/日期未填），
  序号用 `L001` 占位，同步后由桌面主库重编号。
- 单机台号归组：`P7391101–P7391199` → 01，`…2xx` → 02，`…3xx` → 03；位数异常留“待确认”。
- 演示照片为占位值（12 张 / 6 条），汇总卡片标注“演示”，不冒充真实附件。

## 当前边界

- 移动端暂不直接解析 Excel（`.xls/.xlsx` 由桌面端处理）。
- 配对码入口保留，但同步服务尚未联调，界面不伪造连接成功；outbox 只在本机累积。
- 拍照/相册已接入 `image_picker`（长边 1600px、质量 82）；相机扫码待真机接入，
  当前用“手输机台号 + 最近机台”替代，界面上明确写“相机扫码待真机接入”。
- 附件只存在本机 `attachments/{issueId}/{uuid}.jpg`，上传由后续同步服务承担。
- 登录态不持久化：每次冷启动都要输工号密码（共享现场设备的默认口径）。
  密码框不预填，debug 包同样如此。
