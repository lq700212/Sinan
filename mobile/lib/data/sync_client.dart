import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import 'issue_repository.dart';

/// 局域网同步客户端（协议唯一真源：shared/api/sync-api.md）。
///
/// 只负责 HTTP 收发与 JSON 解析，不碰数据库；队列结算、冲突落表由 AppState 编排。
/// 所有请求都有超时：现场 Wi-Fi 时断时续，不能让"立即同步"一直转圈。
class SyncClient {
  SyncClient({http.Client? client}) : _client = client ?? http.Client();

  static const int defaultPort = 5180;
  static const int protocol = 1;
  static const Duration timeout = Duration(seconds: 8);

  final http.Client _client;

  /// 把现场手输的地址补成 `http://host:5180`：
  /// 允许 `192.168.1.26`、`192.168.1.26:5180`、`http://192.168.1.26:5180/` 三种写法。
  static String normalizeBaseUrl(String input) {
    var text = input.trim();
    if (text.isEmpty) return '';
    if (!text.startsWith('http://') && !text.startsWith('https://')) {
      text = 'http://$text';
    }
    if (text.endsWith('/')) text = text.substring(0, text.length - 1);
    final uri = Uri.tryParse(text);
    if (uri == null || uri.host.isEmpty) return '';
    if (uri.hasPort) return text;
    return '$text:$defaultPort';
  }

  Future<PingInfo> ping(String baseUrl) async {
    final response = await _client
        .get(Uri.parse('${_base(baseUrl)}/api/ping'))
        .timeout(timeout);
    final body = _decode(response);
    if (response.statusCode != 200) throw _error(body, '无法连接桌面端');
    if (body['app'] != 'sinan') {
      throw const SyncException('not_sinan', '该地址不是司南桌面端服务。');
    }
    final serverProtocol = body['protocol'] as int? ?? 0;
    if (serverProtocol != protocol) {
      throw SyncException('protocol_mismatch',
          '桌面端协议版本 $serverProtocol 与本机 $protocol 不一致，请升级另一端。');
    }
    return PingInfo(
      serverName: body['serverName'] as String? ?? '司南 · 桌面端',
      desktopVersion: body['desktopVersion'] as String? ?? '',
    );
  }

  Future<PairResult> pair({
    required String baseUrl,
    required String code,
    required String deviceName,
    required String model,
    required String employeeNo,
  }) async {
    final response = await _client
        .post(
          Uri.parse('${_base(baseUrl)}/api/pair'),
          headers: const {'Content-Type': 'application/json; charset=utf-8'},
          body: jsonEncode({
            'code': code.trim(),
            'deviceName': deviceName,
            'model': model,
            'platform': Platform.isIOS ? 'ios' : 'android',
            'employeeNo': employeeNo,
          }),
        )
        .timeout(timeout);
    final body = _decode(response);
    if (response.statusCode != 200 || body['ok'] != true) {
      throw _error(body, '配对失败');
    }
    final token = body['token'] as String? ?? '';
    if (token.isEmpty) throw const SyncException('bad_response', '桌面端未返回令牌。');
    return PairResult(
      deviceId: body['deviceId'] as String? ?? '',
      token: token,
      pairedAt: body['pairedAt'] as String? ?? '',
    );
  }

  Future<List<PushResult>> push({
    required String baseUrl,
    required String token,
    required List<OutboxEntry> entries,
  }) async {
    if (entries.isEmpty) return const [];
    final response = await _client
        .post(
          Uri.parse('${_base(baseUrl)}/api/sync/push'),
          headers: _headers(token),
          body: jsonEncode({
            'operations': [
              for (final entry in entries)
                {
                  'clientOpId': entry.id,
                  'entity': entry.entity,
                  'entityId': entry.entityId,
                  'op': entry.op,
                  'baseVersion': entry.baseVersion,
                  'clientTime': entry.createdAt,
                  'payload': _jsonObject(entry.payload),
                },
            ],
          }),
        )
        .timeout(timeout);
    final body = _decode(response);
    if (response.statusCode != 200 || body['ok'] != true) {
      throw _error(body, '上行同步失败');
    }
    final results = (body['results'] as List?) ?? const [];
    return [
      for (final raw in results)
        if (raw is Map)
          PushResult(
            clientOpId: (raw['clientOpId'] as num?)?.toInt() ?? 0,
            status: raw['status'] as String? ?? 'rejected',
            serverId: raw['serverId'] as String? ?? '',
            serverVersion: (raw['serverVersion'] as num?)?.toInt() ?? 0,
            seqNo: raw['seqNo'] as String? ?? '',
            serverPayload: raw['serverPayload'],
            message: raw['message'] as String? ?? '',
          ),
    ];
  }

