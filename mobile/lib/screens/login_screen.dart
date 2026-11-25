import 'package:material_ui/material_ui.dart';

import '../app_info.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.state});

  final AppState state;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _account = TextEditingController(text: 'engineer');

  /// 密码不预填：debug 包也要真登录，任何人拿到安装包点一下就进系统是不允许的。
  final _password = TextEditingController();
  String? _error;
  bool _busy = false;

  @override
  void dispose() {
    _account.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    await Future<void>.delayed(const Duration(milliseconds: 180));
    final ok = widget.state.login(_account.text, _password.text);
    if (!mounted) return;
    setState(() {
      _busy = false;
      if (!ok) _error = '账号或密码不正确。演示账号见下方提示。';
    });
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Scaffold(
      body: AmbientBackground(
        child: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _brandMark(p),
                    const SizedBox(height: 16),
                    Text(
                      '司南',
                      style: TextStyle(
                        fontSize: 32,
                        fontWeight: FontWeight.w800,
                        letterSpacing: 6,
                        color: p.fg,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'P7391 安装调试问题闭环',
                      style: TextStyle(fontSize: 12.5, color: p.muted, letterSpacing: 1),
                    ),
                    const SizedBox(height: 26),
                    GlassCard(
                      padding: const EdgeInsets.fromLTRB(20, 22, 20, 18),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text('现场登录',
                              style: TextStyle(
                                  fontSize: 16,
                                  fontWeight: FontWeight.w700,
                                  color: p.fg)),
                          const SizedBox(height: 4),
                          Text('离线优先，数据先存本机，联网后再同步。',
                              style: TextStyle(fontSize: 11.5, color: p.faint)),
                          const SizedBox(height: 18),
                          TextField(
                            controller: _account,
                            textInputAction: TextInputAction.next,
                            decoration: const InputDecoration(
                              labelText: '账号',
                              prefixIcon: Icon(Icons.person_outline, size: 18),
                            ),
                          ),
                          const SizedBox(height: 12),
                          TextField(
                            controller: _password,
                            obscureText: true,
                            onSubmitted: (_) => _submit(),
                            decoration: const InputDecoration(
                              labelText: '密码',
                              prefixIcon: Icon(Icons.lock_outline, size: 18),
                            ),
                          ),
                          if (_error != null) ...[
                            const SizedBox(height: 10),
                            Text(_error!,
                                style: TextStyle(fontSize: 11.5, color: p.ng)),
                          ],
                          const SizedBox(height: 18),
                          FilledButton(
                            onPressed: _busy ? null : _submit,
                            child: _busy
                                ? const SizedBox(
                                    width: 18,
                                    height: 18,
                                    child: CircularProgressIndicator(strokeWidth: 2),
                                  )
                                : const Text('进入现场端'),
                          ),
                          const SizedBox(height: 12),
                          Container(
                            padding: const EdgeInsets.all(10),
                            decoration: BoxDecoration(
                              color: p.well,
                              borderRadius: BorderRadius.circular(10),
                            ),
                            child: Row(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Icon(Icons.info_outline, size: 15, color: p.faint),
                                const SizedBox(width: 6),
                                Expanded(
                                  child: Text(
                                    '演示账号：engineer / 123456（工程师）、admin / 123456（管理员）',
                                    style: TextStyle(
                                        fontSize: 10.5, height: 1.5, color: p.faint),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 18),
                    Text('${AppInfo.productLabel} · 离线副本',
                        style: TextStyle(fontSize: 10.5, color: p.faint)),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _brandMark(SinanPalette p) {
    return Container(
      width: 64,
      height: 64,
      decoration: BoxDecoration(
        gradient: p.primaryGradient,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: p.primary.withValues(alpha: 0.35),
            blurRadius: 22,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      alignment: Alignment.center,
      child: const Text(
        '司',
        style: TextStyle(
          fontSize: 30,
          fontWeight: FontWeight.w800,
          color: Colors.white,
        ),
      ),
    );
  }
}
