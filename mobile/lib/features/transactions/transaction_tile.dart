import 'package:flutter/material.dart';

import '../../api/generated/soitho_api.dart';
import '../../shared/dates.dart';
import '../../shared/money.dart';
import '../../shared/widgets.dart';

bool isIncoming(Direction d) => d.toJson() == 'in';

class TransactionTile extends StatelessWidget {
  const TransactionTile(this.tx, {super.key, this.showDate = false});

  final Transaction tx;

  /// Show the date next to the time (lists not grouped by day).
  final bool showDate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final local = tx.occurredAtUtc.toLocal();
    final incoming = isIncoming(tx.direction);
    final when = showDate
        ? '${clock(local)} · ${local.day}/${local.month}'
        : clock(local);
    final content = tx.content.trim().isEmpty
        ? '(không có nội dung)'
        : tx.content.trim();
    return ListTile(
      key: ValueKey('tx-${tx.id}'),
      leading: CircleAvatar(
        backgroundColor: incoming
            ? theme.colorScheme.primaryContainer
            : theme.colorScheme.surfaceContainerHighest,
        child: Icon(
          incoming ? Icons.south_west : Icons.north_east,
          size: 20,
          color: incoming
              ? theme.colorScheme.onPrimaryContainer
              : theme.colorScheme.onSurfaceVariant,
        ),
      ),
      title: Text(content, maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        tx.balance == null ? when : '$when · Số dư ${formatVnd(tx.balance!)}',
      ),
      trailing: AmountText(tx.amount, incoming: incoming),
    );
  }
}