  Future<PullResult> pull({
    required String baseUrl,
    required String token,
    required int cursor,
    int limit = 200,
  }) async {
    final uri = Uri.parse(
        '${_base(baseUrl)}/api/sync/pull?cursor=$cursor&limit=$limit');
    final response = await _client.get(uri, headers: _headers(token)).timeout(timeout);
    final body = _decode(response);
    if (response.statusCode != 200 || body['ok'] != true) {
      throw _error(body, '下行同步失败');
    }
    final changes = (body['changes'] as List?) ?? const [];
    return PullResult(
      cursor: (body['cursor'] as num?)?.toInt() ?? cursor,
      hasMore: body['hasMore'] == true,
      changes: [
        for (final raw in changes)
          if (raw is Map)
            PullChange(
              seq: (raw['seq'] as num?)?.toInt() ?? 0,
              entity: raw['entity'] as String? ?? '',
              entityId: raw['entityId'] as String? ?? '',
              op: raw['op'] as String? ?? '',
              createdAt: raw['createdAt'] as String? ?? '',
              payload: raw['payload'],
            ),
      ],
    );
  }

  /// 上传一张照片的原始字节。sha256 由桌面端可选校验；手机端不传，
  /// 避免为了算哈希再引一个加密包（HTTP 层已保证长度与幂等 ID）。
  Future<AttachmentUploadResult> uploadAttachment({
    required String baseUrl,
    required String token,
    required String issueId,
    required String attachmentId,
    required String fileName,
    required String filePath,
  }) async {
    final file = File(filePath);
    if (!await file.exists()) {
      return const AttachmentUploadResult(
          ok: false, error: 'file_missing', message: '本机照片文件已不存在。');
    }

    final bytes = await file.readAsBytes();
    final uri = Uri.parse(
      '${_base(baseUrl)}/api/attachments/${Uri.encodeComponent(issueId)}/'
      '${Uri.encodeComponent(attachmentId)}'
      '?fileName=${Uri.encodeQueryComponent(fileName)}',
    );
    final request = http.Request('POST', uri)
      ..headers.addAll(_headers(token))
      ..headers['Content-Type'] = 'image/jpeg'
      ..bodyBytes = bytes;

    final streamed = await _client.send(request).timeout(timeout);
    final response = await http.Response.fromStream(streamed);
    final body = _decode(response);
    if (response.statusCode != 200 || body['ok'] != true) {
      return AttachmentUploadResult(
        ok: false,
        error: body['error'] as String? ?? 'upload_failed',
        message: body['message'] as String? ?? '照片上传失败',
      );
    }
    return AttachmentUploadResult(
      ok: true,
      storedPath: body['storedPath'] as String? ?? '',
      size: (body['size'] as num?)?.toInt() ?? bytes.length,
    );
  }

  void close() => _client.close();

  static String _base(String baseUrl) => normalizeBaseUrl(baseUrl);

  static Map<String, String> _headers(String token) => {
        'Content-Type': 'application/json; charset=utf-8',
        'Authorization': 'Bearer $token',
      };

  static Map<String, dynamic> _decode(http.Response response) {
    if (response.bodyBytes.isEmpty) return const {};
    try {
      final decoded = jsonDecode(utf8.decode(response.bodyBytes));
      return decoded is Map ? decoded.cast<String, dynamic>() : const {};
    } catch (_) {
      return const {};
    }
  }

  static Map<String, dynamic> _jsonObject(String text) {
    try {
      final decoded = jsonDecode(text);
      return decoded is Map ? decoded.cast<String, dynamic>() : const {};
    } catch (_) {
      return const {};
    }
  }

  static SyncException _error(Map<String, dynamic> body, String fallback) {
    final code = body['error'] as String? ?? '';
    final message = body['message'] as String? ?? '';
    return SyncException(code.isEmpty ? 'http_error' : code,
        message.isEmpty ? fallback : message);
  }
}

class SyncException implements Exception {
  const SyncException(this.code, this.message);

  final String code;
  final String message;

  @override
  String toString() => message;
}

class PingInfo {
  const PingInfo({required this.serverName, required this.desktopVersion});

  final String serverName;
  final String desktopVersion;
}

class PairResult {
  const PairResult({
    required this.deviceId,
    required this.token,
    required this.pairedAt,
  });

  final String deviceId;
  final String token;
  final String pairedAt;
}

class PushResult {
  const PushResult({
    required this.clientOpId,
    required this.status,
    required this.serverId,
    required this.serverVersion,
    required this.seqNo,
    required this.serverPayload,
    required this.message,
  });

  final int clientOpId;

  /// applied / conflict / rejected / duplicate。
  final String status;

  final String serverId;
  final int serverVersion;
  final String seqNo;
  final dynamic serverPayload;
  final String message;
}

class PullResult {
  const PullResult({
    required this.cursor,
    required this.hasMore,
    required this.changes,
  });

  final int cursor;
  final bool hasMore;
  final List<PullChange> changes;
}

class PullChange {
  const PullChange({
    required this.seq,
    required this.entity,
    required this.entityId,
    required this.op,
    required this.createdAt,
    required this.payload,
  });

  final int seq;
  final String entity;
  final String entityId;
  final String op;
  final String createdAt;
  final dynamic payload;
}

class AttachmentUploadResult {
  const AttachmentUploadResult({
    required this.ok,
    this.storedPath = '',
    this.size = 0,
    this.error = '',
    this.message = '',
  });

  final bool ok;
  final String storedPath;
  final int size;
  final String error;
  final String message;
}
