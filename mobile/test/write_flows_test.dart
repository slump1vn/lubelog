import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:soitho/app/app.dart';
import 'package:soitho/app/config.dart';
import 'package:soitho/app/providers.dart';
import 'package:soitho/auth/auth_controller.dart';
import 'package:soitho/auth/auth_interceptor.dart';
import 'package:soitho/auth/authorizer.dart';
import 'package:soitho/auth/token_manager.dart';
import 'package:soitho/auth/token_store.dart';
import 'package:soitho/data/finance_providers.dart';
import 'package:soitho/data/snapshot_cache.dart';
import 'package:soitho/data/write_api.dart';
import 'package:soitho/lock/app_lock.dart';
import 'package:soitho/shared/issues.dart';
import 'package:soitho/shared/photos.dart';

import 'support/fake_server.dart';
import 'support/fixtures.dart';

class _Authorizer implements Authorizer {
  @override
  Future<AuthorizationGrant?> authorize() async =>
      (code: 'code-1', codeVerifier: 'v' * 43);
}

class _Unlocker implements DeviceUnlocker {
  @override
  Future<bool> isAvailable() async => true;
  @override
  Future<bool> unlock() async => true;
}

class _Photos implements PhotoPicker {
  @override
  Future<PickedPhoto?> pickOne(PhotoFrom from) async =>
      PickedPhoto(Uint8List.fromList([0xFF, 0xD8, 0xFF, 1]), 'hoa-don.jpg');
  @override
  Future<List<PickedPhoto>> pickMany(int max) async => [
    PickedPhoto(Uint8List.fromList([0xFF, 0xD8, 0xFF, 2]), 'nhan.jpg'),
  ];
}

Map<String, Object?> gap(String key, {int amount = 300000}) => {
  'key': key,
  'fromUtc': '2026-11-19T02:00:00Z',
  'toUtc': '2026-11-20T05:00:00Z',
  'balanceBefore': 12900000,
  'balanceAfter': 12600000,
  'amount': amount,
  'direction': 'out',
  'manualEntries': 0,
};

Map<String, Object?> manual(int id, {String category = 'Chung: Ăn uống'}) => {
  ...tx(id, amount: 300000),
  'source': 'manual',
  'category': category,
  'note': 'Đi chợ',
};

