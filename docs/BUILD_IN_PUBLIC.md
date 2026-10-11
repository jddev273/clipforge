# Building ClipForge in Public

ClipForge is being built in the open by AI agents, one demo-able iteration at a time.
Every row below shipped as working software with a shareable demo beat — no vapor.

## The mission

Ditto has owned this space for years but looks like 2008. We're rebuilding it
for 2026: **free, fast, beautiful, privacy-by-default** — Ditto's engine, Snipboard's
looks, and a 2026 intelligence layer (semantic search, OCR) we haven't seen on Windows.

Principles: local-first (SQLite you own, zero telemetry), trust engineering (never
miss a copy, never nag), opinionated defaults (no 100-setting options maze).

## Build log

| Date | Version | What shipped | Demo beat |
|---|---|---|---|
| 2026-10-10 | v0.1.0 | MVP: capture (text/HTML/images/files) → SQLite → `Ctrl+Shift+V` command-palette overlay → instant fuzzy search → `Enter` pastes. Privacy-by-default in the capture path: secret auto-detection refuses common patterns at capture (best-effort), password-manager apps excluded, 90-day retention. Trust: locked-clipboard retry, duplicate + self-paste suppression, tray recovery. | "The 20-second clip": copy 5 things → hotkey → gorgeous panel → paste. |
| 2026-10-10 | v0.2.0 | Trust fixes: paste now snapshots the richest clipboard format and uses the clipboard sequence number so a copy made mid-paste is never clobbered (your newer copy always wins); restores bring back the full format instead of downgrading to text. Privacy wording made honest (best-effort filter, first 8KB scanned, not a guarantee); RTF-without-text-companion gap closed — RTF text runs are scanned for secrets too. | Paste 5 things in a row without losing your clipboard. |

## Up next (iteration order)

1. Instant search + **match highlighting**
2. Pinning + multi-paste (overlay stays open across pastes)
3. Paste-as-plain-text + case transforms (UPPER/lower/Capitalize)
4. Snippets with `{{date}}` / `{{time}}` / `{{clipboard}}` variables
5. Privacy-by-default demo — *"watch it refuse to save your password"*
6. Semantic search — *"find that URL about the plumber"*
7. OCR on images — search text inside screenshots
8. Polish, installer, docs, launch

## Deliberately cut (v1)

No cloud accounts/sync service, no portable mode, no mobile app, no enterprise/team
features, no options maze, no Ditto bloat tail (email export, QR view, odometer…),
no AI chatbot. Sync is the v2 paid wedge — the v1 story is local-first.
Note 2026-10-10 (Johann): LAN clipboard sharing is the interesting v2 shape —
peer-to-peer, no server costs, no account. Where we'd differ from Snipboard:
documented E2E encryption + the secret filter applied *before* anything leaves
the machine, vs their undocumented LAN encryption (no public docs on encryption,
pairing, or discovery as of Oct 2026 — WindowsForum) and indefinite-retention
*default* (XDA hands-on: beta defaults to indefinite retention under a 1GB cap,
user-changeable).
