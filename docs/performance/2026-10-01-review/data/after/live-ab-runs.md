# Live A/B runs

Back-to-back runs of one phase against two builds, or against a control, on the same machine in one
session. "original" is `HEAD` as the review found it; "fixed" is the working tree with every fix.
Release builds, isolated instances (own data directory, TEMP and hotkey). Produced by
`measure-app.ps1`; the raw per-run files stay in the scratch folder, so the numbers are copied here.

## Unreachable network shares on the orbit (`-Phase unreachable`)

One folder on `\\192.0.2.1` and one application on `\\198.51.100.7` (RFC 5737 documentation ranges, never
routed), plus the usual local items. The instance is started, left alone for 12 s, summoned once, and the
UI thread is pinged every 25 ms throughout; a ping still outstanding is counted (see the methodology).

| Build | Items | Start-up stall | First summon visible | UI stalls at first summon | Longest stall |
|---|---|---:|---:|---:|---:|
| original | unreachable | 0 ms | 3.1 ms | 1 | **21261 ms** |
| fixed | unreachable | 0 ms | 5.9 ms | 0 | 0 ms |
| fixed | unreachable | 0 ms | 14.7 ms | 0 | 0 ms |
| fixed | unreachable | 0 ms | 14.6 ms | 0 | 0 ms |
| control | no unreachable items (control) | 0 ms | 5.5 ms | 1 | 447 ms |
| control | no unreachable items (control) | 0 ms | 14.8 ms | 0 | 0 ms |
| control | no unreachable items (control) | 0 ms | 18.5 ms | 1 | 280 ms |
| fixed (final build) | unreachable | 0 ms | 14.4 ms | 0 | 0 ms |
| fixed (full live run) | unreachable | 0 ms | 16.8 ms | 1 | 316 ms |

The single non-zero stall on the fixed build (316 ms, in the full live run) and the two on the control (447 and
280 ms) are the cold first summon of a fresh process, which happens with no network item on the orbit at all
(PR-017). It is not the share.

## Orbit summoned and dismissed over an open Settings window (`-Phase activation`, 150 pairs)

Private bytes are a GC sawtooth in both builds. A 60-pair run on the fixed build had flagged +36 MB; the
150-pair A/B shows the same curve on the original, so that run had ended on a peak (methodology, rule 5).

| Run | Private MB, sampled about every 10 pairs | Handles (first -> last) | GDI (first -> last) |
|---|---|---|---|
| fixed | 117 128 134 136 136 140 154 149 122 123 123 125 126 125 126 126 | 1396 -> 1551 | 127 -> 113 |
| fixed | 117 128 134 136 136 139 153 146 123 123 123 123 125 126 125 126 | 1404 -> 1541 | 135 -> 109 |
| original | 118 127 128 126 127 136 138 134 123 123 124 125 126 124 126 126 | 1386 -> 1609 | 111 -> 115 |
| original | 118 126 128 126 127 137 138 130 125 124 125 126 124 127 126 128 | 1401 -> 1626 | 119 -> 109 |

Handles settle at 1 500-1 550 in both builds after the first sample; GDI and USER are flat.

