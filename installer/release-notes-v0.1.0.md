# ClipForge v0.1.0 — first public build

A clipboard manager rebuilt for Windows 2026. Ditto's soul, modern bones.
Open-core, built in public — this is the MVP, iterating from here.

## What's in it

**Verified on real Windows 10/11 2026-10-10** (marked ✅ — nothing is marked working until it works):

- ✅ Clipboard capture — text, HTML, images (PNG), file lists — via `AddClipboardFormatListener`
- ✅ Local SQLite history (`%LOCALAPPDATA%/ClipForge/clips.db`), survives reboot
- ✅ **Privacy-by-default in the capture path:** secret auto-detection (API keys, Luhn-validated card numbers, OTP codes, private keys, `password:` assignments) is never stored; password-manager apps (1Password, Bitwarden, KeePass…) are excluded; unpinned history expires after 90 days — retention is NOT indefinite
- ✅ Trust engineering: delayed retry on locked clipboard (never silently miss a copy), duplicate suppression, self-paste suppression, explorer-restart tray recovery, zero nag dialogs
- ✅ Global hotkey `Ctrl+Shift+V` → command-palette overlay (WPF, dark, HiDPI-clean)
- ✅ Instant search as you type — Ditto-style substring filter, ranked by match quality
- ✅ `Enter` pastes the selected clip, `Esc`/click-away dismisses
- ✅ Pin clips (`Ctrl+P` or 📌) — pinned section on top, exempt from retention pruning

**Implemented, not yet seen running** (in the binary, untested live):

- RTF capture (stored as RTF + plain-text companion)
- Paste as plain text (`Ctrl+Enter`)
- Image thumbnails in the overlay (async, cached)
- Minimal settings (retention, max clips, excluded apps, rebindable hotkey) + autostart on login
- Tray + window + exe icon

## Install

Run `ClipForge-Setup-0.1.0.exe`. Requires the .NET 8 Desktop Runtime (x64) — the installer tells you if it's missing.

## Next

Search highlighting → multi-paste → case transforms → snippets → semantic search → OCR on images → opt-in cloud sync.

Full build log in the commit history — one feature per commit.
