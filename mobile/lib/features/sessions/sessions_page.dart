import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/soitho_api.dart';
import '../../app/providers.dart';
import '../../data/cached.dart';
import '../../shared/dates.dart';
import '../../shared/widgets.dart';

final sessionsProvider = FutureProvider.autoDispose<List<Session>>(
  (ref) => ref.read(apiProvider).listSessions(),
);

/// Where this account is signed in; any other device can be signed out.
class SessionsPage extends ConsumerWidget {
  const SessionsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(sessionsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Thiết bị đang đăng nhập')),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(sessionsProvider.future),
        child: switch (async) {
          AsyncValue(:final value?) => ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.symmetric(vertical: 8),
            children: [
              for (final s in value) _SessionTile(s),
              const Padding(
                padding: EdgeInsets.all(16),
                child: Text(
                  'Thấy thiết bị lạ thì đăng xuất nó và đổi mật khẩu trên trang web.',
                ),
              ),
            ],
          ),
          AsyncValue(:final error?) => MessageView.error(
            error,
            onRetry: () => ref.invalidate(sessionsProvider),
          ),
          _ => const Center(child: CircularProgressIndicator()),
        },
      ),
    );
  }
}

class _SessionTile extends ConsumerWidget {
  const _SessionTile(this.s);
  final Session s;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final app = s.kind.toJson() == 'app';
    final name = s.deviceName.trim().isEmpty
        ? (app ? 'Ứng dụng' : 'Trình duyệt')
        : s.deviceName;
    final seen = savedAtLabel(s.lastSeenUtc, DateTime.now());
    return ListTile(
      key: ValueKey('session-${s.id}'),
      leading: Icon(app ? Icons.smartphone : Icons.language),
      title: Text(name),
      subtitle: Text(
        s.isCurrent
            ? 'Máy này'
            : '${app ? 'Ứng dụng' : 'Trình duyệt'} · dùng lúc $seen',
      ),
      trailing: s.isCurrent
          ? null
          : TextButton(
              key: ValueKey('end-session-${s.id}'),
              onPressed: () => _end(context, ref, name),
              child: const Text('Đăng xuất'),
            ),
    );
  }

  Future<void> _end(BuildContext context, WidgetRef ref, String name) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Đăng xuất $name?'),
        content: const Text('Thiết bị đó sẽ phải đăng nhập lại bằng mật khẩu.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Hủy'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Đăng xuất'),
          ),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await ref.read(apiProvider).endSession(id: s.id);
    } on DioException catch (e) {
      if (e.response?.statusCode != 404 && context.mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(describeError(e))));
        return;
      }
    }
    ref.invalidate(sessionsProvider);
  }
}
