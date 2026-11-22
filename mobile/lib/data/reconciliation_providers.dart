import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/generated/soitho_api.dart';
import '../app/providers.dart';
import 'finance_providers.dart';

/// Reconciliation needs the server's latest numbers, so it is not kept offline.
final reconciliationProvider =
    AsyncNotifierProvider<ReconciliationNotifier, Reconciliation>(
      ReconciliationNotifier.new,
    );

class ReconciliationNotifier extends AsyncNotifier<Reconciliation> {
  @override
  Future<Reconciliation> build() => ref.read(apiProvider).getReconciliation();

  Future<void> refresh() async {
    final fresh = await AsyncValue.guard(
      () => ref.read(apiProvider).getReconciliation(),
    );
    if (!ref.mounted) return;
    // Keep the list on screen when only the refresh failed.
    if (fresh.hasError && state.hasValue) return;
    state = fresh;
  }
}

/// After a write, everything that shows the joint account is out of date.
void refreshAccountViewsFrom(WidgetRef ref) {
  ref.invalidate(dashboardProvider);
  ref.invalidate(transactionsProvider);
  ref.invalidate(reconciliationProvider);
}
