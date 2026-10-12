# ClipForge v0.2.1

A clipboard manager rebuilt for Windows 2026. Ditto's soul, modern bones.
Open-core, built in public — iterating from here.

## What changed since v0.2.0

A fix release. Several defects found after v0.2.0 shipped are fixed here —
including one fix that was finished before the v0.2.0 installer was built
but didn't make it into it. If you run v0.2.0, we recommend updating.

**Excluded apps now stay excluded.** In v0.2.0, apps on your exclusion list
worked for the session but were silently forgotten on restart. Exclusions
persist now.

**The hotkey survives a settings save.** Saving settings in v0.2.0 could
unregister the global hotkey until the app was restarted. Fixed.

**Escaped secrets no longer slip past the filter.** The secret scan now
decodes RTF hex and unicode escapes before scanning, so a secret written
with escaped characters (a `password:` assignment, say) can't be stored
verbatim anymore. Whitespace-only captures are also refused outright.

**Delete a single clip.** Press `Del` or right-click a clip in the overlay
to remove just that one. v0.2.0 could only forget clips by expiry.

**A failed paste can't empty your clipboard.** If staging a paste fails
after the clipboard was cleared, your previous clipboard content is
restored — and if staging never touched the clipboard, nothing is restored
over a newer copy (the clipboard sequence number decides). Paste data is
also prepared before the clipboard is cleared, not after.

**Capture falls through instead of dropping copies.** If a format handler
claims a copy but fails to capture it, the next handler is tried instead of
the copy being lost.

**An automated test suite is in place.** 154 xUnit tests cover secret
detection, the RTF scan path, empty-capture refusal, sensitive-app
exclusion, the SQLite store, search ranking, hotkey chord parsing, the
paste-restore decision, and the capture handler chain. CI runs the suite on
every push. The Windows-only UI layer is still verified manually — see
TESTING.md in the repo.

## What's in it

- ✅ Clipboard capture — text, HTML, RTF, images (PNG), file lists — via `AddClipboardFormatListener`
- ✅ Local SQLite history (`%LOCALAPPDATA%/ClipForge/clips.db`), survives reboot
- ✅ Secret auto-detection at the capture boundary (best-effort, see Known limitations); password-manager apps (1Password, Bitwarden, KeePass…) are excluded; unpinned history expires after 90 days — retention is NOT indefinite
- ✅ Trust engineering: delayed retry on locked clipboard (never silently miss a copy), duplicate suppression, self-paste suppression, explorer-restart tray recovery, zero nag dialogs
- ✅ Global hotkey `Ctrl+Shift+V` → command-palette overlay (WPF, dark, HiDPI-clean)
- ✅ Instant search as you type — substring filter over the 500 most recent clips, ranked by match quality
- ✅ `Enter` pastes the selected clip, `Esc`/click-away dismisses, `Del` removes the selected clip
- ✅ Pin clips (`Ctrl+P` or 📌) — pinned section on top, exempt from retention pruning

## Install

Run `ClipForge-Setup-0.2.1.exe`. Requires the .NET 8 Desktop Runtime (x64) — the installer tells you if it's missing.

## Known limitations

- Search covers the 500 most recent clips, matched against short previews and the source app name — not the full stored text. The database holds up to 5,000 unpinned clips.
- The SQLite database is not encrypted.
- The secret filter scans the first 8 KB of text; image contents are not scanned.
- HTML clips paste as plain text (their markup), not as rich HTML.
- Pruning runs at startup, so changing retention settings takes effect on the next launch.
- The installer hasn't been verified end-to-end on a clean machine.

Full build log in the commit history — one feature per commit.
