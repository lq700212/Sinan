# AGENTS.md — 司南 · 手机现场端（Flutter）

> 本文件只放 **手机现场端（`mobile/`）** 的规则。项目定位、双端架构、数据模型红线、
> 业务口径、脱敏与编码铁律、版本号、待确认事项见根目录 `../AGENTS.md`，这里不重复。
>
> **只在改动 `mobile/` 时读本文件；改桌面端请看 `../desktop/AGENTS.md`。**

## 1. 定位与边界

- 面向现场的 **Android 端**：离线优先，本地 SQLite 记录，联网后按 outbox 补传。
- **iOS 只做设计支持**：本机没有 macOS/Xcode，无法构建与真机验证；改了 iOS 配置只能做
  语法级校验，并在交付说明里写明"待 macOS 验证"。
- **不做**：Excel 导入/导出（`.xls/.xlsx` 由桌面端处理）、MES 对接、审批流、客户门户、外网穿透。
- **同步尚未联调**：配对码入口已留，outbox 只在本机累积；界面**不得伪造**"同步成功 / 已连接"。
- **扫码尚未接入**：当前用"手输机台号 + 最近机台"替代，界面明确写"相机扫码待真机接入"，
  不伪造扫码成功。
- 登录态**不持久化**：每次冷启动都要输工号密码（共享现场设备的默认口径）；密码框不预填，
  debug 包同样如此。

## 2. 技术栈与目录

| 层 | 选型 | 约束 |
|---|---|---|
| 框架 | **Flutter 3.50 + Dart 3.14** | 只构建 Android；业务代码全部 Dart |
| 架构 | **MVVM**：View / `AppState`（ChangeNotifier）/ Repository / LocalDb | 不引状态管理库；`provider` 都不用 |
| 状态 | `lib/state/app_state.dart` | 只做编排；业务规则在 `models/rules.dart` |
| 本地库 | **sqflite**（schema v2） | 建表口径对照 `shared/db/schema.sql`，只保留现场需要的字段 |
| 配置 | shared_preferences | 主题等轻量偏好 |
| 拍照/选图 | image_picker | 长边 1600px、质量 82 |
| 主题 | `SinanPalette` 明/暗两套 Token | 只允许引用 Token，禁止在页面里写死色值 |
| Android 壳 | Kotlin，仅 `MainActivity` | 不往壳里写业务；权限/清单改动才动 `android/` |

```
mobile/
├─ lib/
│  ├─ main.dart              ← 应用根：主题、登录态、语言
│  ├─ app_info.dart          ← 版本号唯一来源（与 pubspec 一致）
│  ├─ theme/                 ← SinanPalette Token / ThemeData
│  ├─ models/                ← 问题 / 阶段 / 状态 / 附件 / 纯规则
│  ├─ data/                  ← LocalDb（sqflite 建表+迁移+种子）、Repository、AttachmentStore
│  ├─ state/app_state.dart   ← 会话、主题、列表、统计、待传条数
│  ├─ widgets/               ← 玻璃卡片、Chip、阶段点、KPI、空状态、照片缩略图
│  └─ screens/               ← 登录 / 五槽主壳 / 项目 / 分项目 / 台账 / 详情 / 新建 / 机台 / 同步 / 设置 / 看图
├─ assets/seed/              ← 与桌面端同一份 36 条种子
├─ test/                     ← 纯规则回归（当前 22 条）
├─ android/                  ← Flutter Android 宿主壳
└─ ios/                      ← 仅设计支持，本机无法构建
```

## 3. 版本与平台兼容（改清单前必读）

**版本号（与桌面端各自独立）**：`pubspec.yaml` 的 `version` 是唯一来源，界面只读
`lib/app_info.dart`。当前 `0.0.1+1`（`+1` 是 build number，Android `versionCode` /
iOS `CFBundleVersion`）。

- **默认只升 patch**：`0.0.1 → 0.0.2 → 0.0.3`，永远只动第三位；`0.0.9` 之后是
  `0.0.10`，不自动进位成 `0.1.0`。只有用户明确要求才动 minor/major。
- 只改手机端时只升手机版本，桌面端版本保持不动；两端不要求同号。
- 改版本必须同时改两处：`pubspec.yaml` 的 `version: 0.0.x+N` 与 `lib/app_info.dart`
  的 `version = '0.0.x'`；`+N` 与 patch 同步递增（例如 `0.0.2+2`）。
- 升完用 `aapt dump badging` 反查 `versionName`，确认是 `0.0.x`。
- 不用 `0.01` 这种两段写法——Android/iOS 会按 `0.1.0` 解释。

| 项 | 值 | 位置 / 含义 |
|---|---|---|
| Android 最低 | `minSdk = 24`（Android 7.0） | `android/app/build.gradle.kts`，Flutter 3.50 引擎下限 |
| Android 目标 | `targetSdk = 36`（Android 16） | 行为适配目标，**不是装机上限** |
| Android 编译 | `compileSdk = 36` | 与 Flutter 引擎一致 |
| iOS 最低 | `IPHONEOS_DEPLOYMENT_TARGET = 15.0` | `ios/Runner.xcodeproj`，iPhone 6s 及以后 |

