# ClipForge v0.2.0

A clipboard manager rebuilt for Windows 2026. Ditto's soul, modern bones.
Open-core, built in public — iterating from here.

## What changed since v0.1.0

This release fixes two trust issues found in review of v0.1.0. If you installed
v0.1.0, we recommend updating.

**Paste no longer risks your clipboard.** Pasting a clip used to snapshot only
clipboard text and restore it afterwards. If you copied something new in the
middle of a paste, the restore could overwrite it; if the previous clipboard
held an image or files, the restore downgraded it to text. Now ClipForge
snapshots the richest clipboard format and checks the Windows clipboard
sequence number before restoring — if you copied something mid-paste, your
newer copy always wins and nothing is restored over it. Restores bring back
the full format, not just text.

**Privacy wording is honest now.** The secret filter refuses common secret
patterns at the capture boundary (API keys, Luhn-validated card numbers, OTP
codes, private keys, `password:` assignments) — but it is a best-effort
filter, not a guarantee: it scans the first 8KB of text. The v0.1.0 notes
overstated this; they didn't. Also fixed: RTF clips captured without a
plain-text companion previously skipped secret scanning entirely — their text
runs are scanned now.

## What's in it

- ✅ Clipboard capture — text, HTML, RTF, images (PNG), file lists — via `AddClipboardFormatListener`
- ✅ Local SQLite history (`%LOCALAPPDATA%/ClipForge/clips.db`), survives reboot
- ✅ Secret auto-detection at the capture boundary (best-effort, see above); password-manager apps (1Password, Bitwarden, KeePass…) are excluded; unpinned history expires after 90 days — retention is NOT indefinite
- ✅ Trust engineering: delayed retry on locked clipboard (never silently miss a copy), duplicate suppression, self-paste suppression, explorer-restart tray recovery, zero nag dialogs
- ✅ Global hotkey `Ctrl+Shift+V` → command-palette overlay (WPF, dark, HiDPI-clean)
- ✅ Instant search as you type — Ditto-style substring filter, ranked by match quality
- ✅ `Enter` pastes the selected clip, `Esc`/click-away dismisses
- ✅ Pin clips (`Ctrl+P` or 📌) — pinned section on top, exempt from retention pruning

## Install

Run `ClipForge-Setup-0.2.0.exe`. Requires the .NET 8 Desktop Runtime (x64) — the installer tells you if it's missing.

## Known limitations

- No automated test suite yet; the installer hasn't been verified end-to-end on a clean machine.
- Search covers the 500 most recent clips in the overlay (database holds up to 5,000 unpinned).
- The SQLite database is not encrypted.

Full build log in the commit history — one feature per commit.
