import 'package:intl/intl.dart';

final _vnd = NumberFormat.decimalPattern('vi_VN');

/// 1250000 -> "1.250.000 ₫"; negative amounts keep their minus sign.
String formatVnd(num amount) => '${_vnd.format(amount.round())} ₫';

/// Short form for tight places: 1250000 -> "1,3 tr", 25000 -> "25 k".
String formatVndShort(num amount) {
  final sign = amount < 0 ? '-' : '';
  final value = amount.abs();
  if (value >= 1000000000) {
    return '$sign${NumberFormat('#,##0.#', 'vi_VN').format(value / 1000000000)} tỷ';
  }
  if (value >= 1000000) {
    return '$sign${NumberFormat('#,##0.#', 'vi_VN').format(value / 1000000)} tr';
  }
  if (value >= 1000) {
    return '$sign${NumberFormat('#,##0', 'vi_VN').format(value / 1000)} k';
  }
  return '$sign${value.round()} ₫';
}
