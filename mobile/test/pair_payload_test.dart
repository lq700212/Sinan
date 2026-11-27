import 'package:flutter_test/flutter_test.dart';
import 'package:sinan_mobile/models/pair_payload.dart';

/// 配对二维码载荷回归（契约：shared/api/sync-api.md 4.1）。
/// 桌面端 [PairQrPayload.Build] 与这里必须逐字对齐，改格式两端一起改。
void main() {
  group('PairQrPayload.parse', () {
    test('解析桌面端生成的完整载荷', () {
      const raw = 'sinan://pair?v=1&url=http%3A%2F%2F192.168.0.89%3A5180'
          '&code=824716';
      final payload = PairQrPayload.parse(raw);
      expect(payload.serverUrl, 'http://192.168.0.89:5180');
      expect(payload.code, '824716');
    });

    test('地址里的端口与路径保持原样', () {
      const raw = 'sinan://pair?v=1&url=http%3A%2F%2F10.0.0.5%3A5180%2F'
          '&code=000001';
      final payload = PairQrPayload.parse(raw);
      expect(payload.serverUrl, 'http://10.0.0.5:5180/');
      expect(payload.code, '000001');
    });

    test('非司南二维码给出可读提示', () {
      expect(
        () => PairQrPayload.parse('https://example.com/abc'),
        throwsA(isA<PairQrException>().having(
            (e) => e.message, 'message', contains('不是司南的配对二维码'))),
      );
    });

    test('未知版本明确提示不兼容', () {
      expect(
        () => PairQrPayload.parse(
            'sinan://pair?v=2&url=http%3A%2F%2F192.168.0.2%3A5180&code=123456'),
        throwsA(isA<PairQrException>()
            .having((e) => e.message, 'message', contains('不兼容'))),
      );
    });

    test('缺少地址或配对码不是 6 位都拒绝', () {
      expect(
        () => PairQrPayload.parse('sinan://pair?v=1&code=123456'),
        throwsA(isA<PairQrException>()),
      );
      expect(
        () => PairQrPayload.parse(
            'sinan://pair?v=1&url=http%3A%2F%2F192.168.0.2%3A5180&code=12345'),
        throwsA(isA<PairQrException>()),
      );
      expect(
        () => PairQrPayload.parse(
            'sinan://pair?v=1&url=http%3A%2F%2F192.168.0.2%3A5180&code=abcdef'),
        throwsA(isA<PairQrException>()),
      );
    });
  });
}
