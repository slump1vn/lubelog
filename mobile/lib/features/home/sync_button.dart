import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/providers.dart';
import '../../data/cached.dart';
import '../../data/reconciliation_providers.dart';
import '../../shared/issues.dart';

/// Asks SePay for new transactions now, instead of waiting for the next
/// 15-minute pull.
class SyncButton extends ConsumerStatefulWidget {
  const SyncButton({super.key});

  @override
  ConsumerState<SyncButton> createState() => _SyncButtonState();
}

class _SyncButtonState extends ConsumerState<SyncButton> {
  bool _busy = false;

  Future<void> _sync() async {
    setState(() => _busy = true);
    String message;
    try {
      final r = await ref.read(apiProvider).syncAccount();
      if (r.succeeded) {
        message = r.recent
            ? 'Vừa đồng bộ xong, chưa có gì mới.'
            : r.added == 0
            ? 'Đã đồng bộ, không có giao dịch mới.'
            : 'Đã đồng bộ, có ${r.added} giao dịch mới.';
        refreshAccountViewsFrom(ref);
      } else {
        message = translateIssue(r.error ?? '', 'Đồng bộ chưa được.');
      }
    } on DioException catch (e) {
      message = describeError(e);
    }
    if (!mounted) return;
    setState(() => _busy = false);
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    return IconButton(
      key: const Key('sync-now'),
      tooltip: 'Đồng bộ ngay',
      onPressed: _busy ? null : _sync,
      icon: _busy
          ? const SizedBox(
              width: 20,
              height: 20,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(Icons.sync),
    );
  }
}
