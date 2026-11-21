import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import 'app_lock.dart';

/// Puts the lock screen, or a plain cover in the app switcher, over the app
/// while the person is signed in.
class LockGate extends ConsumerStatefulWidget {
  const LockGate({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<LockGate> createState() => _LockGateState();
}

class _LockGateState extends ConsumerState<LockGate>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    ref.read(appLockProvider.notifier).onLifecycle(state);
  }

  @override
  Widget build(BuildContext context) {
    final signedIn = ref.watch(authControllerProvider) is SignedIn;
    final lock = ref.watch(appLockProvider);
    return Stack(
      children: [
        widget.child,
        if (signedIn && lock.locked) const Positioned.fill(child: LockScreen()),
        if (signedIn && lock.obscured && !lock.locked)
          const Positioned.fill(child: _Cover()),
      ],
    );
  }
}

class _Cover extends StatelessWidget {
  const _Cover();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return ColoredBox(
      color: scheme.surface,
      child: Center(
        child: Icon(Icons.lock_outline, size: 48, color: scheme.primary),
      ),
    );
  }
}

class LockScreen extends ConsumerStatefulWidget {
  const LockScreen({super.key});

  @override
  ConsumerState<LockScreen> createState() => _LockScreenState();
}

class _LockScreenState extends ConsumerState<LockScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _unlock());
  }

  void _unlock() => ref.read(appLockProvider.notifier).unlock();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: theme.colorScheme.surface,
      child: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.lock_outline,
                  size: 56,
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(height: 16),
                Text('Sói & Thỏ đang khóa', style: theme.textTheme.titleLarge),
                const SizedBox(height: 8),
                Text(
                  'Dùng Face ID, vân tay hoặc mã máy để mở.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium,
                ),
                const SizedBox(height: 24),
                FilledButton.icon(
                  onPressed: _unlock,
                  icon: const Icon(Icons.fingerprint),
                  label: const Text('Mở khóa'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
