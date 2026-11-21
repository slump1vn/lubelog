import 'dart:math';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/generated/soitho_api.dart';
import '../app/providers.dart';

/// A budget and the tag to send back when saving it.
class EditableBudget {
  const EditableBudget(this.budget, this.etag);
  final Budget budget;
  final String etag;
}

/// The partner saved the budget while it was being edited.
class BudgetChanged implements Exception {
  const BudgetChanged();
}

/// Result of explaining a gap: the entry, and whether an earlier try had
/// already stored it.
class ManualEntryResult {
  const ManualEntryResult(this.entry, {required this.replayed});
  final Transaction entry;
  final bool replayed;
}

/// A random UUID v4 for the Idempotency-Key header.
String newIdempotencyKey([Random? random]) {
  final r = random ?? Random.secure();
  final b = List<int>.generate(16, (_) => r.nextInt(256));
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  final h = b.map((x) => x.toRadixString(16).padLeft(2, '0')).join();
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-'
      '${h.substring(16, 20)}-${h.substring(20)}';
}

/// The writes that need HTTP details the generated client hides: the budget
/// ETag, and 201 versus 200 for a manual entry.
class FinanceWrites {
  FinanceWrites(this._dio);
  final Dio _dio;

  /// The budget as it is on the server now, to edit.
  Future<EditableBudget> loadBudget() async {
    final r = await _dio.get<Map<String, Object?>>('/api/v1/finance/budget');
    final etag = r.headers.value('etag');
    if (etag == null) {
      throw StateError('The server sent the budget without an ETag.');
    }
    return EditableBudget(Budget.fromJson(r.data!), etag);
  }

  /// Saves the budget changed from the one tagged [etag]. Throws
  /// [BudgetChanged] when the partner saved in between.
  Future<EditableBudget> saveBudget(Budget budget, String etag) async {
    try {
      final r = await _dio.put<Map<String, Object?>>(
        '/api/v1/finance/budget',
        data: budget.toJson(),
        options: Options(headers: {'If-Match': etag}),
      );
      return EditableBudget(
        Budget.fromJson(r.data!),
        r.headers.value('etag') ?? etag,
      );
    } on DioException catch (e) {
      if (e.response?.statusCode == 412) throw const BudgetChanged();
      rethrow;
    }
  }

  /// Explains (part of) a gap. Use the same [idempotencyKey] when retrying
  /// the same entry, so it is never stored twice.
  Future<ManualEntryResult> addManual(
    ManualTransactionRequest request, {
    required String idempotencyKey,
  }) async {
    final r = await _dio.post<Map<String, Object?>>(
      '/api/v1/finance/manual-transactions',
      data: request.toJson(),
      options: Options(headers: {'Idempotency-Key': idempotencyKey}),
    );
    return ManualEntryResult(
      Transaction.fromJson(r.data!),
      replayed: r.statusCode == 200,
    );
  }
}

final financeWritesProvider = Provider<FinanceWrites>(
  (ref) => FinanceWrites(ref.watch(dioProvider)),
);
