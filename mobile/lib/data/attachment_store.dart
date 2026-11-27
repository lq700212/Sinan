import 'dart:io';
import 'dart:math';

import 'package:flutter/services.dart'
    show MissingPluginException, PlatformException;
import 'package:image_picker/image_picker.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import '../models/rules.dart';

/// 现场照片的采集与落盘。
///
/// 口径（AGENTS.md 第 13 节 + 调研与方案）：
/// - 长边压到 1600px、单张 ≤2 MB，由 `image_picker` 的 maxWidth/imageQuality 完成；
/// - 文件存 `文档目录/attachments/{issueId}/{uuid}.jpg`，数据库只存相对路径；
/// - 原图留本机，同步时再补传，不在这里做网络请求。
class AttachmentStore {
  AttachmentStore({ImagePicker? picker}) : _picker = picker ?? ImagePicker();

  final ImagePicker _picker;
  final _random = Random();

  /// 从相机或相册取一张图并复制到应用目录。
  /// 用户取消返回 null；调用方负责写数据库与刷新界面。
  Future<PickedAttachment?> pick({
    required String issueId,
    required bool fromCamera,
  }) async {
    final tempPath = await pickTemp(fromCamera: fromCamera);
    if (tempPath == null) return null;
    return persist(tempPath, issueId: issueId);
  }

  /// 只取图不落应用目录：新建问题时还没有 issueId，先拿系统缓存路径，
  /// 保存问题后再 `persist` 到 `attachments/{issueId}/`。
  ///
  /// 异常统一翻成 [AttachmentException]：image_picker 抛的是 PlatformException，
  /// 上层若只写 `await pickImage(...)` 而不接，现场看到的就是"点了拍照没反应"。
  /// 这里把常见 code 翻成能照着做的中文，页面直接展示。
  Future<String?> pickTemp({required bool fromCamera}) async {
    try {
      final picked = await _picker.pickImage(
        source: fromCamera ? ImageSource.camera : ImageSource.gallery,
        maxWidth: 1600,
        maxHeight: 1600,
        imageQuality: 82,
        preferredCameraDevice: CameraDevice.rear,
      );
      return picked?.path;
    } on PlatformException catch (e) {
      throw AttachmentException(_friendlyMessage(e, fromCamera: fromCamera));
    } on MissingPluginException {
      throw const AttachmentException(
        '拍照组件没有正确加载，请重新安装 App；若仍失败请把机型反馈给开发。',
      );
    }
  }

  /// 把 image_picker 的错误码翻成现场能执行的提示。
  /// 常见码：camera_access_denied / photo_access_denied / no_available_camera。
  static String _friendlyMessage(PlatformException e,
      {required bool fromCamera}) {
    switch (e.code) {
      case 'camera_access_denied':
        return '相机权限被拒绝。请到 系统设置 → 应用 → 司南 → 权限，'
            '允许使用相机后重试。';
      case 'photo_access_denied':
        return '相册权限被拒绝。请到系统设置里允许「司南」访问照片后重试。';
      case 'no_available_camera':
        return '没有找到可用的相机。请确认设备相机能正常打开，'
            '或改用相册选图。';
      default:
        final detail = (e.message == null || e.message!.isEmpty)
            ? e.code
            : e.message!;
        return fromCamera ? '打开相机失败：$detail' : '打开相册失败：$detail';
    }
  }

  /// 把任意本地图片复制进应用目录，返回相对路径与文件信息。
  /// 单独暴露出来是为了让测试可以不依赖相机 UI 直接验证落盘逻辑。
  Future<PickedAttachment> persist(String sourcePath, {required String issueId}) async {
    final root = await getApplicationDocumentsDirectory();
    final storedName = '${_uuid()}.jpg';
    final relative = Rules.attachmentRelativePath(issueId, storedName);
    final target = File(p.join(root.path, relative));
    await target.parent.create(recursive: true);
    await File(sourcePath).copy(target.path);

    final length = await target.length();
    return PickedAttachment(
      fileName: p.basename(sourcePath),
      storedName: storedName,
      storedPath: relative,
      size: length,
      absolutePath: target.path,
    );
  }

  /// 文档目录下的绝对路径；文件不存在返回 null，界面据此显示"文件缺失"而不是空白。
  Future<String?> absolutePath(String storedPath) async {
    final root = await getApplicationDocumentsDirectory();
    final file = File(p.join(root.path, storedPath));
    return await file.exists() ? file.path : null;
  }

  /// 不依赖 uuid 包：时间戳 + 6 位随机，单机内足够唯一（同一微秒并发几率极低）。
  String _uuid() {
    final now = DateTime.now().microsecondsSinceEpoch;
    final salt = _random.nextInt(0xFFFFFF).toRadixString(16).padLeft(6, '0');
    return '$now$salt';
  }
}

class PickedAttachment {
  const PickedAttachment({
    required this.fileName,
    required this.storedName,
    required this.storedPath,
    required this.size,
    required this.absolutePath,
  });

  final String fileName;
  final String storedName;
  final String storedPath;
  final int size;
  final String absolutePath;
}

/// 取图失败：message 是给现场看的完整句子，页面直接显示，不再套一层"未知错误"。
class AttachmentException implements Exception {
  const AttachmentException(this.message);

  final String message;

  @override
  String toString() => message;
}
