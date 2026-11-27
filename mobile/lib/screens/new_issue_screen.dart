import 'dart:io';

import 'package:material_ui/material_ui.dart';

import '../data/attachment_store.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import '../widgets/photo_tile.dart';
import 'machine_select_screen.dart';

/// 拍照新建（设计稿 m-new）：扫机台 → 写一句 → 拍一张，三步内记完。
///
/// 保存是"先本机落库、再补照片"：问题先写入 SQLite 并进 outbox，
/// 照片逐张复制进应用目录后关联；网络全程不参与。
class NewIssueScreen extends StatefulWidget {
  const NewIssueScreen({super.key, required this.state, this.initialMachine});

  final AppState state;
  final String? initialMachine;

  @override
  State<NewIssueScreen> createState() => _NewIssueScreenState();
}

class _NewIssueScreenState extends State<NewIssueScreen> {
  final _machine = TextEditingController();
  final _title = TextEditingController();
  final _solution = TextEditingController();
  final _owner = TextEditingController();
  final _due = TextEditingController();

  String _stageCode = StageCodes.assembly;
  String _status = IssueStatuses.assembling;
  DateTime? _dueDate;
  final List<String> _photos = [];
  bool _saving = false;
  String? _error;

  static const _maxPhotos = 9;

  @override
  void initState() {
    super.initState();
    if (widget.initialMachine != null) {
      _machine.text = widget.initialMachine!;
    }
  }

  @override
  void dispose() {
    _machine.dispose();
    _title.dispose();
    _solution.dispose();
    _owner.dispose();
    _due.dispose();
    super.dispose();
  }

