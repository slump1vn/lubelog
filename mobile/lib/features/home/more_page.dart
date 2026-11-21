import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/providers.dart';
import '../../auth/auth_controller.dart';

class MorePage extends ConsumerWidget {
  const MorePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(authControllerProvider);
    final me = state is SignedIn ? state.me : null;
    final server = ref.watch(configProvider).server.host;
    return Scaffold(
      appBar: AppBar(title: const Text('Thêm')),
      body: ListView(
        children: [
          ListTile(
            leading: const Icon(Icons.person_outline),
            title: Text(me?.userName ?? 'Đang ngoại tuyến'),
            subtitle: Text(server),
          ),
          const Divider(),
          ListTile(
            key: const Key('more-reconciliation'),
            leading: const Icon(Icons.fact_check_outlined),
            title: const Text('Đối soát'),
            subtitle: const Text(
              'Ghi rõ các khoản ngân hàng không báo, chụp hóa đơn',
            ),
            onTap: () => context.go('/more/reconciliation'),
          ),
          ListTile(
            key: const Key('more-food'),
            leading: const Icon(Icons.qr_code_scanner),
            title: const Text('Nhãn thực phẩm'),
            subtitle: const Text(
              'Chụp nhãn, xem đường, muối, chất béo và phụ gia',
            ),
            onTap: () => context.go('/more/food'),
          ),
          ListTile(
            key: const Key('more-sessions'),
            leading: const Icon(Icons.devices),
            title: const Text('Thiết bị đang đăng nhập'),
            onTap: () => context.go('/more/sessions'),
          ),
          const Divider(),
          ListTile(
            key: const Key('sign-out'),
            leading: const Icon(Icons.logout),
            title: const Text('Đăng xuất'),
            subtitle: const Text('Xóa phiên đăng nhập trên máy này'),
            onTap: () async {
              final ok = await showDialog<bool>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const Text('Đăng xuất?'),
                  content: const Text(
                    'Lần sau mở app sẽ cần đăng nhập lại bằng mật khẩu.',
                  ),
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
              if (ok == true) {
                await ref.read(authControllerProvider.notifier).signOut();
              }
            },
          ),
        ],
      ),
    );
  }
}
