import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/generated/soitho_api.dart';
import '../../app/providers.dart';
import '../../data/cached.dart';
import '../../data/reconciliation_providers.dart';
import '../../shared/dates.dart';
import '../../shared/money.dart';
import '../../shared/widgets.dart';
import '../transactions/transaction_tile.dart';
import 'manual_entry_sheet.dart';

/// Where the bank balance moved without a recorded transaction, and the
/// entries made by hand that explain it.
class ReconciliationPage extends ConsumerWidget {
  const ReconciliationPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(reconciliationProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Đối soát')),
      floatingActionButton: async.hasValue && async.value!.hasAccount
          ? FloatingActionButton.extended(
              key: const Key('scan-receipt'),
              onPressed: () => context.push('/more/reconciliation/receipt'),
              icon: const Icon(Icons.receipt_long),
              label: const Text('Chụp hóa đơn'),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: ref.read(reconciliationProvider.notifier).refresh,
        child: switch (async) {
          AsyncValue(:final value?) when !value.hasAccount => const MessageView(
            icon: Icons.account_balance_outlined,
            message: 'Chưa có tài khoản chung. Anh chị nhập số tài khoản trên trang web, mục Ngân sách.',
          ),
          AsyncValue(:final value?) => _Body(value),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(reconciliationProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}

class _Body extends ConsumerWidget {
  const _Body(this.r);
  final Reconciliation r;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
      children: [
        Text(
          r.gaps.isEmpty
              ? 'Mọi thay đổi số dư đều đã có giao dịch giải thích.'
              : 'Số dư ngân hàng thay đổi ${r.gaps.length} lần mà chưa có giao dịch nào giải thích. Chạm vào từng khoản để ghi nó là gì.',
          style: theme.textTheme.bodyMedium,
        ),
        if (r.gaps.isNotEmpty) const SectionTitle('Chưa giải thích'),
        for (final g in r.gaps)
          Card(
            child: ListTile(
              key: ValueKey('gap-${g.key}'),
              leading: Icon(
                g.direction.toJson() == 'out'
                    ? Icons.help_outline
                    : Icons.add_circle_outline,
              ),
              title: Text(
                g.direction.toJson() == 'out'
                    ? 'Tiền ra chưa rõ'
                    : 'Tiền vào chưa rõ',
              ),
              subtitle: Text(
                '${shortMoment(g.fromUtc)} → ${shortMoment(g.toUtc)}\n'
                'Số dư ${formatVnd(g.balanceBefore)} → ${formatVnd(g.balanceAfter)}'
                '${g.manualEntries > 0 ? '\nĐã ghi ${g.manualEntries} khoản trong đoạn này' : ''}',
              ),
              isThreeLine: true,
              trailing: AmountText(
                g.amount,
                incoming: g.direction.toJson() == 'in',
              ),
              onTap: () => showManualEntrySheet(
                context,
                gap: g,
                categories: r.categories,
              ),
            ),
          ),
        if (r.manualEntries.isNotEmpty) const SectionTitle('Đã ghi tay'),
        for (final t in r.manualEntries)
          Dismissible(
            key: ValueKey('manual-${t.id}'),
            direction: DismissDirection.endToStart,
            background: Container(
              alignment: Alignment.centerRight,
              padding: const EdgeInsets.only(right: 24),
              color: theme.colorScheme.errorContainer,
              child: Icon(
                Icons.delete,
                color: theme.colorScheme.onErrorContainer,
              ),
            ),
            confirmDismiss: (_) => _confirmDelete(context, ref, t),
            child: _ManualTile(t),
          ),
      ],
    );
  }

  Future<bool> _confirmDelete(
    BuildContext context,
    WidgetRef ref,
    Transaction t,
  ) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Xóa khoản ghi tay?'),
        content: Text('${formatVnd(t.amount)} · ${_label(t)}'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Hủy'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Xóa'),
          ),
        ],
      ),
    );
    if (ok != true) return false;
    try {
      await ref.read(apiProvider).deleteManualTransaction(id: t.id);
    } on DioException catch (e) {
      // Already gone (deleted on the other phone) is what we wanted.
      if (e.response?.statusCode != 404) {
        if (context.mounted) {
          ScaffoldMessenger.of(context)
              .showSnackBar(SnackBar(content: Text(describeError(e))));
        }
        return false;
      }
    }
    refreshAccountViewsFrom(ref);
    return true;
  }
}

String _label(Transaction t) {
  final category = (t.category ?? '').isEmpty || t.category == 'Other'
      ? 'Khác'
      : t.category!;
  final note = (t.note ?? '').trim();
  return note.isEmpty ? category : '$category · $note';
}

class _ManualTile extends StatelessWidget {
  const _ManualTile(this.t);
  final Transaction t;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      leading: const Icon(Icons.edit_note),
      title: Text(_label(t), maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Text(dayHeader(t.occurredAtUtc.toLocal(), DateTime.now())),
      trailing: AmountText(t.amount, incoming: isIncoming(t.direction)),
    );
  }
}
