import 'package:dio/dio.dart';

import '../auth/token_manager.dart';

/// A screen's data and where it came from.
class Cached<T> {
  const Cached(this.value, {required this.savedAt, this.offline = false});

  final T value;

  /// When the server sent it.
  final DateTime savedAt;

  /// True when the last try to refresh failed and [value] is older data.
  final bool offline;

  Cached<T> markOffline() => Cached(value, savedAt: savedAt, offline: true);
}

/// "No budget yet" and similar answers the screen shows as content.
class NotSetUp implements Exception {
  const NotSetUp();
}

/// A short Vietnamese sentence for an error, for the screen.
String describeError(Object error) {
  if (error is NotSetUp) return 'Chưa có dữ liệu.';
  if (error is DioException) {
    if (error.error is SignedOutException) {
      return 'Phiên đăng nhập đã hết hạn.';
    }
    final status = error.response?.statusCode;
    if (status == null) {
      return 'Không kết nối được máy chủ. Kiểm tra mạng rồi kéo xuống để thử lại.';
    }
    if (status >= 500) {
      return 'Máy chủ đang gặp lỗi ($status). Thử lại sau ít phút.';
    }
    return 'Không tải được dữ liệu ($status).';
  }
  return 'Không tải được dữ liệu.';
}
