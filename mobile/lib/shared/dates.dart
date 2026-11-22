import 'package:intl/intl.dart';

/// Server dates are "yyyy-MM-dd" Vietnam calendar dates.
DateTime parseDay(String isoDate) {
  final parts = isoDate.split('-').map(int.parse).toList();
  return DateTime(parts[0], parts[1], parts[2]);
}

/// 2026-11-05 -> "5/11".
String shortDay(String isoDate) {
  final d = parseDay(isoDate);
  return '${d.day}/${d.month}';
}

DateTime _dayOnly(DateTime t) => DateTime(t.year, t.month, t.day);

/// Header for a group of transactions: "Hôm nay", "Hôm qua", "Thứ Năm, 19/11",
/// with the year when it is not this year.
String dayHeader(DateTime local, DateTime now) {
  final day = _dayOnly(local);
  final today = _dayOnly(now);
  final diff = today.difference(day).inDays;
  if (diff == 0) return 'Hôm nay';
  if (diff == 1) return 'Hôm qua';
  final weekday = toBeginningOfSentenceCase(
    DateFormat.EEEE('vi_VN').format(local),
  );
  final date = local.year == now.year
      ? DateFormat('d/M', 'vi_VN').format(local)
      : DateFormat('d/M/y', 'vi_VN').format(local);
  return '$weekday, $date';
}

/// "14:05".
String clock(DateTime local) => DateFormat.Hm('vi_VN').format(local);

/// When data was fetched: "14:05" today, otherwise "14:05 19/11".
String savedAtLabel(DateTime savedAt, DateTime now) {
  final local = savedAt.toLocal();
  if (_dayOnly(local) == _dayOnly(now)) return clock(local);
  return '${clock(local)} ${DateFormat('d/M', 'vi_VN').format(local)}';
}

/// The Vietnam calendar date (UTC+7) of a UTC time, as "yyyy-MM-dd": the
/// server's dates are Vietnam dates whatever the phone's time zone.
String vietnamDay(DateTime utc) {
  final vn = utc.toUtc().add(const Duration(hours: 7));
  return '${vn.year.toString().padLeft(4, '0')}-'
      '${vn.month.toString().padLeft(2, '0')}-'
      '${vn.day.toString().padLeft(2, '0')}';
}

/// "yyyy-MM-dd" of a calendar day picked on the phone.
String isoDay(DateTime day) =>
    '${day.year.toString().padLeft(4, '0')}-'
    '${day.month.toString().padLeft(2, '0')}-'
    '${day.day.toString().padLeft(2, '0')}';

/// "14:05 19/11" in the phone's time, for periods that span days.
String shortMoment(DateTime utc) {
  final local = utc.toLocal();
  return '${clock(local)} ${local.day}/${local.month}';
}
