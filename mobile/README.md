# 司南 · 现场端（Flutter）

P7391 安装调试问题闭环系统的 Android 现场端。桌面端（`../desktop/`）是唯一主库，
本端是**离线副本 + 待传队列**：现场先写本机 SQLite，联网后按 outbox 补传。

## 环境

- Flutter 3.50 / Dart 3.14
- Android SDK 36（`flutter doctor` 可检查）
- 依赖：`sqflite`、`path_provider`、`shared_preferences`、`flutter_localizations`

## 构建

```powershell
# 国内镜像（可选，本机网络下必需）
$env:FLUTTER_STORAGE_BASE_URL = 'https://storage.flutter-io.cn'
$env:PUB_HOSTED_URL = 'https://pub.flutter-io.cn'

flutter pub get
flutter test          # 纯规则回归，14 条
flutter analyze       # 应为 0 issue
flutter build apk --debug
```

Gradle 已配置腾讯云镜像，Maven 已配置阿里云镜像，见
`android/gradle/wrapper/gradle-wrapper.properties` 与 `android/settings.gradle.kts`。
iOS 工程保留但本机无法构建（需 macOS）。

## 目录

```
lib/
├── main.dart              ← 应用根：主题、登录态、语言
├── theme/
│   ├── tokens.dart        ← SinanPalette 明/暗 Token（唯一色值来源）
│   └── app_theme.dart     ← ThemeData
├── models/
│   ├── issue.dart         ← 问题 / 阶段 / 状态
│   └── rules.dart         ← 日期解析、状态推导、倒挂、逾期、归组
├── data/
│   ├── local_db.dart      ← sqflite 建表 + 首启种子
│   └── issue_repository.dart
├── state/app_state.dart   ← 会话、主题、列表与统计
├── widgets/common.dart    ← 玻璃卡片、Chip、阶段点、KPI、空状态
└── screens/               ← 登录 / 总览 / 台账 / 详情 / 交换 / 设置
```

## 数据口径

- 与桌面端同一份种子：`assets/seed/issues.seed.json`（36 条 / 31 台，虚构数据）。
- 日期双字段：`due_raw` 存原文，`due_date` 由原文解析；`/`、`已下图`、空白不参与逾期。
- 阶段状态：`not_started` / `in_progress` / `done` / `na`；问题状态由四阶段推导。
- 单机台号归组：`P7391101–P7391199` → 01，`…2xx` → 02，`…3xx` → 03；位数异常留“待确认”。
- 演示照片为占位值（12 张 / 6 条），界面标注“演示值”。

## 当前边界

- 移动端暂不直接解析 Excel（`.xls/.xlsx` 由桌面端处理）。
- 配对码入口保留，但同步服务尚未联调，界面不伪造连接成功。
- 拍照与附件上传待接入；详情页当前为照片占位。
