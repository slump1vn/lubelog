import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

class PickedPhoto {
  const PickedPhoto(this.bytes, this.name);
  final Uint8List bytes;
  final String name;
}

enum PhotoFrom { camera, gallery }

/// Photos from the camera or the gallery, made smaller on the phone first:
/// the home AI reads a 2000-pixel photo as well as a 12-megapixel one, and it
/// uploads in a second instead of ten.
abstract class PhotoPicker {
  Future<PickedPhoto?> pickOne(PhotoFrom from);
  Future<List<PickedPhoto>> pickMany(int max);
}

class ImagePickerPhotos implements PhotoPicker {
  final _picker = ImagePicker();
  static const _maxSide = 2000.0;
  static const _quality = 85;

  Future<PickedPhoto> _read(XFile f) async =>
      PickedPhoto(await f.readAsBytes(), f.name);

  @override
  Future<PickedPhoto?> pickOne(PhotoFrom from) async {
    final f = await _picker.pickImage(
      source: from == PhotoFrom.camera
          ? ImageSource.camera
          : ImageSource.gallery,
      maxWidth: _maxSide,
      maxHeight: _maxSide,
      imageQuality: _quality,
      requestFullMetadata: false,
    );
    return f == null ? null : _read(f);
  }

  @override
  Future<List<PickedPhoto>> pickMany(int max) async {
    final files = await _picker.pickMultiImage(
      maxWidth: _maxSide,
      maxHeight: _maxSide,
      imageQuality: _quality,
      limit: max,
      requestFullMetadata: false,
    );
    return [for (final f in files.take(max)) await _read(f)];
  }
}

final photoPickerProvider = Provider<PhotoPicker>((ref) => ImagePickerPhotos());
