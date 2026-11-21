import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../data/cached.dart';
import '../../data/finance_providers.dart';
import '../../shared/dates.dart';
import '../../shared/widgets.dart';
import 'transaction_tile.dart';

class TransactionsPage extends ConsumerStatefulWidget {
  const TransactionsPage({super.key});

  @override
  ConsumerState<TransactionsPage> createState() => _TransactionsPageState();
}

class _TransactionsPageState extends ConsumerState<TransactionsPage> {
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.extentAfter < 600) {
        ref.read(transactionsProvider.notifier).loadMore();
      }
    });
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(transactionsProvider);
    final notifier = ref.read(transactionsProvider.notifier);
    final now = ref.watch(nowProvider)();
    return Scaffold(
      appBar: AppBar(title: const Text('Giao dịch')),
      body: RefreshIndicator(
        onRefresh: notifier.refresh,
        child: switch (async) {
          AsyncValue(:final value?) => _List(
            list: value,
            now: now,
            controller: _scroll,
            onRetryMore: notifier.loadMore,
          ),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(transactionsProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}

/// A day header followed by that day's transactions.
sealed class _Row {}

class _Header extends _Row {
  _Header(this.text);
  final String text;
}

class _Item extends _Row {
  _Item(this.tx);
  final Transaction tx;
}

List<_Row> _groupByDay(List<Transaction> items, DateTime now) {
  final rows = <_Row>[];
  String? last;
  for (final tx in items) {
    final header = dayHeader(tx.occurredAtUtc.toLocal(), now);
    if (header != last) {
      rows.add(_Header(header));
      last = header;
    }
    rows.add(_Item(tx));
  }
  return rows;
}

class _List extends StatelessWidget {
  const _List({
    required this.list,
    required this.now,
    required this.controller,
    required this.onRetryMore,
  });

  final TransactionList list;
  final DateTime now;
  final ScrollController controller;
  final VoidCallback onRetryMore;

  @override
  Widget build(BuildContext context) {
    if (list.items.isEmpty) {
      return const MessageView(
        icon: Icons.receipt_long_outlined,
        message: 'Chưa có giao dịch nào của tài khoản chung.',
      );
    }
    final rows = _groupByDay(list.items, now);
    final theme = Theme.of(context);
    return CustomScrollView(
      controller: controller,
      physics: const AlwaysScrollableScrollPhysics(),
      slivers: [
        if (list.offline)
          SliverToBoxAdapter(
            child: OfflineNote(savedAt: list.savedAt, now: now),
          ),
        SliverList.builder(
          itemCount: rows.length,
          itemBuilder: (context, i) => switch (rows[i]) {
            _Header(:final text) => Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
              child: Text(
                text,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: theme.colorScheme.primary,
                ),
              ),
            ),
            _Item(:final tx) => TransactionTile(tx),
          },
        ),
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Center(
              child: list.loadingMore
                  ? const CircularProgressIndicator()
                  : list.loadMoreError != null
                  ? OutlinedButton(
                      onPressed: onRetryMore,
                      child: Text(
                        '${describeError(list.loadMoreError!)} Thử lại',
                        textAlign: TextAlign.center,
                      ),
                    )
                  : list.hasMore
                  ? const SizedBox(height: 24)
                  : Text(
                      'Đã hết giao dịch',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
            ),
          ),
        ),
      ],
    );
  }
}
