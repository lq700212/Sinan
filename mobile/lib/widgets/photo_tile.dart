import 'dart:io';

import 'package:material_ui/material_ui.dart';

import '../theme/tokens.dart';

/// 照片缩略图：真实文件优先，缺文件/占位时给明确提示，不伪装成已拍摄。
class PhotoTile extends StatelessWidget {
  const PhotoTile({
    super.key,
    this.filePath,
    this.caption,
    this.onTap,
    this.add = false,
    this.demo = false,
    this.addLabel = '拍照',
    this.width = double.infinity,
    this.height = 82,
  });

  /// 绝对路径；null 表示还没有真实文件（演示占位或文件被清理）。
  final String? filePath;
  final String? caption;
  final VoidCallback? onTap;
  final bool add;
  final bool demo;

  /// 添加格的说明文字：详情页是「拍照」，新建页是「拍照 / 从相册选」。
  final String addLabel;
  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final radius = BorderRadius.circular(12);
    final Widget body;
    if (add) {
      body = Container(
        decoration: BoxDecoration(
          color: p.well,
          borderRadius: radius,
          border: Border.all(color: p.cardBorder),
        ),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.add_a_photo_outlined, size: 20, color: p.primary),
            const SizedBox(height: 4),
            Text(addLabel,
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 10.5, color: p.primary)),
          ],
        ),
      );
    } else if (filePath != null) {
      body = ClipRRect(
        borderRadius: radius,
        child: Image.file(
          File(filePath!),
          width: width,
          height: height,
          fit: BoxFit.cover,
          cacheWidth: 360,
          errorBuilder: (_, _, _) => _placeholder(p, '文件缺失'),
        ),
      );
    } else {
      body = _placeholder(p, demo ? '演示占位' : '暂无照片');
    }

    final stacked = Stack(
      fit: StackFit.expand,
      children: [
        body,
        if (caption != null && caption!.trim().isNotEmpty && filePath != null)
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Container(
              padding: const EdgeInsets.fromLTRB(6, 10, 6, 4),
              decoration: BoxDecoration(
                borderRadius: const BorderRadius.vertical(bottom: Radius.circular(12)),
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [
                    Colors.black.withValues(alpha: 0),
                    Colors.black.withValues(alpha: 0.62),
                  ],
                ),
              ),
              child: Text(
                caption!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 9, color: Colors.white),
              ),
            ),
          ),
        if (demo)
          Positioned(
            top: 5,
            right: 5,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
              decoration: BoxDecoration(
                color: p.warnSoft,
                borderRadius: BorderRadius.circular(4),
              ),
              child: Text('演示',
                  style: TextStyle(fontSize: 8.5, color: p.warn)),
            ),
          ),
      ],
    );

    final sized = SizedBox(
      width: width,
      height: height,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: radius,
          onTap: onTap,
          child: stacked,
        ),
      ),
    );
    return sized;
  }

  Widget _placeholder(SinanPalette p, String text) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: p.well,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: p.cardBorder),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.image_outlined,
              size: 20, color: p.faint.withValues(alpha: 0.8)),
          const SizedBox(height: 3),
          Text(text, style: TextStyle(fontSize: 9.5, color: p.faint)),
        ],
      ),
    );
  }
}
