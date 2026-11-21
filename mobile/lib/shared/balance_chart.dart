import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'money.dart';

/// Balance per day as a line, with the lowest and highest values labelled.
/// Drawn by hand: one series, no interaction needed, no chart package.
class BalanceChart extends StatelessWidget {
  const BalanceChart({super.key, required this.points, this.height = 120});

  /// (day, balance), oldest first.
  final List<(DateTime, int)> points;
  final double height;

  @override
  Widget build(BuildContext context) {
    if (points.length < 2) return const SizedBox.shrink();
    final scheme = Theme.of(context).colorScheme;
    final values = points.map((p) => p.$2);
    final low = values.reduce(math.min);
    final high = values.reduce(math.max);
    final label = Theme.of(context).textTheme.labelSmall
        ?.copyWith(color: scheme.onSurfaceVariant);
    return Semantics(
      label:
          'Số dư theo ngày, thấp nhất ${formatVnd(low)}, cao nhất ${formatVnd(high)}',
      child: SizedBox(
        height: height,
        child: Row(
          children: [
            SizedBox(
              height: height,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(formatVndShort(high), style: label),
                  Text(formatVndShort(low), style: label),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: SizedBox(
                height: height,
                child: CustomPaint(
                  painter: _LinePainter(
                    values: values.toList(),
                    line: scheme.primary,
                    fill: scheme.primary.withValues(alpha: 0.12),
                    grid: scheme.outlineVariant,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _LinePainter extends CustomPainter {
  _LinePainter({
    required this.values,
    required this.line,
    required this.fill,
    required this.grid,
  });

  final List<int> values;
  final Color line;
  final Color fill;
  final Color grid;

  @override
  void paint(Canvas canvas, Size size) {
    final low = values.reduce(math.min).toDouble();
    final high = values.reduce(math.max).toDouble();
    final span = high - low == 0 ? 1.0 : high - low;
    Offset at(int i) => Offset(
      size.width * i / (values.length - 1),
      size.height - 4 - (values[i] - low) / span * (size.height - 8),
    );
    final gridPaint = Paint()
      ..color = grid
      ..strokeWidth = 1;
    canvas.drawLine(Offset(0, 4), Offset(size.width, 4), gridPaint);
    canvas.drawLine(
      Offset(0, size.height - 4),
      Offset(size.width, size.height - 4),
      gridPaint,
    );
    final path = Path()..moveTo(at(0).dx, at(0).dy);
    for (var i = 1; i < values.length; i++) {
      path.lineTo(at(i).dx, at(i).dy);
    }
    final area = Path.from(path)
      ..lineTo(size.width, size.height)
      ..lineTo(0, size.height)
      ..close();
    canvas.drawPath(area, Paint()..color = fill);
    canvas.drawPath(
      path,
      Paint()
        ..color = line
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..strokeJoin = StrokeJoin.round,
    );
    canvas.drawCircle(at(values.length - 1), 3.5, Paint()..color = line);
  }

  @override
  bool shouldRepaint(_LinePainter old) =>
      old.values != values || old.line != line || old.fill != fill;
}
