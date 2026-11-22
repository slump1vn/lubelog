import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../data/cached.dart';
import '../../data/reconciliation_providers.dart';
import '../../data/write_api.dart';
import '../../shared/dates.dart';
import '../../shared/money.dart';

/// What the form starts with, e.g. from a receipt photo.
class ManualEntryDraft {
  const ManualEntryDraft({this.amount, this.date, this.note = ''});
  final int? amount;
  final String? date;
  final String note;
}

/// Opens the form that explains [gap]. True when an entry was saved.
Future<bool> showManualEntrySheet(
  BuildContext context, {
  required BalanceGap gap,
  required List<String> categories,
  ManualEntryDraft draft = const ManualEntryDraft(),
}) async {
  final saved = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (_) =>
        ManualEntrySheet(gap: gap, categories: categories, draft: draft),
  );
  return saved == true;
}

/// Digits only, shown with dots: 1250000 -> "1.250.000".
class _AmountFormatter extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue old,
    TextEditingValue value,
  ) {
    final digits = value.text.replaceAll(RegExp(r'\D'), '');
    if (digits.isEmpty) return const TextEditingValue();
    final text = formatVnd(int.parse(digits)).replaceAll(' ₫', '');
    return TextEditingValue(
      text: text,
      selection: TextSelection.collapsed(offset: text.length),
    );
  }
}

int? parseAmount(String text) {
  final digits = text.replaceAll(RegExp(r'\D'), '');
  return digits.isEmpty ? null : int.tryParse(digits);
}

class ManualEntrySheet extends ConsumerStatefulWidget {
  const ManualEntrySheet({
    super.key,
    required this.gap,
    required this.categories,
    this.draft = const ManualEntryDraft(),
  });

  final BalanceGap gap;
  final List<String> categories;
  final ManualEntryDraft draft;

  @override
  ConsumerState<ManualEntrySheet> createState() => _ManualEntrySheetState();
}

class _ManualEntrySheetState extends ConsumerState<ManualEntrySheet> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _amount;
  late final TextEditingController _note;
  late String _date;
  String? _category;
  bool _saving = false;
  String? _error;

  // One key per entry: a retry after a dropped connection cannot add it twice.
  final String _idempotencyKey = newIdempotencyKey();

  @override
  void initState() {
    super.initState();
    final amount =
        widget.draft.amount != null && widget.draft.amount! <= widget.gap.amount
        ? widget.draft.amount!
        : widget.gap.amount;
    _amount = TextEditingController(
      text: formatVnd(amount).replaceAll(' ₫', ''),
    );
    _note = TextEditingController(text: widget.draft.note);
    _date = widget.draft.date ?? vietnamDay(widget.gap.toUtc);
    _category = widget.categories.isEmpty ? null : widget.categories.first;
  }

  @override
  void dispose() {
    _amount.dispose();
    _note.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final current = parseDay(_date);
    final picked = await showDatePicker(
      context: context,
      initialDate: current,
      firstDate: DateTime.now().subtract(const Duration(days: 365 * 5)),
      lastDate: DateTime.now().add(const Duration(days: 1)),
    );
    if (picked != null) setState(() => _date = isoDay(picked));
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref
          .read(financeWritesProvider)
          .addManual(
            ManualTransactionRequest(
              gapKey: widget.gap.key,
              amount: parseAmount(_amount.text)!,
              date: _date,
              category: _category ?? '',
              note: _note.text.trim(),
            ),
            idempotencyKey: _idempotencyKey,
          );
      refreshAccountViewsFrom(ref);
      if (mounted) Navigator.pop(context, true);
    } on DioException catch (e) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = describeError(e);
      });
      if (e.response?.statusCode == 409) {
        unawaited(ref.read(reconciliationProvider.notifier).refresh());
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final gap = widget.gap;
    final out = gap.direction.toJson() == 'out';
    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 8,
        bottom: MediaQuery.viewInsetsOf(context).bottom + 16,
      ),
      child: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              out ? 'Giải thích khoản tiền ra' : 'Giải thích khoản tiền vào',
              style: theme.textTheme.titleLarge,
            ),
            const SizedBox(height: 4),
            Text(
              'Chưa rõ ${formatVnd(gap.amount)}, từ ${shortMoment(gap.fromUtc)} đến ${shortMoment(gap.toUtc)}.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            TextFormField(
              key: const Key('entry-amount'),
              controller: _amount,
              keyboardType: TextInputType.number,
              inputFormatters: [_AmountFormatter()],
              decoration: const InputDecoration(
                labelText: 'Số tiền',
                suffixText: '₫',
                border: OutlineInputBorder(),
              ),
              validator: (text) {
                final v = parseAmount(text ?? '');
                if (v == null || v <= 0) return 'Nhập số tiền lớn hơn 0.';
                if (v > gap.amount) {
                  return 'Tối đa ${formatVnd(gap.amount)} (phần còn chưa rõ).';
                }
                return null;
              },
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              key: const Key('entry-date'),
              onPressed: _pickDate,
              icon: const Icon(Icons.event),
              label: Text('Ngày ${shortDay(_date)}/${parseDay(_date).year}'),
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              key: const Key('entry-category'),
              initialValue: _category,
              isExpanded: true,
              decoration: const InputDecoration(
                labelText: 'Hạng mục',
                border: OutlineInputBorder(),
              ),
              items: [
                for (final c in widget.categories)
                  DropdownMenuItem(
                    value: c,
                    child: Text(c == 'Other' ? 'Khác' : c),
                  ),
              ],
              onChanged: (v) => setState(() => _category = v),
            ),
            const SizedBox(height: 12),
            TextFormField(
              key: const Key('entry-note'),
              controller: _note,
              maxLength: 300,
              decoration: const InputDecoration(
                labelText: 'Ghi chú',
                hintText: 'Ví dụ: đi chợ, quẹt thẻ siêu thị',
                border: OutlineInputBorder(),
              ),
            ),
            if (_error != null) ...[
              Text(
                _error!,
                key: const Key('entry-error'),
                style: TextStyle(color: theme.colorScheme.error),
              ),
              const SizedBox(height: 8),
            ],
            FilledButton(
              key: const Key('entry-save'),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Lưu'),
            ),
          ],
        ),
      ),
    );
  }
}
