import 'dart:async';

import 'package:material_ui/material_ui.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../models/rules.dart';
import '../widgets/scanner_chrome.dart';

/// 扫机台码：识别机台标签上的条码/二维码，返回提取到的机台号。
///
/// 只做"识别 + 提取"，不查库：手输路径同样允许输入本机还没有的合法机台号
/// （新建问题后由桌面端补全设备），扫码保持一致口径。
/// 条码常见 Code128，二维码也兼容；提取规则见 [Rules.extractMachineCode]。
class MachineScanScreen extends StatefulWidget {
  const MachineScanScreen({super.key, required this.projectCode});

  /// 当前总项目号，用于限定机台号前缀（如 P7391）。
  final String projectCode;

  @override
  State<MachineScanScreen> createState() => _MachineScanScreenState();
}

class _MachineScanScreenState extends State<MachineScanScreen>
    with WidgetsBindingObserver {
  late final MobileScannerController _controller;
  bool _handled = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _controller = MobileScannerController(
      formats: const [
        BarcodeFormat.qrCode,
        BarcodeFormat.code128,
        BarcodeFormat.code39,
        BarcodeFormat.code93,
        BarcodeFormat.codabar,
        BarcodeFormat.dataMatrix,
        BarcodeFormat.ean13,
        BarcodeFormat.ean8,
        BarcodeFormat.itf2of5,
      ],
      // 机台标签常贴多张码，识别到合法机台号就退出，重复回调无意义。
      detectionSpeed: DetectionSpeed.noDuplicates,
    );
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
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

    final machine =
        Rules.extractMachineCode(raw, projectCode: widget.projectCode);
    if (machine == null) {
      final message = '这不是 ${widget.projectCode} 的机台码'
          '（应为 ${widget.projectCode} + 3 位序号，如 ${widget.projectCode}106）。';
      if (_error != message) setState(() => _error = message);
      return;
    }

    _handled = true;
    Navigator.of(context).pop(machine);
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
                  title: '扫描机台码',
                  onClose: () => Navigator.of(context).pop(),
                ),
                const Spacer(),
                ScanBottomHint(
                  text: _error ??
                      '把机台标签上的条码或二维码放进框内，'
                          '扫到后自动带回新建页；没有标签就返回手输。',
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
