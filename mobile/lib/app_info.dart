/// 应用版本唯一来源。
///
/// 与 `pubspec.yaml` 的 `version` 保持一致（当前 0.0.6+6）。
/// 手机端与桌面端各自独立升版，只动第三位；三段式语义化版本，
/// 修 bug 走第三位，不再出现 `0.01` 这种两段写法（Android/iOS 都会按 0.1.0 解释，反而对不上）。
/// 手机端不发版号给用户看，但登录页和设置页必须显示同一个值，
/// 不允许两处各写一个对不上——桌面端 `AppInfo` 同一条纪律。
class AppInfo {
  const AppInfo._();

  static const String version = '0.0.6';
  static const String label = 'v$version';
  static const String productLabel = '司南 $label';
}
