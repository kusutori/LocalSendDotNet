# Protocol compatibility baseline

- Protocol: LocalSend v2.2
- Baseline inspected: `localsend/localsend` main, commit `dfb9eb1110eb9dc143098c4c5d62e26f24ac0b3e`
- Inspection date: 2026-08-10
- Public specification: <https://github.com/localsend/protocol>

## Automated coverage

- JSON wire shape and tolerant device type parsing
- Stable RSA identity and uppercase SHA-256 DER fingerprint
- Mutual TLS transfer between two in-process nodes
- Streaming file body, partial selection, safe destination paths and collision handling
- Concurrent uploads, abandoned-session expiry, aggregate progress and bounded session admission
- Hexadecimal/Base64 SHA-256 validation before atomic publication
- Periodic announcements, stale-device removal and trusted manual HTTPS probing

## Manual official-client matrix

- Official client and CLI discover each other.
- Send and receive one file, multiple files and a text item in both directions.
- Accept a subset of an offer.
- Cancel from either sender or receiver.
- Exercise missing, incorrect and correct PINs and the three-failure rate limit.
- Confirm a mismatched HTTPS fingerprint is rejected before request content is sent.

Protocol v3 will be added behind a new internal protocol adapter. Public `LocalSendNode` operations and version-neutral models must remain stable.
