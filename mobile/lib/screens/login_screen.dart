import 'dart:math' as math;

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
                    _brandMark(),
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
                      '现场问题闭环',
                      style: TextStyle(fontSize: 12.5, color: p.muted, letterSpacing: 1),
                    ),
                    const SizedBox(height: 26),
                    GlassCard(
                      padding: const EdgeInsets.fromLTRB(20, 22, 20, 18),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text('工号登录',
                              style: TextStyle(
                                  fontSize: 16,
                                  fontWeight: FontWeight.w700,
                                  color: p.fg)),
                          const SizedBox(height: 4),
                          Text('离线优先，数据先存本机，联网后再同步。',
                              style: TextStyle(fontSize: 11.5, color: p.faint)),
                          const SizedBox(height: 18),
                          // 账号框不显示“账号”标签：值已预填 engineer，左侧人形图标足够表意。
                          // 仍保留读屏用的语义标签，删除的只是视觉文本。
                          Semantics(
                            label: '账号',
                            child: TextField(
                              controller: _account,
                              textInputAction: TextInputAction.next,
                              decoration: const InputDecoration(
                                prefixIcon: Icon(Icons.person_outline, size: 18),
                              ),
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
                          const SizedBox(height: 10),
                          // 设计稿这里是"已配对 · 192.168.1.26 / 局域网"；
                          // 这里读真实的配对配置：已配对显示桌面端地址，未配对如实显示"离线"。
                          // 配对状态只代表"有令牌"，不代表此刻连得上——真正结果以同步中心为准。
                          Row(
                            children: [
                              Icon(
                                  widget.state.syncConfigured
                                      ? Icons.link
                                      : Icons.link_off,
                                  size: 13,
                                  color: p.faint),
                              const SizedBox(width: 5),
                              Expanded(
                                child: Text(
                                    widget.state.syncConfigured
                                        ? '已配对 · ${widget.state.syncServerUrl.replaceFirst(RegExp(r'^https?://'), '')}'
                                        : '未配对 · 数据先存本机',
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style:
                                        TextStyle(fontSize: 10.5, color: p.faint)),
                              ),
                              const SizedBox(width: 6),
                              SinanChip(
                                  text: widget.state.syncConfigured
                                      ? '离线可用'
                                      : '离线',
                                  tone: widget.state.syncConfigured
                                      ? 'ok'
                                      : 'warn',
                                  dense: true),
                            ],
                          ),
                          if (_error != null) ...[
                            const SizedBox(height: 10),
                            Text(_error!,
                                style: TextStyle(fontSize: 11.5, color: p.ng)),
                          ],
                          const SizedBox(height: 14),
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
                    const SizedBox(height: 14),
                    GlassCard(
                      padding: const EdgeInsets.fromLTRB(14, 12, 14, 10),
                      radius: 14,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('离线可用范围',
                              style: TextStyle(
                                  fontSize: 12,
                                  fontWeight: FontWeight.w700,
                                  color: p.fg)),
                          const SizedBox(height: 7),
                          _capability(p, '查看已同步的 ${widget.state.issues.length} 条问题', true),
                          _capability(p, '新建问题、拍照、推进阶段', true),
                          _capability(p, '跨人查询与统计需回到桌面端', false),
                        ],
                      ),
                    ),
                    const SizedBox(height: 14),
                    Text('${AppInfo.label} · 仅限现场调试组',
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

  Widget _capability(SinanPalette p, String text, bool ok) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(ok ? Icons.check_circle_outline : Icons.remove_circle_outline,
              size: 14, color: ok ? p.ok : p.faint),
          const SizedBox(width: 6),
          Expanded(
            child: Text(text,
                style: TextStyle(fontSize: 10.5, height: 1.5, color: p.muted)),
          ),
        ],
      ),
    );
  }

  /// 品牌图标：与系统启动画面（Android 12 splash）显示的应用图标一致——
  /// 橙色地盘圆环 + 白色司南磁勺（`docs/branding/logo-2-ladle.svg`，
  /// 背景色取 `android/res/values/colors.xml` 的 `ic_launcher_background`）。
  /// 用矢量复刻而不是位图，60px 与高分辨率下都清晰。
  Widget _brandMark() {
    return const SizedBox(
      width: 60,
      height: 60,
      child: CustomPaint(painter: _LadleMarkPainter()),
    );
  }
}

/// 绘制司南磁勺图标（128×128 设计坐标，等比缩放到目标尺寸）。
/// 几何与 `logo-2-ladle.svg` 逐点对应，唯一的差异是把 SVG 的圆角方块地盘
/// 改成圆形——即启动画面里系统的圆形裁剪效果。
class _LadleMarkPainter extends CustomPainter {
  const _LadleMarkPainter();

  static const double _design = 128; // SVG viewBox 边长
  static const Color _plate = Color(0xFFC5540A); // 与 ic_launcher_background 同色

  @override
  void paint(Canvas canvas, Size size) {
    final s = size.width / _design;
    final center = Offset(size.width / 2, size.height / 2);
    final white = Paint()
      ..color = Colors.white
      ..isAntiAlias = true;

    // 地盘：实心圆
    canvas.drawCircle(
      center,
      64 * s,
      Paint()
        ..color = _plate
        ..isAntiAlias = true,
    );

    // 地盘圆环：SVG 为白色 50% 透明、线宽 4、半径 42
    canvas.drawCircle(
      center,
      42 * s,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 4 * s
        ..color = Colors.white.withValues(alpha: 0.5)
        ..isAntiAlias = true,
    );

    // 磁勺：绕中心逆时针 38°，与 SVG 的 rotate(-38 64 64) 一致
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(-38 * math.pi / 180);
    canvas.translate(-center.dx, -center.dy);

    // 勺头
    canvas.drawCircle(Offset(64 * s, 45 * s), 14 * s, white);

    // 勺柄：上宽下尖的多边形
    final stem = Path()
      ..moveTo(58 * s, 52 * s)
      ..lineTo(70 * s, 52 * s)
      ..lineTo(66 * s, 92 * s)
      ..lineTo(64 * s, 99 * s)
      ..lineTo(62 * s, 92 * s)
      ..close();
    canvas.drawPath(stem, white);

    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _LadleMarkPainter oldDelegate) => false;
}
