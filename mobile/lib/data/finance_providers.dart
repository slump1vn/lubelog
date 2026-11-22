import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/generated/soitho_api.dart';
import '../app/providers.dart';
import 'cached.dart';
import 'snapshot_cache.dart';

final snapshotCacheProvider = Provider<SnapshotCache>(
  (ref) => SecureSnapshotCache(),
);

final nowProvider = Provider<DateTime Function()>((ref) => DateTime.now);

/// Shows the saved snapshot at once (stale-while-revalidate), then the
/// server's answer. When the server cannot be reached the snapshot stays,
/// marked offline.
abstract class SnapshotNotifier<T> extends AsyncNotifier<Cached<T>> {
  String get cacheKey;
  Future<T> fetch(ApiClient api);
  T decode(Map<String, Object?> json);
  Map<String, Object?> encode(T value);

  @override
  Future<Cached<T>> build() async {
    final saved = await ref.read(snapshotCacheProvider).read(cacheKey);
    if (saved != null) {
      final cached = Cached(decode(saved.json), savedAt: saved.savedAt);
      // Revalidate after the first frame shows the saved numbers.
      unawaited(Future.microtask(refresh));
      return cached;
    }
    return _load();
  }

  Future<Cached<T>> _load() async {
    final value = await fetch(ref.read(apiProvider));
    final now = ref.read(nowProvider)();
    await ref.read(snapshotCacheProvider).write(cacheKey, encode(value), now);
    return Cached(value, savedAt: now);
  }

  /// Pull to refresh. Keeps what is on screen if the refresh fails.
  Future<void> refresh() async {
    try {
      final fresh = await _load();
      if (ref.mounted) state = AsyncData(fresh);
    } on Object catch (e, st) {
      if (!ref.mounted) return;
      final current = state.value;
      // Keep the numbers on screen, marked as old, rather than an error page.
      state = current != null
          ? AsyncData(current.markOffline())
          : AsyncError(e, st);
    }
  }
}

final dashboardProvider =
    AsyncNotifierProvider<DashboardNotifier, Cached<Dashboard>>(
      DashboardNotifier.new,
    );

class DashboardNotifier extends SnapshotNotifier<Dashboard> {
  @override
  String get cacheKey => 'dashboard';
  @override
  Future<Dashboard> fetch(ApiClient api) => api.getDashboard();
  @override
  Dashboard decode(Map<String, Object?> json) => Dashboard.fromJson(json);
  @override
  Map<String, Object?> encode(Dashboard value) => value.toJson();
}

final budgetProvider = AsyncNotifierProvider<BudgetNotifier, Cached<Budget>>(
  BudgetNotifier.new,
);

class BudgetNotifier extends SnapshotNotifier<Budget> {
  @override
  String get cacheKey => 'budget';
  @override
  Future<Budget> fetch(ApiClient api) async {
    try {
      return await api.getBudget();
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) throw const NotSetUp();
      rethrow;
    }
  }

  @override
  Budget decode(Map<String, Object?> json) => Budget.fromJson(json);
  @override
  Map<String, Object?> encode(Budget value) => value.toJson();
}

/// The transaction list: newest first, older pages loaded while scrolling.
class TransactionList {
  const TransactionList({
    required this.items,
    required this.nextCursor,
    required this.savedAt,
    this.offline = false,
    this.loadingMore = false,
    this.loadMoreError,
  });

  final List<Transaction> items;
  final String? nextCursor;
  final DateTime savedAt;
  final bool offline;
  final bool loadingMore;
  final Object? loadMoreError;

  bool get hasMore => nextCursor != null;

  TransactionList copyWith({
    List<Transaction>? items,
    String? Function()? nextCursor,
    bool? offline,
    bool? loadingMore,
    Object? Function()? loadMoreError,
  }) => TransactionList(
    items: items ?? this.items,
    nextCursor: nextCursor != null ? nextCursor() : this.nextCursor,
    savedAt: savedAt,
    offline: offline ?? this.offline,
    loadingMore: loadingMore ?? this.loadingMore,
    loadMoreError: loadMoreError != null ? loadMoreError() : this.loadMoreError,
  );
}

final transactionsProvider =
    AsyncNotifierProvider<TransactionsNotifier, TransactionList>(
      TransactionsNotifier.new,
    );

class TransactionsNotifier extends AsyncNotifier<TransactionList> {
  static const pageSize = 50;
  static const _cacheKey = 'transactions';

  @override
  Future<TransactionList> build() async {
    final saved = await ref.read(snapshotCacheProvider).read(_cacheKey);
    if (saved != null) {
      final page = TransactionPage.fromJson(saved.json);
      unawaited(Future.microtask(refresh));
      return TransactionList(
        items: page.items,
        nextCursor: page.nextCursor,
        savedAt: saved.savedAt,
      );
    }
    return _firstPage();
  }

  Future<TransactionList> _firstPage() async {
    final page = await ref.read(apiProvider).listTransactions(limit: pageSize);
    final now = ref.read(nowProvider)();
    await ref.read(snapshotCacheProvider).write(_cacheKey, page.toJson(), now);
    return TransactionList(
      items: page.items,
      nextCursor: page.nextCursor,
      savedAt: now,
    );
  }

  Future<void> refresh() async {
    try {
      final fresh = await _firstPage();
      if (ref.mounted) state = AsyncData(fresh);
    } on Object catch (e, st) {
      if (!ref.mounted) return;
      final current = state.value;
      state = current != null
          ? AsyncData(current.copyWith(offline: true))
          : AsyncError(e, st);
    }
  }

  /// Loads the next older page; does nothing while one is loading or at the end.
  Future<void> loadMore() async {
    final current = state.value;
    if (current == null || !current.hasMore || current.loadingMore) return;
    state = AsyncData(
      current.copyWith(loadingMore: true, loadMoreError: () => null),
    );
    try {
      final page = await ref
          .read(apiProvider)
          .listTransactions(limit: pageSize, cursor: current.nextCursor);
      if (!ref.mounted) return;
      final seen = {for (final t in current.items) t.id};
      state = AsyncData(
        current.copyWith(
          items: [
            ...current.items,
            ...page.items.where((t) => !seen.contains(t.id)),
          ],
          nextCursor: () => page.nextCursor,
          loadingMore: false,
          loadMoreError: () => null,
        ),
      );
    } on Object catch (e) {
      if (!ref.mounted) return;
      state = AsyncData(
        current.copyWith(loadingMore: false, loadMoreError: () => e),
      );
    }
  }
}
