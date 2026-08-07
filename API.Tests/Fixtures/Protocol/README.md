# Sanitized BZ98R protocol fixtures

These files are small, synthetic regression fixtures for the Battlezone 98 Redux lobby protocol as currently understood by Game Watcher. They are executable protocol documentation, not recordings of real users or raw lobby-server captures.

## What is represented

The corpus covers representative game and chat lobbies, Steam/GOG/Web authentication, waiting/launched/unknown state, password-marker state, stock and Workshop mod identifiers, full and truncated `gameSettings`, owner snapshots, malformed optional metadata, and upstream user network fields used only to prove the public response mapper does not expose them.

Known `gameSettings` indexes are:

| Index | Meaning |
| ---: | --- |
| 0 | metadata version |
| 1 | map filename |
| 2 | CRC32 |
| 3 | mod / Workshop ID |
| 4 | sync join |
| 5 | satellite |
| 6 | barracks |
| 7 | time limit |
| 8 | lives |
| 9 | player limit |
| 10 | sniper |
| 11 | kill limit |
| 12 | splinter |

Missing, empty, malformed, and still-unidentified values must remain unknown/null rather than being guessed.

`authType` is authoritative for platform classification. In particular, `B1000002` with `authType = web` is Web, not GOG. Identifier shape is used only for platform-specific enrichment after authentication type is known.

The lobby metadata `gameType` field is currently understood as a validity/broken-state marker (`0` broken/invalid, `1` valid), not `0 = MPI` / `1 = Strategy`. Actual multiplayer mode comes from optional map enrichment when available.

`launched` remains tri-state: `1` means in progress, `0` means still in lobby, and missing/unknown remains not reported. Explicit `gameended` values are preserved without manufacturing a value when absent.

## Sanitization and privacy

No real network identity may be committed here. Fixtures should omit network-address fields unless their structural presence is required by a privacy regression test.

When an address-shaped field is structurally necessary, use only documentation ranges:

- IPv4: `192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24` (RFC 5737)
- IPv6: `2001:db8::/32` (RFC 3849)

Authentication secrets, tokens, passwords, real WAN/LAN/IP addresses, and other identifying network data are prohibited. The fixture-safety test fails CI for unexpected IP literals or suspicious network-address property values.

The public API must continue to expose only the password-protected boolean state and must continue to omit upstream IP, WAN, and LAN values entirely.

**Never commit a raw lobby-server capture without sanitizing network identifiers first.** Prefer constructing the smallest synthetic fixture needed for the protocol behavior under test.

## Sources and intent

Field semantics come from the repository's reverse-engineering work summarized in `docs/protocol-audit-2026-08.md`, cross-checks against community tooling, and observed protocol behavior already encoded in production parsing/mapping logic.

These fixtures serve three purposes:

1. prevent Game Watcher protocol regressions;
2. document reverse-engineered BZ98R behavior as executable examples; and
3. provide sanitized protocol inputs for later drift-detection and matchmaking-server/Shim research.

Adding a fixture does not establish new protocol semantics by itself. Unknown fields should remain opaque until independently identified.
