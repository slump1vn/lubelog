import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/auth_controller.dart';

/// Phase 0: proves the sign-in works end to end (GET /api/v1/me).
/// Phase 1 replaces it with the joint-account dashboard.
class OverviewPage extends ConsumerWidget {
  const OverviewPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(authControllerProvider);
    final me = state is SignedIn ? state.me : null;
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Tổng quan')),
      body: RefreshIndicator(
        onRefresh: () => ref.read(authControllerProvider.notifier).reload(),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      me == null ? 'Đang ngoại tuyến' : 'Xin chào ${me.userName}',
                      key: const Key('greeting'),
                      style: theme.textTheme.titleLarge,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      me == null
                          ? 'Không kết nối được máy chủ. Kéo xuống để thử lại.'
                          : 'Đã kết nối an toàn với máy chủ gia đình. '
                                'Số dư và giao dịch sẽ hiện ở đây trong bản tiếp theo.',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
