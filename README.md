# ClipForge

**Ditto won every clipboard-manager shootout but looks like 2008. ClipForge is the rebuild for 2026 — free, fast, beautiful, built in public by AI agents.**

ClipForge is a native Windows clipboard manager: everything you copy (text, images, files) lands in a local SQLite history; `Ctrl+Shift+V` summons a command-palette overlay; fuzzy search finds anything instantly; `Enter` pastes it. No account, no cloud, no telemetry — your clips never leave your machine.

## Status: v0.1.0 MVP (2026-10-10)

Verified on real Windows 10/11 2026-10-10 are marked ✅ — the rest compiles
clean (0 warnings, strict analyzer pack) and is implemented, but hasn't been
seen running yet. Nothing is marked working until it works.

- [x] ✅ Clipboard capture — text, HTML, images (PNG), file lists — via `AddClipboardFormatListener`
- [x] ✅ Local SQLite history (`%LOCALAPPDATA%/ClipForge/clips.db`), survives reboot
- [x] ✅ **Privacy-by-default in the capture path:** secret auto-detection (API keys, Luhn-validated card numbers, OTP codes, private keys, `password:` assignments) is never stored; password-manager apps (1Password, Bitwarden, KeePass…) are excluded; unpinned history expires after 90 days — retention is NOT indefinite
- [x] ✅ Trust engineering: delayed retry on locked clipboard (never silently miss a copy), duplicate suppression, self-paste suppression, explorer-restart tray recovery, zero nag dialogs
- [x] Global hotkey `Ctrl+Shift+V` → command-palette overlay (WPF, dark, HiDPI-clean) — implemented, not yet seen running
- [x] Instant search as you type — Ditto-style substring filter, ranked by match quality (position, word-boundary, exact case) — implemented, not yet seen running
- [x] `Enter` pastes the selected clip, `Esc`/click-away dismisses — implemented, not yet seen running

**Not yet (iteration order):** search highlighting → pinning + multi-paste → paste-as-plain-text + case transforms → snippets with variables → privacy demo beat → semantic search → OCR on images → installer/polish. See [docs/BUILD_IN_PUBLIC.md](docs/BUILD_IN_PUBLIC.md).

**Deliberately cut (v1):** cloud sync/accounts, portable mode, mobile, enterprise/team features, options maze, Ditto's bloat tail (email export, QR view, odometer…), AI chatbot.

## Run it

**Requirements:** Windows 10/11 x64, .NET 8 Desktop Runtime.

1. Grab the latest publish from [Releases](https://github.com/jddev273/clipforge/releases) (framework-dependent — needs the .NET 8 Desktop Runtime, already listed above), or build from source below.
2. Run it — it lives in the tray. Copy anything.
3. Press `Ctrl+Shift+V` → type to filter → `Enter` to paste.

## Build from source

```powershell
# Windows, .NET 8 SDK
dotnet build ClipForge.sln -c Release
# Fast publish profile: framework-dependent + ReadyToRun (~3MB, ~5s cold start)
dotnet publish src/ClipForge.App/ClipForge.App.csproj -c Release /p:PublishProfile=Fast
```

The solution also compiles on Linux/macOS (`EnableWindowsTargeting`) for CI — it just can't *run* there.

## Project layout

```
src/
  ClipForge.Core/          # no UI deps: model, SQLite store, fuzzy search,
                           #   secret detector, Win32 clipboard listener (P/Invoke)
  ClipForge.App/           # WPF: overlay, global hotkey, paste, tray icon
docs/
  BUILD_IN_PUBLIC.md       # the build log — every iteration is a public beat
```

The `ClipForge.Core` ↔ UI seam is deliberately clean (`IClipStore`, `ClipboardListener`, `FuzzyMatcher`) — search, pinning UI, snippets, semantic search and OCR all build on top of it.

## Privacy

Local-first, always: one SQLite file you own, no network calls, no telemetry, no account. Secrets are refused at the capture boundary — see `SecretDetector.cs`.

## License

MIT — see [LICENSE](LICENSE). Built in public; PRs welcome.
