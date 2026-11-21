import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:soitho/lock/lock_policy.dart';
import 'package:soitho/shared/money.dart';

void main() {
  setUpAll(() => initializeDateFormatting('vi_VN'));

  group('lock policy', () {
    const policy = LockPolicy();
    final t = DateTime(2026, 11, 21, 9);

    test('stays open after a short switch to another app', () {
      expect(
        policy.shouldLockOnResume(
          wentToBackgroundAt: t,
          now: t.add(const Duration(minutes: 4, seconds: 59)),
        ),
        isFalse,
      );
    });

    test('locks after 5 minutes in the background', () {
      expect(
        policy.shouldLockOnResume(
          wentToBackgroundAt: t,
          now: t.add(const Duration(minutes: 5)),
        ),
        isTrue,
      );
    });

    test('never locks without a background time', () {
      expect(
        policy.shouldLockOnResume(wentToBackgroundAt: null, now: t),
        isFalse,
      );
    });
  });

  group('money', () {
    test('full VND amounts use Vietnamese grouping', () {
      expect(formatVnd(1250000), '1.250.000 ₫');
      expect(formatVnd(-8000000), '-8.000.000 ₫');
      expect(formatVnd(0), '0 ₫');
    });

    test('short amounts', () {
      expect(formatVndShort(25000000), '25 tr');
      expect(formatVndShort(1250000), '1,3 tr');
      expect(formatVndShort(25000), '25 k');
      expect(formatVndShort(-1750000), '-1,8 tr');
      expect(formatVndShort(500), '500 ₫');
    });
  });
}
