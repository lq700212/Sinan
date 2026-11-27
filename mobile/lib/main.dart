import 'package:material_ui/material_ui.dart';
import 'package:cupertino_ui/cupertino_ui.dart';
import 'package:flutter_localizations/flutter_localizations.dart'
    show GlobalWidgetsLocalizations;

import 'data/issue_repository.dart';
import 'data/local_db.dart';
import 'screens/login_screen.dart';
import 'screens/shell_screen.dart';
import 'state/app_state.dart';
import 'theme/app_theme.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  final repository = IssueRepository(LocalDb.instance);
  final state = AppState(repository);
  // 首帧直接进登录页：数据库与种子在后台加载，读完自动 notifyListeners 刷新。
  // 本地 SQLite 通常几百毫秒，没必要用全屏“正在读取本机数据”挡住首屏。
  runApp(SinanApp(state: state));
  state.init();
}

/// 应用根：主题跟随 [AppState.themeMode]，登录态决定进登录页还是主壳。
/// 业务状态全部在 AppState，页面只订阅，不各存一份。
class SinanApp extends StatelessWidget {
  const SinanApp({super.key, required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: state,
      builder: (context, _) {
        return MaterialApp(
          title: '司南 · 安装调试问题闭环',
          debugShowCheckedModeBanner: false,
          theme: buildSinanTheme(Brightness.light),
          darkTheme: buildSinanTheme(Brightness.dark),
          themeMode: state.themeMode,
          locale: const Locale('zh', 'CN'),
          supportedLocales: const [Locale('zh', 'CN')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          builder: (context, child) {
            // 固定系统字号缩放上限，避免现场大字体把布局挤爆。
            final media = MediaQuery.of(context);
            return MediaQuery(
              data: media.copyWith(
                textScaler: media.textScaler.clamp(
                  minScaleFactor: 0.9,
                  maxScaleFactor: 1.2,
                ),
              ),
              child: child ?? const SizedBox.shrink(),
            );
          },
          // 没有全屏启动页：加载中直接显示登录页，数据读完自己刷新；
          // 只有读库失败才占满屏幕给原因与“重试”。
          home: state.loadError != null
              ? _LoadErrorScreen(state: state)
              : state.loggedIn
                  ? ShellScreen(state: state)
                  : LoginScreen(state: state),
        );
      },
    );
  }
}

/// 本机数据库初始化失败时的兜底页：给出可读原因与“重试”，
/// 不让现场人员面对空白或一直转圈的登录页。
class _LoadErrorScreen extends StatelessWidget {
  const _LoadErrorScreen({required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = Theme.of(context).colorScheme;
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 360),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.storage_outlined, size: 36, color: p.error),
                const SizedBox(height: 12),
                Text(
                  '本机数据读取失败',
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: p.onSurface,
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  '记录还在本机，可先重试；若仍失败，请检查存储权限或重装应用后再试。',
                  style: TextStyle(fontSize: 12, height: 1.5, color: p.onSurfaceVariant),
                ),
                const SizedBox(height: 12),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: p.errorContainer.withValues(alpha: 0.35),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: SelectableText(
                    state.loadError ?? '',
                    style: TextStyle(fontSize: 11, color: p.onSurfaceVariant),
                  ),
                ),
                const SizedBox(height: 16),
                Align(
                  alignment: Alignment.centerRight,
                  child: FilledButton.icon(
                    onPressed: state.init,
                    icon: const Icon(Icons.refresh, size: 18),
                    label: const Text('重试'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
