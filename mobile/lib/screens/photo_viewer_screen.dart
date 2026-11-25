import 'dart:io';

import 'package:material_ui/material_ui.dart';

/// 全屏看大图：左右滑动切换、双指缩放，底部显示文件名与序号。
/// 不用 hero 动画：现场单手操作，稳定比炫技重要。
class PhotoViewerScreen extends StatefulWidget {
  const PhotoViewerScreen({
    super.key,
    required this.paths,
    this.initialIndex = 0,
    this.title = '现场照片',
  });

  final List<String> paths;
  final int initialIndex;
  final String title;

  @override
  State<PhotoViewerScreen> createState() => _PhotoViewerScreenState();
}

class _PhotoViewerScreenState extends State<PhotoViewerScreen> {
  late final PageController _controller =
      PageController(initialPage: widget.initialIndex);
  late int _index = widget.initialIndex;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final paths = widget.paths.where((p) => p.trim().isNotEmpty).toList();
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text('${widget.title}  ${_index + 1}/${paths.length}',
            style: const TextStyle(fontSize: 14)),
      ),
      body: paths.isEmpty
          ? const Center(
              child: Text('没有可显示的照片', style: TextStyle(color: Colors.white70)))
          : PageView.builder(
              controller: _controller,
              itemCount: paths.length,
              onPageChanged: (i) => setState(() => _index = i),
              itemBuilder: (_, i) => InteractiveViewer(
                minScale: 1,
                maxScale: 4,
                child: Center(
                  child: Image.file(
                    File(paths[i]),
                    fit: BoxFit.contain,
                    errorBuilder: (_, _, _) => const Center(
                      child: Text('照片文件已丢失',
                          style: TextStyle(color: Colors.white70)),
                    ),
                  ),
                ),
              ),
            ),
    );
  }
}
