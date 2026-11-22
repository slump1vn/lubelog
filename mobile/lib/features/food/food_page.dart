import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/generated/soitho_api.dart';
import '../../app/providers.dart';
import '../../data/cached.dart';
import '../../shared/issues.dart';
import '../../shared/photos.dart';
import '../../shared/widgets.dart';
import 'food_result_view.dart';

final foodScansProvider = FutureProvider.autoDispose<List<FoodScan>>(
  (ref) => ref.read(apiProvider).listFoodScans(limit: 30),
);

/// Photograph a food label, check what the AI read, then see the rating.
class FoodPage extends ConsumerStatefulWidget {
  const FoodPage({super.key});

  @override
  ConsumerState<FoodPage> createState() => _FoodPageState();
}

/// The numbers of the label the family checks before it is rated.
const _numbers = <(String, String)>[
  ('energyKcal', 'Năng lượng (kcal)'),
  ('fat', 'Chất béo (g)'),
  ('saturatedFat', 'Chất béo bão hòa (g)'),
  ('transFat', 'Chất béo trans (g)'),
  ('carbohydrate', 'Carbohydrate (g)'),
  ('sugars', 'Đường (g)'),
  ('protein', 'Đạm (g)'),
  ('salt', 'Muối (g)'),
  ('sodiumMg', 'Natri (mg)'),
  ('servingSize', 'Một khẩu phần (g hoặc ml)'),
];

class _FoodPageState extends ConsumerState<FoodPage> {
  static const _maxPhotos = 3;
  final List<PickedPhoto> _photos = [];
  bool _busy = false;
  String? _error;
  Map<String, Object?>? _reading;
  final Map<String, TextEditingController> _controllers = {};

