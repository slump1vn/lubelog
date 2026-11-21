import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

typedef FakeResponse = ({int status, Object? body});
typedef Handler = FakeResponse Function(RequestOptions request, String body);

/// A Dio adapter that answers from Dart code, and records every request.
class FakeServer implements HttpClientAdapter {
  FakeServer(this.handler);

  Handler handler;
  final requests = <({RequestOptions options, String body})>[];
  Duration delay = Duration.zero;

  /// Extra response headers, e.g. an ETag.
  Map<String, List<String>> Function(RequestOptions request)? headers;

  List<String> get paths => [for (final r in requests) r.options.uri.path];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final bytes = <int>[];
    if (requestStream != null) {
      await for (final chunk in requestStream) {
        bytes.addAll(chunk);
      }
    }
    // Photos are binary: keep the text parts readable, ignore the rest.
    final body = utf8.decode(bytes, allowMalformed: true);
    requests.add((options: options, body: body));
    if (delay > Duration.zero) await Future<void>.delayed(delay);
    final answer = handler(options, body);
    return ResponseBody.fromString(
      answer.body == null ? '' : jsonEncode(answer.body),
      answer.status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
        ...?headers?.call(options),
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Map<String, String> form(String body) => Uri.splitQueryString(body);
