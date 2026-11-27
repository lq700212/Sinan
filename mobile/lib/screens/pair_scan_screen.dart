import 'dart:async';

import 'package:material_ui/material_ui.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../models/pair_payload.dart';
import '../widgets/scanner_chrome.dart';

/// 扫描桌面端配对二维码。
///
/// 只识别 QR，扫到 `sinan://pair?v=1&...` 就解析并返回 [PairQrPayload]；
/// 扫到别的二维码不退出，底部提示"这不是司南的配对二维码"，继续扫。
class PairScanScreen extends StatefulWidget {
  const PairScanScreen({super.key});

  @override
  State<PairScanScreen> createState() => _PairScanScreenState();
}

class _PairScanScreenState extends State<PairScanScreen>
    with WidgetsBindingObserver {
  late final MobileScannerController _controller;
  bool _handled = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _controller = MobileScannerController(
      formats: const [BarcodeFormat.qrCode],
      // 同一个码只回调一次，避免连续触发重复解析/重复 pop。
      detectionSpeed: DetectionSpeed.noDuplicates,
    );
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // 传了 controller 时生命周期要自己管（mobile_scanner 文档口径）：
    // 切后台/锁屏必须停相机，回前台再启动，否则相机会被占住。
    if (!_controller.value.hasCameraPermission) return;
    switch (state) {
      case AppLifecycleState.resumed:
        unawaited(_controller.start());
      case AppLifecycleState.inactive:
      case AppLifecycleState.paused:
      case AppLifecycleState.hidden:
      case AppLifecycleState.detached:
        unawaited(_controller.stop());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    unawaited(_controller.dispose());
    super.dispose();
  }

  void _onDetect(BarcodeCapture capture) {
    if (_handled) return;
    final raw = capture.barcodes.isEmpty ? null : capture.barcodes.first.rawValue;
    if (raw == null || raw.trim().isEmpty) return;

    try {
      final payload = PairQrPayload.parse(raw);
      _handled = true;
      Navigator.of(context).pop(payload);
    } on PairQrException catch (e) {
      // 同一个错误码不重复 setState，避免相机帧率级重建。
      if (_error != e.message) setState(() => _error = e.message);
    }
  }

  Future<void> _retry() async {
    try {
      await _controller.start();
      if (mounted) setState(() => _error = null);
    } catch (e) {
      if (mounted) setState(() => _error = '相机启动失败：$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        fit: StackFit.expand,
        children: [
          MobileScanner(
            controller: _controller,
            onDetect: _onDetect,
            errorBuilder: (context, error) {
              final denied =
                  error.errorCode == MobileScannerErrorCode.permissionDenied;
              return ScannerErrorView(
                message: denied
                    ? '相机权限被拒绝。请到系统设置里允许「司南」使用相机，然后重试。'
                    : '相机启动失败：${error.errorCode.message}',
                onRetry: _retry,
              );
            },
            placeholderBuilder: (_) => const ColoredBox(
              color: Colors.black,
              child: Center(
                child: CircularProgressIndicator(color: Colors.white70),
              ),
            ),
          ),
          ScanFrameOverlay(hasError: _error != null),
          SafeArea(
            child: Column(
              children: [
                ScanTopBar(
                  title: '扫描配对二维码',
                  onClose: () => Navigator.of(context).pop(),
                ),
                const Spacer(),
                ScanBottomHint(
                  text: _error ??
                      '把桌面端「手机扫码配对」里的二维码放进框内，'
                          '扫到后会自动填入地址和配对码。',
                  isError: _error != null,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