  @override
  void dispose() {
    for (final c in _controllers.values) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _add(PhotoFrom from) async {
    final picker = ref.read(photoPickerProvider);
    final room = _maxPhotos - _photos.length;
    final picked = from == PhotoFrom.camera
        ? [?await picker.pickOne(from)]
        : await picker.pickMany(room);
    if (!mounted) return;
    setState(() => _photos.addAll(picked.take(room)));
  }

  Future<void> _read() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ref
          .read(apiProvider)
          .readFoodLabel(
            files: [
              for (final p in _photos)
                MultipartFile.fromBytes(p.bytes, filename: p.name),
            ],
          );
      if (!mounted) return;
      if (result.success != true || result.reading == null) {
        setState(
          () => _error = translateMessage(
            result.message ?? 'Không đọc được nhãn.',
          ),
        );
        return;
      }
      final json = jsonDecode(
        jsonEncode(result.reading!.toJson()),
      ) as Map<String, Object?>;
      for (final c in _controllers.values) {
        c.dispose();
      }
      _controllers
        ..clear()
        ..['productName'] = TextEditingController(
          text: '${json['productName'] ?? ''}',
        )
        ..['ingredients'] = TextEditingController(
          text: '${json['ingredients'] ?? ''}',
        );
      for (final (key, _) in _numbers) {
        final v = json[key];
        _controllers[key] = TextEditingController(
          text: v == null ? '' : '$v'.replaceAll('.', ','),
        );
      }
      setState(() => _reading = json);
    } on DioException catch (e) {
      if (mounted) setState(() => _error = describeError(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _analyze() async {
    final json = Map<String, Object?>.of(_reading!);
    json['productName'] = _controllers['productName']!.text.trim();
    json['ingredients'] = _controllers['ingredients']!.text.trim();
    for (final (key, _) in _numbers) {
      final text = _controllers[key]!.text.trim().replaceAll(',', '.');
      json[key] = text.isEmpty ? null : num.tryParse(text);
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ref
          .read(apiProvider)
          .analyzeFoodLabel(
            body: FoodAnalyzeRequest(
              reading: FoodLabelReading.fromJson(json),
              save: true,
            ),
          );
      if (!mounted) return;
      if (result.success != true || result.analysis == null) {
        setState(
          () => _error = translateMessage(
            result.message ?? 'Chưa đánh giá được.',
          ),
        );
        return;
      }
      ref.invalidate(foodScansProvider);
      await Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) =>
              FoodScanPage.fromResult(result.reading, result.analysis!),
        ),
      );
      if (mounted) {
        setState(() {
          _photos.clear();
          _reading = null;
        });
      }
    } on DioException catch (e) {
      if (mounted) setState(() => _error = describeError(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Nhãn thực phẩm'),
        actions: [
          IconButton(
            key: const Key('food-history'),
            tooltip: 'Đã quét',
            onPressed: () => context.push('/more/food/history'),
            icon: const Icon(Icons.history),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_reading == null) ...[
            Text(
              'Chụp 1 đến 3 ảnh của cùng một nhãn: bảng dinh dưỡng và phần thành phần. AI ở nhà đọc chữ, anh chị kiểm tra lại số, rồi app đánh giá theo ngưỡng của FSA (Anh) và WHO. Ảnh không được lưu.',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 16),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (var i = 0; i < _photos.length; i++)
                  Stack(
                    children: [
                      ClipRRect(
                        borderRadius: BorderRadius.circular(8),
                        child: Image.memory(
                          _photos[i].bytes,
                          errorBuilder: (_, _, _) => const SizedBox(
                            width: 96,
                            height: 96,
                            child: Icon(Icons.broken_image),
                          ),
                          width: 96,
                          height: 96,
                          fit: BoxFit.cover,
                        ),
                      ),
                      Positioned(
                        right: 0,
                        top: 0,
                        child: IconButton.filledTonal(
                          iconSize: 16,
                          onPressed: _busy
                              ? null
                              : () => setState(() => _photos.removeAt(i)),
                          icon: const Icon(Icons.close),
                        ),
                      ),
                    ],
                  ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton.icon(
                    key: const Key('food-camera'),
                    onPressed: _busy || _photos.length >= _maxPhotos
                        ? null
                        : () => _add(PhotoFrom.camera),
                    icon: const Icon(Icons.photo_camera),
                    label: const Text('Chụp'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: OutlinedButton.icon(
                    key: const Key('food-gallery'),
                    onPressed: _busy || _photos.length >= _maxPhotos
                        ? null
                        : () => _add(PhotoFrom.gallery),
                    icon: const Icon(Icons.photo_library),
                    label: const Text('Chọn ảnh'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            FilledButton(
              key: const Key('food-read'),
              onPressed: _busy || _photos.isEmpty ? null : _read,
              child: const Text('Đọc nhãn'),
            ),
          ] else
            ..._check(context),
          if (_busy) ...[
            const SizedBox(height: 24),
            const Center(child: CircularProgressIndicator()),
            const SizedBox(height: 8),
            const Text(
              'AI đang đọc, mỗi ảnh có thể mất tới một phút.',
              textAlign: TextAlign.center,
            ),
          ],
          if (_error != null) ...[
            const SizedBox(height: 16),
            Text(
              _error!,
              key: const Key('food-error'),
              style: TextStyle(color: theme.colorScheme.error),
            ),
          ],
        ],
      ),
    );
  }

  List<Widget> _check(BuildContext context) {
    final uncertain = {
      for (final u in (_reading!['uncertain'] as List?) ?? const []) '$u',
    };
    final perServing = _reading!['basis'] == 'PerServing';
    InputDecoration deco(String key, String label) => InputDecoration(
      labelText: label,
      border: const OutlineInputBorder(),
      isDense: true,
      helperText: uncertain.contains(key)
          ? 'AI không chắc số này, xem lại nhãn'
          : null,
      helperStyle: TextStyle(color: Theme.of(context).colorScheme.error),
    );
    return [
      Text(
        'Kiểm tra lại với nhãn',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 8),
      TextField(
        controller: _controllers['productName'],
        decoration: deco('productName', 'Tên sản phẩm'),
      ),
      const SizedBox(height: 8),
      SegmentedButton<bool>(
        segments: const [
          ButtonSegment(value: false, label: Text('Trên 100 g/ml')),
          ButtonSegment(value: true, label: Text('Trên 1 khẩu phần')),
        ],
        selected: {perServing},
        onSelectionChanged: (v) => setState(
          () => _reading!['basis'] = v.first ? 'PerServing' : 'Per100',
        ),
      ),
      const SizedBox(height: 8),
      for (final (key, label) in _numbers)
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: TextField(
            key: Key('food-$key'),
            controller: _controllers[key],
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: deco(key, label),
          ),
        ),
      TextField(
        controller: _controllers['ingredients'],
        minLines: 3,
        maxLines: 8,
        decoration: deco('ingredients', 'Thành phần'),
      ),
      const SizedBox(height: 12),
      // Stacked, so large text sizes still fit.
      FilledButton(
        key: const Key('food-analyze'),
        onPressed: _busy ? null : _analyze,
        child: const Text('Đánh giá và lưu'),
      ),
      TextButton(
        onPressed: _busy ? null : () => setState(() => _reading = null),
        child: const Text('Chụp lại'),
      ),
    ];
  }
}

/// A saved or just-made rating.
class FoodScanPage extends StatelessWidget {
  const FoodScanPage({
    super.key,
    required this.reading,
    required this.analysis,
    this.subtitle,
  });

  const FoodScanPage.fromResult(
    FoodLabelReading? reading,
    FoodLabelAnalysis analysis, {
    Key? key,
  }) : this(key: key, reading: reading, analysis: analysis);

  final FoodLabelReading? reading;
  final FoodLabelAnalysis analysis;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Kết quả')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (subtitle != null)
            Text(subtitle!, style: Theme.of(context).textTheme.bodySmall),
          FoodResultView(reading: reading, analysis: analysis),
        ],
      ),
    );
  }
}

class FoodHistoryPage extends ConsumerWidget {
  const FoodHistoryPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(foodScansProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Đã quét')),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(foodScansProvider.future),
        child: switch (async) {
          AsyncValue(:final value?) when value.isEmpty => const MessageView(
            icon: Icons.qr_code_scanner,
            message: 'Chưa quét nhãn nào.',
          ),
          AsyncValue(:final value?) => ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              for (final s in value)
                ListTile(
                  key: ValueKey('scan-${s.id}'),
                  title: Text(
                    (s.reading?.productName ?? '').isEmpty
                        ? '(không tên)'
                        : s.reading!.productName!,
                  ),
                  subtitle: Text(_summary(s)),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: s.analysis == null
                      ? null
                      : () => Navigator.of(context).push(
                          MaterialPageRoute<void>(
                            builder: (_) => FoodScanPage(
                              reading: s.reading,
                              analysis: s.analysis!,
                              subtitle: _when(s),
                            ),
                          ),
                        ),
                ),
            ],
          ),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(foodScansProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }

  static String _when(FoodScan s) {
    final t = s.createdAtUtc?.toLocal();
    final who = s.userName ?? '';
    return t == null ? who : '${t.day}/${t.month}/${t.year} · $who';
  }

  static String _summary(FoodScan s) {
    final a = s.analysis;
    final lights = a == null
        ? ''
        : '${a.redCount ?? 0} cao · ${a.amberCount ?? 0} trung bình · ${a.greenCount ?? 0} thấp';
    return [_when(s), lights].where((x) => x.isNotEmpty).join('\n');
  }
}