- **Android 没有 `maxSdk`**：`<uses-sdk android:maxSdkVersion>` 是历史字段，早已废弃且
  Google Play 不认；装机上限由设备系统版本决定，App 只声明下限与适配目标。
- **iOS 也没有"最高版本"**：只有 deployment target 当下限；要限制硬件能力用
  `UIRequiredDeviceCapabilities`，不是版本号。
- **三个 Android SDK 值显式写死**，不要改回 `flutter.minSdkVersion` 等默认变量：
  Flutter 升级改默认值时，不会悄悄抬高最低版本把现场旧机踢出安装范围。
- **清单权限**（`android/app/src/main/AndroidManifest.xml`）：
  `INTERNET` 必须在 main（release 包不带 debug/profile 清单）；
  `usesCleartextTraffic="true"` 是因为同步服务是局域网 `http://<IP>:5180`，无 TLS。
- **iOS `Info.plist`** 必须保留 `NSCameraUsageDescription`、`NSPhotoLibraryUsageDescription`
  （缺了调用相机/相册会被系统杀进程）、`NSLocalNetworkUsageDescription` 与
  `NSAppTransportSecurity.NSAllowsLocalNetworking`（局域网同步）。桌面显示名是「司南」。
- **edge-to-edge**：Android 15+ 强制，页面必须继续用 `SafeArea` 包内容。
- **16 KB 页**：Flutter 3.50 + 当前 NDK 已满足；发版前可用
  `zipalign -c -P 16 -v 4 app-release.apk` 复核。
- 改这些值后，同步更新 `mobile/README.md` 的「版本兼容」表与本文件；不要只改代码。

## 4. 架构约定

- **View 只留 glue**：页面负责布局与手势；业务判断、筛选、统计一律在 `AppState`/`Rules`。
- **数据访问只在 `data/`**：页面不直接 `openDatabase`，也不拼 SQL；走 `IssueRepository`。
- **状态变更走 `AppState`**：`notifyListeners()` 后刷新，不在页面里各存一份副本。
- **IO 不阻塞首帧**：建库/种子/查询在异步初始化里做；`main.dart` 用 loading / error / 登录
  三态兜住，失败给可读提示，不白屏。
- **规则纯函数优先**：日期解析、状态推导、倒挂/逾期、归组、我的待办都放
  `models/rules.dart`，并在 `test/rules_test.dart` 覆盖；页面里不许再写一套。
- **错误就地展开**：表单校验、保存失败在字段/按钮附近提示，不弹二级窗、不吞异常。
- **颜色只用 Token**：`theme/tokens.dart` 是唯一色值来源；明暗两套都要能看。

## 5. 页面与交互

- 五槽底栏：**待办 / 项目 / 台账 / 同步**（中间是扫码/拍照入口），系统设置从顶栏账号菜单进。
- 页面清单：登录、主壳、待办、项目、分项目、台账、详情、新建、机台选择、同步、设置、看图。
- 设计基准 `390×844`；布局要能容忍 360–430 宽度，不写死单机宽度。
- 每个列表必须有空状态；耗时操作必须有加载态；离线/待传要有明确角标。
- 一屏只保留 1 个主操作；主按钮固定在底部可达区域。
- 演示照片用占位 + "演示"角标，不伪装成真实现场照片；汇总卡片上的演示数字要标注。
- 详情页照片指标优先取真实附件数；0 张时不标"演示"。
- 新建问题"保存并继续"：保留机台，清空描述/措施/责任人/日期/照片，停留本页。
- 图查看器支持左右切换与双指缩放；文件缺失时给明确占位，不显示破图。

## 6. 数据与口径（移动侧）

- `LocalDb` 当前 schema **v2**（附件表 + 迁移）；改表必须同步改 `onCreate`、`onUpgrade`
  和桌面端 `shared/db/schema.sql`，并升版本号。
- 种子：`assets/seed/issues.seed.json` 与 `shared/db/seed/issues.seed.json` 必须是同一份
  （36 条 / 31 台，虚构数据），改完两端同步拷贝。
- 新建问题：`seq_no` 先用 `L001` 占位，同步后由桌面主库重编号；责任阶段初始
  `in_progress`（选了阶段就代表正在做，即使责任人/日期未填）。
- 每次写操作同时落 `outbox`（`synced_at IS NULL` 即待传）；同步页只展示真实队列，
  不预填"已同步"。
- 附件：`attachments/{issueId}/{uuid}.jpg`，DB 只存相对路径；缺失文件明确占位；
  上传由后续同步服务承担。
