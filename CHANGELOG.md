# Changelog

## 0.2.0-preview.2

- Recover IPv4 multicast discovery after network-interface changes and socket failures.
- Make manual refresh force discovery-socket recovery for resume scenarios.
- Publish certificate identity through a cross-process lock and temporary files; reject incomplete or corrupt identities with an actionable exception.
- Report occupied TCP ports and unavailable discovery interfaces with dedicated public exceptions.
- Allow startup retry after a corrected startup failure.
- Require XML documentation for the complete public API and enforce an approved API-surface baseline.
- Add a package-only consumer sample and cross-platform CI smoke build.
- Use source-generated protocol JSON metadata so Native AOT applications can consume the core library.

## 0.2.0-preview.1

- Add lifecycle state and persistent local identity APIs for application hosts.
- Add stale-device removal, periodic announcements, trusted manual endpoint probing and removal.
- Add explicit transfer cancellation, bounded cancellation requests and recognizable failure codes.
- Add incoming decision, session, request-body, item, byte and concurrent-upload safeguards.
- Add SHA-256 generation/verification, aggregate receive progress and source-length change detection.
- Add stream-backed items, directory enumeration, relative paths and MIME type inference.
- Reject linked-directory traversal and normalize IPv4-mapped endpoints.
- Add concurrent upload, timeout, busy receiver, checksum and cancellation integration tests.
