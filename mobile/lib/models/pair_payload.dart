/// 桌面端配对二维码的载荷解析（契约：`shared/api/sync-api.md` 第 4.1 节）。
///
/// 二维码内容固定为：
/// `sinan://pair?v=1&url=http%3A%2F%2F192.168.0.89%3A5180&code=824716`
///
/// 只解析地址与一次性配对码，不承载 token；配对仍走 `POST /api/pair`。
/// 解析失败抛 [PairQrException]，message 直接给现场看，不暴露内部细节。
library;

/// 二维码解析结果：桌面端地址（已解码）+ 6 位配对码。
class PairQrPayload {
  const PairQrPayload({required this.serverUrl, required this.code});

  /// 桌面端监听地址，例如 `http://192.168.0.89:5180`。
  final String serverUrl;

  /// 6 位一次性配对码。
  final String code;

  /// 当前 App 认识的载荷版本。桌面端升版本时必须两端一起改契约。
  static const int supportedVersion = 1;

  static final RegExp _codePattern = RegExp(r'^\d{6}$');

  /// 解析扫码得到的原始字符串；不是司南配对码时抛出可读异常。
  static PairQrPayload parse(String raw) {
    final text = raw.trim();
    if (text.isEmpty) {
      throw const PairQrException('二维码内容为空，请对准桌面端的配对二维码。');
    }

    final uri = Uri.tryParse(text);
    if (uri == null || uri.scheme != 'sinan' || uri.host != 'pair') {
      throw const PairQrException('这不是司南的配对二维码，请扫描桌面端弹出的二维码。');
    }

    final version = uri.queryParameters['v'] ?? '';
    if (version != '$supportedVersion') {
      throw PairQrException(
        '二维码版本 ${version.isEmpty ? '未知' : version} 与本机不兼容，'
        '请把手机端与桌面端都升级到最新版本。',
      );
    }

    final url = (uri.queryParameters['url'] ?? '').trim();
    if (url.isEmpty) {
      throw const PairQrException('二维码里缺少桌面端地址，请在桌面端重新生成。');
    }

    final code = (uri.queryParameters['code'] ?? '').trim();
    if (!_codePattern.hasMatch(code)) {
      throw const PairQrException('二维码里的配对码不是 6 位数字，请在桌面端重新生成。');
    }

    return PairQrPayload(serverUrl: url, code: code);
  }
}

/// 配对二维码不可用：message 是给现场看的完整句子。
class PairQrException implements Exception {
  const PairQrException(this.message);

  final String message;

  @override
  String toString() => message;
}
