/// 现场照片附件。字段与 `shared/db/schema.sql` 的 `attachments` 对齐，
/// 移动端只保留现场记录需要的子集（同步状态在 `syncState`）。
library;

class IssueAttachment {
  const IssueAttachment({
    required this.id,
    required this.issueId,
    required this.fileName,
    required this.storedName,
    required this.storedPath,
    this.size = 0,
    this.mime = 'image/jpeg',
    this.createdAt = '',
    this.isDemo = false,
    this.syncState = 'local',
  });

  final String id;
  final String issueId;

  /// 原始文件名（相册里看到的名字），仅用于导出 manifest。
  final String fileName;

  /// 落盘文件名（uuid.jpg），避免中文/重名覆盖。
  final String storedName;

  /// 相对应用文档目录的路径：`attachments/{issueId}/{storedName}`。
  final String storedPath;

  final int size;
  final String mime;
  final String createdAt;
  final bool isDemo;

  /// local / pending / synced / failed；MVP 一律 local，配对服务接入后改 pending。
  final String syncState;

  bool get hasFile => storedPath.trim().isNotEmpty;

  /// 缩略图角标：显示拍摄时间而不是 `scaled_b24ab9b9…` 这类内部文件名，
  /// 现场人员只需要知道"什么时候拍的"。解析失败返回空串，由调用方省略。
  String get timeLabel {
    final time = DateTime.tryParse(createdAt);
    if (time == null) return '';
    final hh = time.hour.toString().padLeft(2, '0');
    final mm = time.minute.toString().padLeft(2, '0');
    return '${time.month}-${time.day} $hh:$mm';
  }
}
