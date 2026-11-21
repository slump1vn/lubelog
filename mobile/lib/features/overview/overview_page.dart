import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/generated/soitho_api.dart';
import '../../data/finance_providers.dart';
import '../../shared/balance_chart.dart';
import '../../shared/dates.dart';
import '../../shared/money.dart';
import '../../shared/widgets.dart';
import '../transactions/transaction_tile.dart';

class OverviewPage extends ConsumerWidget {
  const OverviewPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(dashboardProvider);
    final now = ref.watch(nowProvider)();
    return Scaffold(
      appBar: AppBar(title: const Text('Tổng quan')),
      body: RefreshIndicator(
        onRefresh: ref.read(dashboardProvider.notifier).refresh,
        child: switch (async) {
          AsyncValue(:final value?) => ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              if (value.offline) OfflineNote(savedAt: value.savedAt, now: now),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                child: _Dashboard(value.value, now: now),
              ),
            ],
          ),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(dashboardProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}

class _Dashboard extends StatelessWidget {
  const _Dashboard(this.d, {required this.now});

  final Dashboard d;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _BalanceCard(d.account, history: d.balanceHistory, now: now),
        const SizedBox(height: 12),
        _CycleCard(d.cycle),
        if (d.upcoming.isNotEmpty) ...[
          const SectionTitle('Sắp tới'),
          Card(
            child: Column(
              children: [for (final u in d.upcoming) _UpcomingTile(u)],
            ),
          ),
        ],
        SectionTitle(
          'Giao dịch gần đây',
          trailing: TextButton(
            onPressed: () => context.go('/transactions'),
            child: const Text('Xem tất cả'),
          ),
        ),
        Card(
          child: d.recent.isEmpty
              ? const Padding(
                  padding: EdgeInsets.all(16),
                  child: Text('Chưa có giao dịch nào.'),
                )
              : Column(
                  children: [
                    for (final t in d.recent)
                      TransactionTile(t, showDate: true),
                  ],
                ),
        ),
      ],
    );
  }
}

class _BalanceCard extends StatelessWidget {
  const _BalanceCard(this.account, {required this.history, required this.now});

  final Account account;
  final List<BalancePoint> history;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    if (!account.linked) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: Text(
            'Chưa liên kết tài khoản chung. Thiết lập ở trang web, mục Ngân sách.',
          ),
        ),
      );
    }
    final lastSync = account.lastSyncUtc;
    final syncFailed = account.lastSyncSucceeded == false;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Tài khoản chung · ${account.bankName} ${account.maskedNumber}',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              account.balance == null
                  ? 'Chưa có số dư'
                  : formatVnd(account.balance!),
              key: const Key('balance'),
              style: theme.textTheme.headlineMedium?.copyWith(
                fontWeight: FontWeight.w600,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
            if (lastSync != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Row(
                  children: [
                    if (syncFailed) ...[
                      Icon(
                        Icons.warning_amber,
                        size: 16,
                        color: theme.colorScheme.error,
                      ),
                      const SizedBox(width: 4),
                    ],
                    Expanded(
                      child: Text(
                        syncFailed
                            ? 'Lần đồng bộ ngân hàng gần nhất bị lỗi (${savedAtLabel(lastSync, now)})'
                            : 'Ngân hàng cập nhật lúc ${savedAtLabel(lastSync, now)}',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: syncFailed
                              ? theme.colorScheme.error
                              : theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            if (history.length >= 2) ...[
              const SizedBox(height: 16),
              BalanceChart(
                points: [
                  for (final p in history) (parseDay(p.date), p.balance),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _CycleCard extends StatelessWidget {
  const _CycleCard(this.c);

  final Cycle c;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Kỳ ${shortDay(c.start)} – ${shortDay(c.end)}',
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            LinearProgressIndicator(
              value: c.days == 0 ? 0 : (c.day / c.days).clamp(0, 1).toDouble(),
              semanticsLabel: 'Ngày ${c.day} trên ${c.days} của kỳ',
            ),
            const SizedBox(height: 4),
            Text('Ngày ${c.day}/${c.days}', style: quiet),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(child: _Figure('Tiền vào', c.moneyIn, incoming: true)),
                Expanded(
                  child: _Figure('Tiền ra', c.moneyOut, incoming: false),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Text(
              'Chênh lệch kỳ này: ${c.net >= 0 ? '+' : '−'}${formatVnd(c.net.abs())}',
              style: theme.textTheme.bodyMedium,
            ),
            if (c.projectedOut != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'Theo nhịp hiện tại, cả kỳ chi khoảng ${formatVnd(c.projectedOut!)}',
                  style: quiet,
                ),
              ),
            if (c.unrecordedOut > 0 || c.unrecordedIn > 0)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  [
                    if (c.unrecordedOut > 0)
                      'Tiền ra gồm ${formatVnd(c.unrecordedOut)} suy ra từ số dư (ngân hàng không báo giao dịch)',
                    if (c.unrecordedIn > 0)
                      'Tiền vào gồm ${formatVnd(c.unrecordedIn)} suy ra từ số dư',
                  ].join('. '),
                  style: quiet,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _Figure extends StatelessWidget {
  const _Figure(this.label, this.amount, {required this.incoming});

  final String label;
  final int amount;
  final bool incoming;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: theme.textTheme.bodySmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        AmountText(
          amount,
          incoming: incoming,
          style: theme.textTheme.titleMedium,
        ),
      ],
    );
  }
}

class _UpcomingTile extends StatelessWidget {
  const _UpcomingTile(this.u);

  final Upcoming u;

  @override
  Widget build(BuildContext context) {
    final incoming = isIncoming(u.direction);
    return ListTile(
      leading: Icon(incoming ? Icons.payments_outlined : Icons.event_outlined),
      title: Text(u.name),
      subtitle: Text('Ngày ${shortDay(u.date)}'),
      trailing: AmountText(u.amount, incoming: incoming),
    );
  }
}
