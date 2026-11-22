import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../app/providers.dart';
import '../../data/cached.dart';
import '../../data/reconciliation_providers.dart';
import '../../shared/dates.dart';
import '../../shared/money.dart';
import '../../shared/photos.dart';
import '../../shared/widgets.dart';
import '../reconciliation/manual_entry_sheet.dart';
import '../transactions/transaction_tile.dart';

/// Photo of a receipt or a transfer -> the AI reads it -> the family picks
/// which unexplained balance change it is. Nothing is stored until then.
class ReceiptPage extends ConsumerStatefulWidget {
  const ReceiptPage({super.key});

  @override
  ConsumerState<ReceiptPage> createState() => _ReceiptPageState();
}

class _ReceiptPageState extends ConsumerState<ReceiptPage> {
  bool _reading = false;
  ReceiptReading? _result;
  String? _error;

  Future<void> _pick(PhotoFrom from) async {
    final photo = await ref.read(photoPickerProvider).pickOne(from);
    if (photo == null || !mounted) return;
    setState(() {
      _reading = true;
      _error = null;
      _result = null;
    });
    try {
      final result = await ref
          .read(apiProvider)
          .readReceipt(
            file: MultipartFile.fromBytes(photo.bytes, filename: photo.name),
          );
      if (mounted) setState(() => _result = result);
    } on DioException catch (e) {
      if (mounted) setState(() => _error = describeError(e));
    } finally {
      if (mounted) setState(() => _reading = false);
    }
  }

  Future<void> _explain(BalanceGap gap) async {
    final r = _result!;
    final categories =
        ref.read(reconciliationProvider).value?.categories ?? const ['Other'];
    final note = [
      r.vendor,
      r.content,
    ].where((s) => s.trim().isNotEmpty).join(' · ');
    final saved = await showManualEntrySheet(
      context,
      gap: gap,
      categories: categories,
      draft: ManualEntryDraft(
        amount: r.total > 0 ? r.total : null,
        date: r.date,
        note: note.length > 300 ? note.substring(0, 300) : note,
      ),
    );
    if (saved && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Đã ghi khoản chi từ hóa đơn.')),
      );
      Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final r = _result;
    return Scaffold(
      appBar: AppBar(title: const Text('Đọc hóa đơn')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            'Chụp hóa đơn mua hàng hoặc ảnh chuyển khoản. AI ở nhà đọc số tiền và ngày, rồi anh chị chọn nó khớp với khoản chênh lệch nào. Ảnh không được lưu lại.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              Expanded(
                child: FilledButton.icon(
                  key: const Key('receipt-camera'),
                  onPressed: _reading ? null : () => _pick(PhotoFrom.camera),
                  icon: const Icon(Icons.photo_camera),
                  label: const Text('Chụp ảnh'),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: OutlinedButton.icon(
                  key: const Key('receipt-gallery'),
                  onPressed: _reading ? null : () => _pick(PhotoFrom.gallery),
                  icon: const Icon(Icons.photo_library),
                  label: const Text('Chọn ảnh'),
                ),
              ),
            ],
          ),
          if (_reading) ...[
            const SizedBox(height: 32),
            const Center(child: CircularProgressIndicator()),
            const SizedBox(height: 12),
            const Text(
              'AI đang đọc ảnh, có thể mất tới một phút.',
              textAlign: TextAlign.center,
            ),
          ],
          if (_error != null) ...[
            const SizedBox(height: 24),
            Text(
              _error!,
              key: const Key('receipt-error'),
              style: TextStyle(color: theme.colorScheme.error),
            ),
          ],
          if (r != null) ..._resultView(context, r),
        ],
      ),
    );
  }

  List<Widget> _resultView(BuildContext context, ReceiptReading r) {
    final theme = Theme.of(context);
    final kind = r.kind.toJson() == 'transfer' ? 'Chuyển khoản' : 'Hóa đơn';
    return [
      const SectionTitle('AI đọc được'),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(kind, style: theme.textTheme.labelLarge),
              const SizedBox(height: 4),
              Text(
                r.total > 0 ? formatVnd(r.total) : 'Không đọc được số tiền',
                key: const Key('receipt-total'),
                style: theme.textTheme.headlineSmall,
              ),
              if (r.vendor.isNotEmpty) Text(r.vendor),
              Text(
                r.date == null
                    ? 'Không thấy ngày trên ảnh'
                    : 'Ngày ${shortDay(r.date!)}/${parseDay(r.date!).year}',
              ),
              if (r.content.isNotEmpty)
                Text(r.content, style: theme.textTheme.bodySmall),
              const SizedBox(height: 8),
              Text(
                'Kiểm tra lại với ảnh gốc; có thể sửa số tiền ở bước sau.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
      if (r.alreadyRecorded != null) ...[
        const SectionTitle('Đã có trong sao kê'),
        Card(
          key: const Key('receipt-recorded'),
          color: theme.colorScheme.secondaryContainer,
          child: Column(
            children: [
              const Padding(
                padding: EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Text(
                  'Khoản này có vẻ đã được ngân hàng báo về, không cần ghi thêm:',
                ),
              ),
              TransactionTile(r.alreadyRecorded!, showDate: true),
            ],
          ),
        ),
      ],
      SectionTitle(
        r.gaps.isEmpty
            ? 'Không có khoản chênh lệch phù hợp'
            : 'Khớp với khoản chênh lệch nào?',
      ),
      if (r.gaps.isEmpty)
        const Text(
          'Không có khoản tiền ra chưa rõ nào đủ lớn và gần ngày này. Có thể ngân hàng đã báo giao dịch này rồi, hoặc số dư chưa được cập nhật: kéo để đồng bộ ở màn Tổng quan rồi thử lại.',
        ),
      for (final g in r.gaps)
        Card(
          child: ListTile(
            key: ValueKey('receipt-gap-${g.key}'),
            title: Text('Tiền ra chưa rõ ${formatVnd(g.amount)}'),
            subtitle: Text(
              '${shortMoment(g.fromUtc)} → ${shortMoment(g.toUtc)}',
            ),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => _explain(g),
          ),
        ),
    ];
  }
}
