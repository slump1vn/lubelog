/// When the app must ask for Face ID / fingerprint / device code again.
///
/// Locked at every cold start, and after [timeout] in the background.
class LockPolicy {
  const LockPolicy({this.timeout = const Duration(minutes: 5)});

  final Duration timeout;

  bool shouldLockOnResume({
    required DateTime? wentToBackgroundAt,
    required DateTime now,
  }) {
    if (wentToBackgroundAt == null) return false;
    return now.difference(wentToBackgroundAt) >= timeout;
  }
}