void main() {
  late FakeServer server;
  late MemoryTokenStore store;
  late List<Map<String, Object?>> gaps;
  late List<Map<String, Object?>> manuals;
  late Map<String, Object?> budget;
  late String etag;

  setUp(() {
    store = MemoryTokenStore()..token = 'r0';
    gaps = [gap('g1')];
    manuals = [];
    budget = jsonDecode(jsonEncode(budgetJson)) as Map<String, Object?>;
    etag = '"v1"';
    server = FakeServer((req, body) {
      final path = req.uri.path;
      switch ((req.method, path)) {
        case (_, '/oauth/token'):
          return (
            status: 200,
            body: {
              'access_token': 'a',
              'expires_in': 900,
              'refresh_token': 'r',
            },
          );
        case (_, '/api/v1/me'):
          return (
            status: 200,
            body: {
              'id': 1,
              'userName': 'soi',
              'householdId': -1,
              'isAdmin': true,
            },
          );
        case (_, '/api/v1/finance/dashboard'):
          return (status: 200, body: dashboardJson);
        case (_, '/api/v1/finance/transactions'):
          return (
            status: 200,
            body: {
              'items': [tx(3)],
              'nextCursor': null,
            },
          );
        case ('GET', '/api/v1/finance/budget'):
          return (status: 200, body: budget);
        case ('PUT', '/api/v1/finance/budget'):
          if (req.headers['If-Match'] != etag) {
            return (status: 412, body: {'title': 'Changed by someone else'});
          }
          final sent = jsonDecode(body) as Map<String, Object?>;
          if ((sent['approvalThreshold']! as int) <= 0) {
            return (
              status: 422,
              body: {
                'title': 'Invalid budget',
                'errors': [
                  {
                    'code': 'approval_threshold_must_be_greater_than_0',
                    'message': 'Approval threshold must be greater than 0',
                    'args': <String>[],
                  },
                ],
              },
            );
          }
          budget = sent;
          etag = '"v2"';
          return (status: 200, body: budget);
        case ('GET', '/api/v1/finance/reconciliation'):
          return (
            status: 200,
            body: {
              'hasAccount': true,
              'gaps': gaps,
              'manualEntries': manuals,
              'categories': ['Chung: Ăn uống', 'Other'],
            },
          );
        case ('POST', '/api/v1/finance/manual-transactions'):
          final sent = jsonDecode(body) as Map<String, Object?>;
          if (!gaps.any((g) => g['key'] == sent['gapKey'])) {
            // Already explained: the same key gets its entry back.
            return manuals.isNotEmpty
                ? (status: 200, body: manuals.first)
                : (
                    status: 409,
                    body: {
                      'errors': [
                        {
                          'code': 'gap_not_found',
                          'message': 'x',
                          'args': <String>[],
                        },
                      ],
                    },
                  );
          }
          gaps.removeWhere((g) => g['key'] == sent['gapKey']);
          manuals.add(manual(40, category: sent['category']! as String));
          return (status: 201, body: manuals.last);
        case ('DELETE', '/api/v1/finance/manual-transactions/40'):
          manuals.clear();
          gaps.add(gap('g1'));
          return (status: 204, body: null);
        case ('POST', '/api/v1/finance/account/sync'):
          return (
            status: 200,
            body: {
              'succeeded': true,
              'added': 2,
              'error': null,
              'lastRunUtc': '2026-11-21T06:00:00Z',
              'recent': false,
            },
          );
        case ('POST', '/api/v1/finance/receipts/read'):
          return (
            status: 200,
            body: {
              'kind': 'receipt',
              'vendor': 'Bách Hóa Xanh',
              'date': '2026-11-20',
              'total': 250000,
              'content': 'rau, thịt',
              'reference': '',
              'gaps': [gap('g1')],
              'alreadyRecorded': null,
            },
          );
        case ('GET', '/api/v1/me/sessions'):
          return (
            status: 200,
            body: [
              {
                'id': 7,
                'kind': 'app',
                'deviceName': 'Test phone',
                'createdUtc': '2026-11-21T01:00:00Z',
                'lastSeenUtc': '2026-11-21T06:00:00Z',
                'expiresUtc': '2026-12-21T01:00:00Z',
                'isCurrent': true,
              },
              {
                'id': 8,
                'kind': 'browser',
                'deviceName': 'Chrome trên Windows',
                'createdUtc': '2026-11-20T01:00:00Z',
                'lastSeenUtc': '2026-11-20T06:00:00Z',
                'expiresUtc': '2026-12-20T01:00:00Z',
                'isCurrent': false,
              },
            ],
          );
        case ('POST', '/api/v1/food/read'):
          return (
            status: 200,
            body: {
              'success': true,
              'message': '',
              'reading': {
                'productName': 'Bánh quy',
                'kind': 'Food',
                'basis': 'Per100',
                'fat': 22,
                'sugars': 30,
                'salt': 0.9,
                'ingredients': 'bột mì, đường, E322',
                'uncertain': ['salt'],
              },
            },
          );
        case ('POST', '/api/v1/food/analyze'):
          final sent = jsonDecode(body) as Map<String, Object?>;
          return (
            status: 200,
            body: {
              'success': true,
              'message': '',
              'scanId': 5,
              'reading': sent['reading'],
              'analysis': {
                'hasNutrition': true,
                'ratings': [
                  {
                    'nutrient': 'sugars',
                    'per100': 30,
                    'light': 'Red',
                    'redByPortion': false,
                    'dailyLimit': 50,
                  },
                ],
                'additives': [
                  {
                    'code': 'E322',
                    'nameVi': 'Lecithin',
                    'known': true,
                    'notes': <Object?>[],
                  },
                ],
                'allergens': [
                  {
                    'group': 'gluten',
                    'matched': 'bột mì',
                    'mayContain': false,
                    'inProfile': false,
                  },
                ],
                'flags': <Object?>[],
                'issues': <Object?>[],
                'redCount': 1,
                'amberCount': 0,
                'greenCount': 0,
              },
            },
          );
        case ('DELETE', '/api/v1/me/sessions/8'):
          return (status: 204, body: null);
      }
      return (status: 404, body: null);
    });
    server.headers = (req) => req.uri.path == '/api/v1/finance/budget'
        ? {
            'etag': [etag],
          }
        : {};
  });

  Widget app() {
    final config = AppConfig(
      server: Uri.parse('https://finance.test'),
      clientId: 'soitho-android',
      redirectUri: 'vn.digifusion.soitho:/oauth/callback',
    );
    return ProviderScope(
      overrides: [
        configProvider.overrideWithValue(config),
        tokenStoreProvider.overrideWithValue(store),
        tokenManagerProvider.overrideWith(
          (ref) => TokenManager(
            config: config,
            store: store,
            http: Dio()..httpClientAdapter = server,
          ),
        ),
        dioProvider.overrideWith((ref) {
          final dio = Dio(BaseOptions(baseUrl: 'https://finance.test'))
            ..httpClientAdapter = server;
          dio.interceptors.add(
            AuthInterceptor(ref.watch(tokenManagerProvider), dio),
          );
          return dio;
        }),
        authorizerProvider.overrideWithValue(_Authorizer()),
        deviceUnlockerProvider.overrideWithValue(_Unlocker()),
        deviceNamerProvider.overrideWithValue(() async => 'Test phone'),
        snapshotCacheProvider.overrideWithValue(MemorySnapshotCache()),
        photoPickerProvider.overrideWithValue(_Photos()),
        nowProvider.overrideWithValue(() => DateTime(2026, 11, 21, 14)),
      ],
      retry: (_, _) => null,
      child: const SoithoApp(),
    );
  }

  Future<void> openMore(WidgetTester tester, String key) async {
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.text('Thêm'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(Key(key)));
    await tester.pumpAndSettle();
  }

  testWidgets(
    'a gap is explained once, with one idempotency key, and can be deleted',
    (tester) async {
      await openMore(tester, 'more-reconciliation');
      expect(find.text('Tiền ra chưa rõ'), findsOneWidget);

      await tester.tap(find.byKey(const ValueKey('gap-g1')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('entry-amount')), '400000');
      await tester.tap(find.byKey(const Key('entry-save')));
      await tester.pumpAndSettle();
      expect(find.text('Tối đa 300.000 ₫ (phần còn chưa rõ).'), findsOneWidget);
      expect(
        server.paths.where((p) => p.endsWith('manual-transactions')),
        isEmpty,
      );

      await tester.enterText(find.byKey(const Key('entry-amount')), '300000');
      await tester.enterText(find.byKey(const Key('entry-note')), 'Đi chợ');
      await tester.tap(find.byKey(const Key('entry-save')));
      await tester.pumpAndSettle();

      final post = server.requests.singleWhere(
        (r) =>
            r.options.method == 'POST' &&
            r.options.path.endsWith('manual-transactions'),
      );
      final key = post.options.headers['Idempotency-Key'] as String;
      expect(
        RegExp(
          r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
        ).hasMatch(key),
        isTrue,
      );
      final sent = jsonDecode(post.body) as Map<String, Object?>;
      expect(sent, {
        'gapKey': 'g1',
        'amount': 300000,
        'date': '2026-11-20',
        'category': 'Chung: Ăn uống',
        'note': 'Đi chợ',
      });
      expect(
        find.text('Tiền ra chưa rõ'),
        findsNothing,
        reason: 'the list reloads',
      );
      expect(find.text('Chung: Ăn uống · Đi chợ'), findsOneWidget);

      await tester.drag(
        find.byKey(const ValueKey('manual-40')),
        const Offset(-500, 0),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Xóa'));
      await tester.pumpAndSettle();
      expect(server.paths, contains('/api/v1/finance/manual-transactions/40'));
      expect(find.text('Tiền ra chưa rõ'), findsOneWidget);
    },
  );

  testWidgets('a receipt photo fills the entry for the gap the family picks', (
    tester,
  ) async {
    await openMore(tester, 'more-reconciliation');
    await tester.tap(find.byKey(const Key('scan-receipt')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('receipt-camera')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('receipt-total')), findsOneWidget);
    expect(find.text('250.000 ₫'), findsOneWidget);
    final upload = server.requests.singleWhere(
      (r) => r.options.path.endsWith('receipts/read'),
    );
    expect(upload.options.data, isA<FormData>());

    await tester.tap(find.byKey(const ValueKey('receipt-gap-g1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('entry-save')));
    await tester.pumpAndSettle();
    final sent = jsonDecode(
      server.requests
          .lastWhere((r) => r.options.path.endsWith('manual-transactions'))
          .body,
    ) as Map<String, Object?>;
    expect(sent['amount'], 250000);
    expect(sent['date'], '2026-11-20');
    expect(sent['note'], 'Bách Hóa Xanh · rau, thịt');
    expect(find.text('Đã ghi khoản chi từ hóa đơn.'), findsOneWidget);
    expect(
      find.text('Đối soát'),
      findsOneWidget,
      reason: 'back on reconciliation',
    );
  });

  testWidgets('sync now reports new transactions', (tester) async {
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('sync-now')));
    await tester.pumpAndSettle();
    expect(find.text('Đã đồng bộ, có 2 giao dịch mới.'), findsOneWidget);
  });

  testWidgets('another device can be signed out, this one cannot', (
    tester,
  ) async {
    await openMore(tester, 'more-sessions');
    expect(find.text('Máy này'), findsOneWidget);
    expect(find.byKey(const ValueKey('end-session-7')), findsNothing);
    await tester.tap(find.byKey(const ValueKey('end-session-8')));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Đăng xuất'));
    await tester.pumpAndSettle();
    expect(
      server.requests.where(
        (r) =>
            r.options.method == 'DELETE' &&
            r.options.path.endsWith('/me/sessions/8'),
      ),
      hasLength(1),
    );
  });

  testWidgets(
    'the budget is saved with If-Match; a rule error is shown in Vietnamese',
    (tester) async {
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();
      await tester.tap(find.text('Ngân sách'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('budget-edit')));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(const Key('budget-approvalThreshold')),
        '0',
      );
      await tester.tap(find.byKey(const Key('budget-save')));
      await tester.pumpAndSettle();
      expect(find.text('• Mức cần đồng ý phải lớn hơn 0.'), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('budget-approvalThreshold')),
        '2000000',
      );
      await tester.tap(find.byKey(const Key('budget-save')));
      await tester.pumpAndSettle();
      final put = server.requests.lastWhere((r) => r.options.method == 'PUT');
      expect(put.options.headers['If-Match'], '"v1"');
      expect(budget['approvalThreshold'], 2000000);
      expect(find.text('Đã lưu ngân sách.'), findsOneWidget);
    },
  );

  testWidgets('when the partner saved first, nothing is overwritten', (
    tester,
  ) async {
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.text('Ngân sách'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('budget-edit')));
    await tester.pumpAndSettle();
    etag = '"partner"';
    await tester.enterText(
      find.byKey(const Key('budget-approvalThreshold')),
      '3000000',
    );
    await tester.tap(find.byKey(const Key('budget-save')));
    await tester.pumpAndSettle();
    expect(find.text('Ngân sách vừa được sửa'), findsOneWidget);
    expect(budget['approvalThreshold'], 1000000);
    await tester.tap(find.text('Tải bản mới'));
    await tester.pumpAndSettle();
    final field = tester.widget<TextField>(
      find.byKey(const Key('budget-approvalThreshold')),
    );
    expect(
      field.controller!.text,
      '1.000.000',
      reason: 'the latest budget is loaded again',
    );
  });

  testWidgets('a food label is read, checked, then rated and saved', (
    tester,
  ) async {
    // A tall phone, so the whole check form is built.
    tester.view.physicalSize = const Size(1080, 6000);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    await openMore(tester, 'more-food');
    await tester.tap(find.byKey(const Key('food-gallery')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('food-read')));
    await tester.pumpAndSettle();
    expect(
      find.text('AI không chắc số này, xem lại nhãn'),
      findsOneWidget,
      reason: 'salt was uncertain',
    );
    await tester.enterText(find.byKey(const Key('food-salt')), '1,2');
    await tester.ensureVisible(find.byKey(const Key('food-analyze')));
    await tester.tap(find.byKey(const Key('food-analyze')));
    await tester.pumpAndSettle();
    final sent = jsonDecode(
      server.requests
          .lastWhere((r) => r.options.path.endsWith('food/analyze'))
          .body,
    ) as Map<String, Object?>;
    expect(sent['save'], true);
    expect((sent['reading']! as Map)['salt'], 1.2);
    expect(find.text('Kết quả'), findsOneWidget);
    expect(find.text('Đường: Cao'), findsOneWidget);
    expect(find.text('Ngũ cốc chứa gluten'), findsOneWidget);
  });

  test('issue codes become Vietnamese, with amounts formatted', () {
    expect(
      translateIssue(
        'fund_needs_per_month_but_receives',
        'Fund {0} needs {1} per month but receives {2}',
        ['Chung', '10000000', '9000000'],
      ),
      'Quỹ Chung cần 10.000.000 ₫ mỗi tháng nhưng chỉ nhận 9.000.000 ₫.',
    );
    expect(translateIssue('something_new', 'Something new'), 'Something new');
  });

  test('idempotency keys are random UUIDs', () {
    final a = newIdempotencyKey();
    expect(a, hasLength(36));
    expect(a, isNot(newIdempotencyKey()));
  });
}
