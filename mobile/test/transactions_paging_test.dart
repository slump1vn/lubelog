import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:soitho/app/providers.dart';
import 'package:soitho/data/finance_providers.dart';
import 'package:soitho/data/snapshot_cache.dart';

import 'support/fake_server.dart';
import 'support/fixtures.dart';

void main() {
  late FakeServer server;
  late ProviderContainer container;
  late MemorySnapshotCache cache;

  setUp(() {
    cache = MemorySnapshotCache();
    server = FakeServer((req, body) {
      final cursor = req.uri.queryParameters['cursor'];
      return switch (cursor) {
        null => (
          status: 200,
          body: {
            'items': [tx(5), tx(4), tx(3)],
            'nextCursor': 'c1',
          },
        ),
        // The server may repeat an item at a page edge; it must not show twice.
        'c1' => (
          status: 200,
          body: {
            'items': [tx(3), tx(2), tx(1)],
            'nextCursor': null,
          },
        ),
        _ => (status: 400, body: null),
      };
    });
    container = ProviderContainer(
      overrides: [
        dioProvider.overrideWithValue(
          Dio(BaseOptions(baseUrl: 'https://finance.test'))
            ..httpClientAdapter = server,
        ),
        snapshotCacheProvider.overrideWithValue(cache),
      ],
      retry: (_, _) => null,
    );
  });

  tearDown(() => container.dispose());

  test(
    'pages load in order, without duplicates, and stop at the end',
    () async {
      final first = await container.read(transactionsProvider.future);
      expect(first.items.map((t) => t.id), [5, 4, 3]);
      expect(first.hasMore, isTrue);
      expect(server.requests.single.options.uri.queryParameters['limit'], '50');

      await container.read(transactionsProvider.notifier).loadMore();
      final all = container.read(transactionsProvider).value!;
      expect(all.items.map((t) => t.id), [5, 4, 3, 2, 1]);
      expect(all.hasMore, isFalse);

      await container.read(transactionsProvider.notifier).loadMore();
      expect(server.requests, hasLength(2), reason: 'nothing more to load');
    },
  );

  test('only the first page is kept on the phone', () async {
    await container.read(transactionsProvider.future);
    await container.read(transactionsProvider.notifier).loadMore();
    final saved = await cache.read('transactions');
    expect((saved!.json['items']! as List).length, 3);
  });

  test('a failed older page keeps the list and can be tried again', () async {
    await container.read(transactionsProvider.future);
    final ok = server.handler;
    server.handler = (req, body) => (status: 503, body: null);
    await container.read(transactionsProvider.notifier).loadMore();
    var list = container.read(transactionsProvider).value!;
    expect(list.items, hasLength(3));
    expect(list.loadMoreError, isNotNull);

    server.handler = ok;
    await container.read(transactionsProvider.notifier).loadMore();
    list = container.read(transactionsProvider).value!;
    expect(list.items, hasLength(5));
    expect(list.loadMoreError, isNull);
  });
}
