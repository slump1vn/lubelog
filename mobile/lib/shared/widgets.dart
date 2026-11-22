import 'package:flutter/material.dart';

import '../data/cached.dart';
import 'dates.dart';
import 'money.dart';

/// Money in green with "+", money out with "−", in the theme's colours.
class AmountText extends StatelessWidget {
  const AmountText(
    this.amount, {
    super.key,
    required this.incoming,
    this.style,
  });

  final int amount;
  final bool incoming;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    final base = style ?? Theme.of(context).textTheme.titleSmall;
    final color = incoming
        ? (Theme.of(context).brightness == Brightness.dark
              ? const Color(0xFF6FD58E)
              : const Color(0xFF1B873F))
        : Theme.of(context).colorScheme.onSurface;
    return Text(
      '${incoming ? '+' : '−'}${formatVnd(amount)}',
      style: base?.copyWith(
        color: color,
        fontFeatures: const [FontFeature.tabularFigures()],
      ),
    );
  }
}

/// A thin note at the top when the numbers are older than they look.
class OfflineNote extends StatelessWidget {
  const OfflineNote({super.key, required this.savedAt, required this.now});

  final DateTime savedAt;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      key: const Key('offline-note'),
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      color: scheme.secondaryContainer,
      child: Row(
        children: [
          Icon(Icons.cloud_off, size: 18, color: scheme.onSecondaryContainer),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              'Chưa tải được số liệu mới. Đang hiện số liệu lúc ${savedAtLabel(savedAt, now)}.',
              style: TextStyle(color: scheme.onSecondaryContainer),
            ),
          ),
        ],
      ),
    );
  }
}

/// Full-screen message with a retry button, used when there is nothing to show.
class MessageView extends StatelessWidget {
  const MessageView({
    super.key,
    required this.icon,
    required this.message,
    this.onRetry,
  });

  MessageView.error(Object error, {Key? key, VoidCallback? onRetry})
    : this(
        key: key,
        icon: Icons.error_outline,
        message: describeError(error),
        onRetry: onRetry,
      );

  final IconData icon;
  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListView(
      // Scrollable so pull-to-refresh works on it too.
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(32),
      children: [
        const SizedBox(height: 64),
        Icon(icon, size: 48, color: theme.colorScheme.onSurfaceVariant),
        const SizedBox(height: 16),
        Text(
          message,
          textAlign: TextAlign.center,
          style: theme.textTheme.bodyLarge,
        ),
        if (onRetry != null) ...[
          const SizedBox(height: 16),
          Center(
            child: OutlinedButton(
              onPressed: onRetry,
              child: const Text('Thử lại'),
            ),
          ),
        ],
      ],
    );
  }
}

class SectionTitle extends StatelessWidget {
  const SectionTitle(this.text, {super.key, this.trailing});

  final String text;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 24, 4, 8),
      child: Row(
        children: [
          Expanded(
            child: Text(text, style: Theme.of(context).textTheme.titleMedium),
          ),
          ?trailing,
        ],
      ),
    );
  }
}