- 日期与 `/`、`已下图`、空白的区别，逾期只认 `due_date`——全部走 `Rules`，与桌面同源。
- 单机台号归组：`P7391101–P7391199` → 01，`…2xx` → 02，`…3xx` → 03；位数异常留"待确认"
  （完整规则见根第 6 节）。
- 统计数字实时算，不写死演示值。

## 7. 构建与验证

```powershell
# 国内镜像（可选，本机网络下建议）
$env:FLUTTER_STORAGE_BASE_URL = 'https://storage.flutter-io.cn'
$env:PUB_HOSTED_URL = 'https://pub.flutter-io.cn'

cd mobile
flutter pub get
flutter analyze       # 应为 0 issue
flutter test          # 纯规则回归，当前 22 条
flutter build apk --debug
```

- `image_picker` 必须用官方源安装：镜像缺 `image_picker_android`，会解析失败；
  `flutter pub get` 前去掉 `PUB_HOSTED_URL` / `FLUTTER_STORAGE_BASE_URL` 即可。
- Gradle 已配置腾讯云镜像、Maven 阿里云镜像，见
  `android/gradle/wrapper/gradle-wrapper.properties` 与 `android/settings.gradle.kts`。
- 成功标准：`flutter analyze` 0 issue、`flutter test` 全过、
  `mobile/build/app/outputs/flutter-apk/app-debug.apk` 生成。
- **改过版本号/SDK/清单**：用 `aapt dump badging app-debug.apk` 反查
  `versionName`、`sdkVersion`、`targetSdkVersion`、`uses-permission`，不能只看 gradle 文件。
- **改过 `Info.plist`**：用 Python `plistlib` 解析校验（本机无 Xcode），并在交付说明里
  标注"iOS 待 macOS 构建验证"。
- 本机开发机的 Flutter SDK 在 `E:\DevTools\flutter`（不在 PATH 时用
  `E:\DevTools\flutter\bin\flutter.bat`）；其他机器的安装位置以 `where flutter` 为准。

## 8. 交付清单（手机端）

在根目录通用清单之外，逐条确认：

- [ ] 改过 Dart：`flutter analyze` 0 issue、`flutter test` 全过（当前 22 条）、APK 生成；
- [ ] 改过版本号：`pubspec.yaml`、`lib/app_info.dart` 两处一致，且 `aapt` 反查的
      `versionName` 与 `pubspec.yaml` 相同；
- [ ] 改过 SDK/权限：`merged_manifests` 或 `aapt` 里确认 `minSdk 24` / `targetSdk 36` /
      `INTERNET` / cleartext 生效；
- [ ] 改过 iOS 清单：`plistlib` 解析通过，键值齐全，交付说明标注待 macOS 验证；
- [ ] 界面改动：明/暗主题各看一次，空状态 / 加载态 / 离线角标都在；
- [ ] 没有把"同步成功""扫码成功"等未实现能力写成已实现；
- [ ] 演示数字有标注，统计来自真实查询。

## 9. 常用文件导航（手机端）

| 文件 | 作用 |
|---|---|
| `mobile/lib/main.dart` | 应用根：主题、语言、登录态三态（loading/error/登录） |
| `mobile/lib/app_info.dart` | 版本号唯一来源（与 `pubspec.yaml` 一致） |
| `mobile/lib/theme/tokens.dart` | `SinanPalette` 明/暗 Token（唯一色值来源） |
| `mobile/lib/theme/app_theme.dart` | `ThemeData` 组装 |
| `mobile/lib/models/rules.dart` | 日期解析、状态推导、倒挂/逾期、归组、我的待办 |
| `mobile/lib/models/issue.dart` | 问题 / 阶段 / 状态 / 统计模型 |
| `mobile/lib/models/attachment.dart` | 附件模型（本机文件 + 同步状态） |
| `mobile/lib/data/local_db.dart` | sqflite 建表 v2、迁移、首启种子 |
| `mobile/lib/data/issue_repository.dart` | 查询、新建、附件、outbox、机台聚合 |
| `mobile/lib/data/attachment_store.dart` | 拍照/相册、压缩落盘 |
| `mobile/lib/state/app_state.dart` | 会话、主题、列表、统计、待传条数 |
| `mobile/lib/screens/shell_screen.dart` | 五槽底栏与页面切换 |
| `mobile/lib/screens/new_issue_screen.dart` | 新建问题（离线落库 + 写 outbox） |
| `mobile/lib/screens/issue_detail_screen.dart` | 详情、阶段推进、评论、附件 |
| `mobile/lib/screens/sync_screen.dart` | 待传队列、配对码入口、同步规则说明 |
| `mobile/android/app/build.gradle.kts` | SDK 口径、版本号来源、签名（当前 debug） |
| `mobile/android/app/src/main/AndroidManifest.xml` | 权限、明文 HTTP、应用名 |
| `mobile/ios/Runner/Info.plist` | iOS 权限描述、本地网络、ATS、显示名 |
