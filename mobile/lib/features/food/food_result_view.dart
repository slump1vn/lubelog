import 'package:flutter/material.dart';

import '../../api/generated/soitho_api.dart';
import '../../shared/issues.dart';

const _allergenNames = <String, String>{
  'gluten': 'Ngũ cốc chứa gluten',
  'crustaceans': 'Giáp xác',
  'eggs': 'Trứng',
  'fish': 'Cá',
  'peanuts': 'Đậu phộng (lạc)',
  'soy': 'Đậu nành',
  'milk': 'Sữa',
  'nuts': 'Các loại hạt cây',
  'celery': 'Cần tây',
  'mustard': 'Mù tạt',
  'sesame': 'Mè (vừng)',
  'sulphites': 'Sulfit',
  'lupin': 'Đậu lupin',
  'molluscs': 'Nhuyễn thể',
};

String nutrientName(String key) => switch (key) {
  'fat' => 'Chất béo',
  'saturates' => 'Chất béo bão hòa',
  'sugars' => 'Đường',
  'salt' => 'Muối',
  _ => key,
};

String _number(num v) {
  final s = v.toStringAsFixed(v == v.roundToDouble() ? 0 : 1);
  return s.replaceAll('.', ',');
}

/// The rating of one label: UK FSA traffic lights, WHO daily limits,
/// additives and allergens. Same rules and wording as the website.
class FoodResultView extends StatelessWidget {
  const FoodResultView({
    super.key,
    required this.reading,
    required this.analysis,
  });

  final FoodLabelReading? reading;
  final FoodLabelAnalysis analysis;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    final ratings = analysis.ratings ?? const [];
    final issues = analysis.issues ?? const [];
    final flags = analysis.flags ?? const [];
    final additives = analysis.additives ?? const [];
    final allergens = analysis.allergens ?? const [];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if ((reading?.productName ?? '').isNotEmpty)
          Text(reading!.productName!, style: theme.textTheme.titleLarge),
        if (issues.isNotEmpty)
          Card(
            color: theme.colorScheme.errorContainer,
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    'Vài con số có vẻ chưa đúng, anh chị so lại với nhãn:',
                  ),
                  for (final i in issues) Text('• ${translateMessage(i)}'),
                ],
              ),
            ),
          ),
        if (analysis.energyKcalPer100 != null)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Text(
              'Năng lượng: ${_number(analysis.energyKcalPer100!)} kcal / 100 ${reading?.kind?.toJson() == 'Drink' ? 'ml' : 'g'}',
            ),
          ),
        if (ratings.isEmpty && analysis.hasNutrition != true)
          const Padding(
            padding: EdgeInsets.only(top: 8),
            child: Text(
              'Không có số liệu dinh dưỡng để chấm điểm, chỉ kiểm tra thành phần.',
            ),
          ),
        for (final r in ratings) _RatingTile(r),
        if (ratings.isNotEmpty)
          Text(
            'Màu theo ngưỡng của Cơ quan Tiêu chuẩn Thực phẩm Anh (FSA). Giới hạn mỗi ngày theo WHO, cho người lớn ăn 2000 kcal.',
            style: quiet,
          ),
        if (flags.isNotEmpty) ...[
          const SizedBox(height: 16),
          Text('Lưu ý về thành phần', style: theme.textTheme.titleMedium),
          for (final f in flags) _NoteText(f),
        ],
        const SizedBox(height: 16),
        Text('Phụ gia', style: theme.textTheme.titleMedium),
        if (additives.isEmpty)
          const Text('Không thấy mã hay tên phụ gia trong thành phần.')
        else
          for (final a in additives)
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(
                '${a.code ?? ''} ${a.nameVi ?? a.nameEn ?? ''}'.trim(),
              ),
              subtitle: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if ((a.class2 ?? '').isNotEmpty) Text(a.class2!),
                  if (a.known == false)
                    const Text('Không có trong bảng tham chiếu'),
                  for (final n in a.notes ?? const <FoodNote>[]) _NoteText(n),
                ],
              ),
            ),
        const SizedBox(height: 16),
        Text('Chất gây dị ứng trên nhãn', style: theme.textTheme.titleMedium),
        if (allergens.isEmpty)
          const Text('Không tìm thấy.')
        else
          for (final a in allergens)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(
                a.inProfile == true ? Icons.warning_amber : Icons.info_outline,
                color: a.inProfile == true ? theme.colorScheme.error : null,
              ),
              title: Text(_allergenNames[a.group] ?? a.group ?? ''),
              subtitle: Text(
                [
                  if (a.mayContain == true) 'có thể chứa',
                  if ((a.matched ?? '').isNotEmpty) '"${a.matched}"',
                  if (a.inProfile == true) 'có trong hồ sơ dị ứng của anh chị',
                ].join(' · '),
              ),
            ),
        Text(
          'Tìm bằng cách so chữ trong phần thành phần. Nếu dị ứng nặng, luôn đọc lại nhãn gốc.',
          style: quiet,
        ),
        const SizedBox(height: 16),
        Text(
          'Đây là thông tin dinh dưỡng chung, không phải lời khuyên y tế, và không nói sản phẩm có an toàn với anh chị hay không. Nếu có bệnh, dị ứng hoặc chế độ ăn riêng, hãy hỏi bác sĩ hoặc chuyên gia dinh dưỡng.',
          style: quiet,
        ),
      ],
    );
  }
}

class _NoteText extends StatelessWidget {
  const _NoteText(this.note);
  final FoodNote note;

  @override
  Widget build(BuildContext context) {
    final caution = note.level?.toJson() == 'Caution';
    final text = note.textVi ?? note.textEn ?? '';
    final source = (note.sourceName ?? '').isEmpty
        ? ''
        : ' (${note.sourceName})';
    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: Text(
        '${caution ? '⚠ ' : ''}$text$source',
        style: caution
            ? TextStyle(color: Theme.of(context).colorScheme.error)
            : null,
      ),
    );
  }
}

class _RatingTile extends StatelessWidget {
  const _RatingTile(this.r);
  final NutrientRating r;

  @override
  Widget build(BuildContext context) {
    final light = r.light?.toJson() ?? 'Unknown';
    final (color, label) = switch (light) {
      'Green' => (const Color(0xFF2E7D32), 'Thấp'),
      'Amber' => (const Color(0xFFF9A825), 'Trung bình'),
      'Red' => (const Color(0xFFC62828), 'Cao'),
      _ => (Colors.grey, 'Không có trên nhãn'),
    };
    final name = nutrientName(r.nutrient?.toJson() ?? '');
    final per100 = r.per100 == null ? '' : '${_number(r.per100!)} g / 100';
    final daily = r.percentOfDailyLimit == null
        ? ''
        : ' · 1 khẩu phần = ${_number(r.percentOfDailyLimit!)}% giới hạn/ngày';
    return ListTile(
      key: ValueKey('rating-${r.nutrient?.toJson()}'),
      contentPadding: EdgeInsets.zero,
      leading: CircleAvatar(radius: 10, backgroundColor: color),
      title: Text(
        '$name: $label${r.redByPortion == true ? ' (khẩu phần lớn)' : ''}',
      ),
      subtitle: Text('$per100$daily'),
    );
  }
}
