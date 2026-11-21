import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../auth/auth_controller.dart';
import '../../data/cached.dart';
import '../../data/finance_providers.dart';
import '../../shared/money.dart';
import '../../shared/widgets.dart';

/// Read-only view of the household plan. Editing stays on the website until phase 2.
class BudgetPage extends ConsumerWidget {
  const BudgetPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(budgetProvider);
    final now = ref.watch(nowProvider)();
    final auth = ref.watch(authControllerProvider);
    final myId = auth is SignedIn ? auth.me?.id : null;
    return Scaffold(
      appBar: AppBar(title: const Text('Ngân sách')),
      body: RefreshIndicator(
        onRefresh: ref.read(budgetProvider.notifier).refresh,
        child: switch (async) {
          AsyncValue(:final value?) => ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              if (value.offline) OfflineNote(savedAt: value.savedAt, now: now),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                child: _Plan(value.value, myId: myId),
              ),
            ],
          ),
          AsyncValue(error: NotSetUp()) => const MessageView(
            icon: Icons.pie_chart_outline,
            message: 'Chưa thiết lập ngân sách. Anh chị thiết lập trên trang web, mục Ngân sách, rồi kéo xuống để tải lại.',
          ),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(budgetProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}

String fundKindLabel(FundKind kind) => switch (kind.toJson()) {
  'shared' => 'Chi tiêu chung',
  'personal' => 'Cá nhân',
  'debt' => 'Trả nợ',
  'emergency' => 'Dự phòng',
  'savings' => 'Tiết kiệm',
  final other => other,
};

class _Plan extends StatelessWidget {
  const _Plan(this.b, {required this.myId});

  final Budget b;
  final int? myId;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final fundNames = {for (final f in b.funds) f.key: f.name};
    final monthlyIn = b.incomes.fold<int>(0, (s, i) => s + i.amount);
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Thu nhập mỗi tháng', style: quiet),
                Text(
                  formatVnd(monthlyIn),
                  style: theme.textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  'Kỳ ngân sách bắt đầu ngày ${b.cycleStartDay} hằng tháng.',
                ),
                Text(
                  'Khoản chi ngoài kế hoạch từ ${formatVnd(b.approvalThreshold)} cần cả hai đồng ý.',
                ),
              ],
            ),
          ),
        ),
        if (b.incomes.isNotEmpty) ...[
          const SectionTitle('Ngày lương'),
          for (final income in b.incomes)
            Card(
              margin: const EdgeInsets.only(bottom: 8),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            '${income.name} · ngày ${income.payDay}',
                            style: theme.textTheme.titleSmall,
                          ),
                        ),
                        Text(
                          formatVnd(income.amount),
                          style: theme.textTheme.titleSmall,
                        ),
                      ],
                    ),
                    for (final (i, a) in income.allocations.indexed)
                      Padding(
                        padding: const EdgeInsets.only(top: 6),
                        child: Row(
                          children: [
                            Text('${i + 1}. ', style: quiet),
                            Expanded(
                              child: Text(fundNames[a.fundKey] ?? a.fundKey),
                            ),
                            Text(formatVnd(a.amount)),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
            ),
        ],
        if (b.funds.isNotEmpty) ...[
          const SectionTitle('Các quỹ'),
          for (final f in b.funds)
            Card(
              margin: const EdgeInsets.only(bottom: 8),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            f.name,
                            style: theme.textTheme.titleSmall,
                          ),
                        ),
                        Text(
                          '${formatVnd(f.monthlyAmount)}/tháng',
                          style: theme.textTheme.titleSmall,
                        ),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      [
                        fundKindLabel(f.kind),
                        if (f.ownerUserId != null && f.ownerUserId == myId)
                          'của bạn',
                        if (f.targetAmount != null)
                          'mục tiêu ${formatVnd(f.targetAmount!)}',
                      ].join(' · '),
                      style: quiet,
                    ),
                    for (final c in f.categories)
                      Padding(
                        padding: const EdgeInsets.only(top: 6),
                        child: Row(
                          children: [
                            Expanded(child: Text(c.name)),
                            Text(
                              'tối đa ${formatVnd(c.monthlyLimit)}',
                              style: quiet,
                            ),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
            ),
        ],
        if (b.debts.isNotEmpty) ...[
          const SectionTitle('Khoản nợ'),
          Card(
            child: Column(
              children: [
                for (final d in b.debts)
                  ListTile(
                    title: Text(d.name),
                    subtitle: Text(
                      [
                        'Trả ngày ${d.dueDay}',
                        if (d.remainingBalance != null)
                          'còn ${formatVnd(d.remainingBalance!)}',
                        if (d.annualInterestRate != null)
                          'lãi ${d.annualInterestRate}%/năm',
                      ].join(' · '),
                    ),
                    trailing: Text(formatVnd(d.monthlyPayment)),
                  ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 16),
        Text(
          'Muốn sửa ngân sách, anh chị dùng trang web. App sẽ sửa được ở bản sau.',
          style: quiet,
          textAlign: TextAlign.center,
        ),
      ],
    );
  }
}
