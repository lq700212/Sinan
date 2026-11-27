/// 扫码页共用的相机覆盖层。
///
/// 配对扫码与机台扫码的相机管理、取景框、提示条、错误页完全一致，
/// 只有解析规则和文案不同；这里只放视觉，不碰业务。
/// 覆盖层固定白字黑底，不跟明暗主题：背景永远是相机画面，
/// 用主题前景色在浅色主题下会变成深色字，直接看不见。
library;

import 'package:material_ui/material_ui.dart';

/// 顶部返回 + 标题条。
class ScanTopBar extends StatelessWidget {
  const ScanTopBar({super.key, required this.title, required this.onClose});

  final String title;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(6, 6, 12, 0),
      child: Row(
        children: [
          IconButton(
            onPressed: onClose,
            icon: const Icon(Icons.arrow_back, color: Colors.white),
            tooltip: '返回',
          ),
          Expanded(
            child: Text(
              title,
              style: const TextStyle(
                color: Colors.white,
                fontSize: 15,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// 扫描框：四角亮线；识别失败时变暖红。
class ScanFrameOverlay extends StatelessWidget {
  const ScanFrameOverlay({super.key, required this.hasError});

  final bool hasError;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: LayoutBuilder(
        builder: (context, constraints) {
          final side = constraints.maxWidth * 0.66;
          return Center(
            child: SizedBox(
              width: side,
              height: side,
              child: CustomPaint(
                painter: _CornerPainter(
                  color: hasError ? const Color(0xFFFFB4A8) : Colors.white,
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _CornerPainter extends CustomPainter {
  const _CornerPainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 3
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    const arm = 26.0;
    final w = size.width;
    final h = size.height;

    // 左上
    canvas.drawPath(
        Path()
          ..moveTo(0, arm)
          ..lineTo(0, 0)
          ..lineTo(arm, 0),
        paint);
    // 右上
    canvas.drawPath(
        Path()
          ..moveTo(w - arm, 0)
          ..lineTo(w, 0)
          ..lineTo(w, arm),
        paint);
    // 右下
    canvas.drawPath(
        Path()
          ..moveTo(w, h - arm)
          ..lineTo(w, h)
          ..lineTo(w - arm, h),
        paint);
    // 左下
    canvas.drawPath(
        Path()
          ..moveTo(arm, h)
          ..lineTo(0, h)
          ..lineTo(0, h - arm),
        paint);
  }

  @override
  bool shouldRepaint(covariant _CornerPainter oldDelegate) =>
      oldDelegate.color != color;
}

/// 底部提示条：正常白字，识别失败暖红字。
class ScanBottomHint extends StatelessWidget {
  const ScanBottomHint({
    super.key,
    required this.text,
    this.isError = false,
  });

  final String text;
  final bool isError;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.fromLTRB(20, 0, 20, 26),
      padding: const EdgeInsets.fromLTRB(14, 11, 14, 11),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.62),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Text(
        text,
        textAlign: TextAlign.center,
        style: TextStyle(
          color: isError ? const Color(0xFFFFB4A8) : Colors.white,
          fontSize: 12,
          height: 1.55,
        ),
      ),
    );
  }
}

/// 相机不可用时的整屏提示（权限被拒 / 启动失败），带重试。
class ScannerErrorView extends StatelessWidget {
  const ScannerErrorView({
    super.key,
    required this.message,
    required this.onRetry,
  });

  final String message;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: Colors.black,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.no_photography_outlined,
                  color: Colors.white54, size: 38),
              const SizedBox(height: 12),
              Text(
                message,
                textAlign: TextAlign.center,
                style: const TextStyle(
                    color: Colors.white70, fontSize: 12.5, height: 1.6),
              ),
              const SizedBox(height: 16),
              OutlinedButton(
                onPressed: onRetry,
                style: OutlinedButton.styleFrom(
                  foregroundColor: Colors.white,
                  side: const BorderSide(color: Colors.white38),
                ),
                child: const Text('重试'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