  Future<void> _pickMachine() async {
    final machine = await Navigator.of(context).push<String>(
      MaterialPageRoute(
          builder: (_) => MachineSelectScreen(state: widget.state)),
    );
    if (machine != null && mounted) {
      setState(() => _machine.text = machine);
    }
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: _dueDate ?? now,
      firstDate: DateTime(now.year - 1),
      lastDate: DateTime(now.year + 3),
      helpText: '计划完成日期',
    );
    if (picked == null || !mounted) return;
    setState(() {
      _dueDate = picked;
      _due.text = '${picked.year}年${picked.month}月${picked.day}日';
    });
  }

  Future<void> _addPhoto({required bool fromCamera}) async {
    if (_photos.length >= _maxPhotos) {
      setState(() => _error = '现场照片最多 $_maxPhotos 张。');
      return;
    }
    try {
      final path =
          await widget.state.attachments.pickTemp(fromCamera: fromCamera);
      if (path == null || !mounted) return;
      setState(() {
        _photos.add(path);
        _error = null;
      });
    } on AttachmentException catch (e) {
      // 相机/相册失败必须落到表单上；不接异常的话现场就是"点了没反应"。
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) setState(() => _error = '拍照失败：$e');
    }
  }

  Future<void> _save() async {
    if (_saving) return;
    final machine = _machine.text.trim().toUpperCase();
    final title = _title.text.trim();
    if (machine.isEmpty) {
      setState(() => _error = '请先选择机台。');
      return;
    }
    if (!Rules.isMachineCode(machine,
        projectCode: widget.state.projectCode)) {
      setState(() =>
          _error = '机台号格式应为 ${widget.state.projectCode} + 3 位序号，'
              '例如 ${widget.state.projectCode}106。');
      return;
    }
    if (title.isEmpty) {
      setState(() => _error = '问题描述不能为空，写一句现场看到的现象即可。');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final issue = await widget.state.createIssue(
        machineCode: machine,
        title: title,
        solution: _solution.text,
        stageCode: _stageCode,
        ownerText: _owner.text,
        dueRaw: _dueDate == null ? '' : Rules.formatRawDate(_dueDate),
        status: _status,
      );
      for (final path in _photos) {
        await widget.state.addPhotoFromPath(issue.id, path);
      }
      if (!mounted) return;
      // 「保存并继续」：保留机台号，清空其余字段，现场可在同一台设备上连拍多条。
      setState(() {
        _saving = false;
        _title.clear();
        _solution.clear();
        _owner.clear();
        _due.clear();
        _dueDate = null;
        _photos.clear();
        _error = null;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('已保存到本机：${issue.machineCode} · 待同步队列 +1'),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = '保存失败：$e';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Scaffold(
      body: AmbientBackground(
        child: SafeArea(
          child: Column(
            children: [
              _header(context, p),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                  children: [
                    _label(p, '机台', required: true),
                    const SizedBox(height: 5),
                    GlassCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 13),
                      radius: 14,
                      onTap: _pickMachine,
                      child: Row(
                        children: [
                          Icon(
                            _machine.text.isEmpty
                                ? Icons.qr_code_scanner
                                : Icons.check_circle_outline,
                            size: 17,
                            color: _machine.text.isEmpty ? p.primary : p.ok,
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              _machine.text.isEmpty ? '选择或输入机台号' : _machine.text,
                              style: TextStyle(
                                fontSize: 14,
                                fontWeight: _machine.text.isEmpty
                                    ? FontWeight.w400
                                    : FontWeight.w700,
                                color: _machine.text.isEmpty ? p.faint : p.fg,
                                fontFeatures: const [
                                  FontFeature.tabularFigures()
                                ],
                              ),
                            ),
                          ),
                          if (_machine.text.isNotEmpty) ...[
                            Text('已选择',
                                style:
                                    TextStyle(fontSize: 9.5, color: p.ok)),
                            const SizedBox(width: 8),
                          ],
                          Text(_machine.text.isEmpty ? '选择 ›' : '重新选 ›',
                              style:
                                  TextStyle(fontSize: 11, color: p.primary)),
                        ],
                      ),
                    ),
                    const SizedBox(height: 14),
                    _label(p, '存在问题', required: true),
                    const SizedBox(height: 5),
                    TextField(
                      controller: _title,
                      maxLines: 3,
                      minLines: 2,
                      decoration: const InputDecoration(
                        hintText: '一句话说清现象，例如：夹爪等待多 1.2 秒，节拍不达标',
                      ),
                    ),
                    const SizedBox(height: 12),
                    _label(p, '解决措施'),
                    const SizedBox(height: 5),
                    TextField(
                      controller: _solution,
                      maxLines: 2,
                      minLines: 2,
                      decoration: const InputDecoration(
                        hintText: '现场可先写初步方案，回到办公室再补',
                      ),
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              _label(p, '责任阶段'),
                              const SizedBox(height: 5),
                              _StageDropdown(
                                value: _stageCode,
                                onChanged: (code) => setState(() {
                                  _stageCode = code;
                                  _status = IssueStatuses.forStage(code);
                                }),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              _label(p, '责任人'),
                              const SizedBox(height: 5),
                              TextField(
                                controller: _owner,
                                decoration: const InputDecoration(
                                  hintText: '如 王工',
                                  isDense: true,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              _label(p, '计划完成'),
                              const SizedBox(height: 5),
                              InkWell(
                                borderRadius: BorderRadius.circular(12),
                                onTap: _pickDate,
                                child: InputDecorator(
                                  decoration: const InputDecoration(
                                    isDense: true,
                                    suffixIcon: Icon(
                                        Icons.calendar_today_outlined,
                                        size: 15),
                                  ),
                                  child: Text(
                                    _due.text.isEmpty ? '选择日期' : _due.text,
                                    style: TextStyle(
                                      fontSize: 13,
                                      color:
                                          _due.text.isEmpty ? p.faint : p.fg,
                                    ),
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              _label(p, '状态'),
                              const SizedBox(height: 5),
                              _StatusDropdown(
                                value: _status,
                                onChanged: (s) => setState(() => _status = s),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 14),
                    Row(
                      children: [
                        _label(p, '现场照片'),
                        const Spacer(),
                        Text('最多 $_maxPhotos 张',
                            style:
                                TextStyle(fontSize: 10.5, color: p.faint)),
                      ],
                    ),
                    const SizedBox(height: 6),
                    SizedBox(
                      height: 96,
                      child: ListView(
                        scrollDirection: Axis.horizontal,
                        children: [
                          for (final path in _photos) ...[
                            _DraftPhoto(
                              path: path,
                              onRemove: () =>
                                  setState(() => _photos.remove(path)),
                            ),
                            const SizedBox(width: 6),
                          ],
                          SizedBox(
                            width: 92,
                            child: PhotoTile(
                              add: true,
                              addLabel: '拍照 / 相册',
                              width: 92,
                              height: 64,
                              onTap: () => _showPhotoSheet(),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    GlassCard(
                      inset: true,
                      radius: 12,
                      padding: const EdgeInsets.fromLTRB(10, 9, 10, 9),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Container(
                            width: 16,
                            height: 16,
                            alignment: Alignment.center,
                            decoration: BoxDecoration(
                              color: p.infoSoft,
                              borderRadius: BorderRadius.circular(4),
                            ),
                            child: Text('i',
                                style: TextStyle(
                                    fontSize: 10,
                                    fontWeight: FontWeight.w700,
                                    color: p.info)),
                          ),
                          const SizedBox(width: 7),
                          Expanded(
                            child: Text(
                              '保存后这台手机上有 ${widget.state.pendingOutbox + 1} 条待传。'
                              '回到局域网打开 App 会自动补传，不需要手动点。',
                              style: TextStyle(
                                  fontSize: 10, height: 1.5, color: p.muted),
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: 8),
                      Text(_error!, style: TextStyle(fontSize: 11.5, color: p.ng)),
                    ],
                  ],
                ),
              ),
              _actionBar(p),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header(BuildContext context, SinanPalette p) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(6, 6, 16, 8),
      child: Row(
        children: [
          IconButton(
            onPressed: _saving ? null : () => Navigator.of(context).pop(),
            icon: Icon(Icons.close, size: 20, color: p.muted),
            tooltip: '取消',
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('新建问题',
                    style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
                Text('保存到本机，联网后自动上传',
                    style: TextStyle(fontSize: 10.5, color: p.faint)),
              ],
            ),
          ),
          const SinanChip(text: '离线', tone: 'warn', dense: true),
        ],
      ),
    );
  }

  Widget _actionBar(SinanPalette p) {
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
      decoration: BoxDecoration(
        color: p.panel.withValues(alpha: 0.92),
        border: Border(top: BorderSide(color: p.sepLine)),
      ),
      child: Row(
        children: [
          Expanded(
            flex: 2,
            child: OutlinedButton(
              onPressed: _saving ? null : () => Navigator.of(context).pop(),
              style: OutlinedButton.styleFrom(
                  minimumSize: const Size.fromHeight(46)),
              child: const Text('取消'),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            flex: 5,
            child: FilledButton(
              onPressed: _saving ? null : _save,
              style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(46)),
              child: _saving
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child:
                          CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                    )
                  : const Text('保存并继续'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _label(SinanPalette p, String text, {bool required = false}) {
    return Row(
      children: [
        Text(text,
            style: TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w600,
                color: p.muted)),
        if (required)
          Text(' *', style: TextStyle(fontSize: 11.5, color: p.ng)),
      ],
    );
  }

  Future<void> _showPhotoSheet() async {
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('拍照'),
              subtitle: const Text('压缩到长边 1600px'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _addPhoto(fromCamera: true);
              },
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('从相册选择'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _addPhoto(fromCamera: false);
              },
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }
}

/// 新建页的待上传照片卡：缩略图 + 真实文件名 + 真实大小。
/// 文件名取系统临时路径的 basename，不伪装成相机原文件名；大小读文件本身。
class _DraftPhoto extends StatelessWidget {
  const _DraftPhoto({required this.path, required this.onRemove});

  final String path;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final name = path.split(RegExp(r'[\\/]')).last;
    var sizeText = '';
    try {
      final bytes = File(path).lengthSync();
      sizeText = bytes >= 1024 * 1024
          ? '${(bytes / 1024 / 1024).toStringAsFixed(1)} MB'
          : '${(bytes / 1024).round()} KB';
    } catch (_) {
      // 文件读不到时只显示"已压缩"，不编一个大小。
    }
    return SizedBox(
      width: 92,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Stack(
            children: [
              PhotoTile(
                filePath: path,
                width: 92,
                height: 64,
                onTap: onRemove,
              ),
              Positioned(
                top: 2,
                right: 2,
                child: GestureDetector(
                  onTap: onRemove,
                  child: Container(
                    width: 19,
                    height: 19,
                    decoration: const BoxDecoration(
                      color: Colors.black54,
                      shape: BoxShape.circle,
                    ),
                    child: const Icon(Icons.close,
                        size: 12, color: Colors.white),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 3),
          Text(name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 9, color: p.muted)),
          Text(sizeText.isEmpty ? '已压缩' : '$sizeText · 已压缩',
              style: TextStyle(fontSize: 8, color: p.faint)),
        ],
      ),
    );
  }
}

class _StageDropdown extends StatelessWidget {
  const _StageDropdown({required this.value, required this.onChanged});

  final String value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    return DropdownButtonFormField<String>(
      key: ValueKey('stage-$value'),
      initialValue: value,
      isDense: true,
      decoration: const InputDecoration(isDense: true),
      items: [
        for (final code in StageCodes.all)
          DropdownMenuItem(value: code, child: Text(StageCodes.label(code))),
      ],
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
    );
  }
}

class _StatusDropdown extends StatelessWidget {
  const _StatusDropdown({required this.value, required this.onChanged});

  final String value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final list = IssueStatuses.all.contains(value)
        ? IssueStatuses.all
        : [value, ...IssueStatuses.all];
    return DropdownButtonFormField<String>(
      key: ValueKey('status-$value'),
      initialValue: value,
      isDense: true,
      decoration: const InputDecoration(isDense: true),
      items: [
        for (final s in list) DropdownMenuItem(value: s, child: Text(s)),
      ],
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
    );
  }
}
