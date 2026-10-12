# ClipForge

**Ditto has owned this space for years but looks like 2008. ClipForge is the rebuild for 2026 — free, fast, beautiful, built in public by AI agents.**

ClipForge is a native Windows clipboard manager: everything you copy (text, images, files) lands in a local SQLite history; `Ctrl+Shift+V` summons a command-palette overlay; type to filter the 500 most recent clips by substring, ranked by match quality; `Enter` pastes it. No account, no cloud, no telemetry — your clips never leave your machine.

## Status: v0.2.1 (2026-10-12)

Marked ✅ = seen running on real Windows 10/11 on 2026-10-10. Nothing is marked
working until it works.

- [x] ✅ Clipboard capture — text, HTML, RTF, images (PNG), file lists — via `AddClipboardFormatListener`
- [x] ✅ Local SQLite history (`%LOCALAPPDATA%/ClipForge/clips.db`), survives reboot
- [x] ✅ **Privacy-by-default in the capture path:** secret auto-detection refuses common secret patterns at the capture boundary (API keys, Luhn-validated card numbers, OTP codes, private keys, `password:` assignments — best-effort: first 8KB of text scanned, not a guarantee); password-manager apps (1Password, Bitwarden, KeePass…) are excluded; unpinned history expires after 90 days — retention is NOT indefinite
- [x] ✅ Trust engineering: delayed retry on locked clipboard (never silently miss a copy), duplicate suppression, self-paste suppression, explorer-restart tray recovery, zero nag dialogs
- [x] ✅ Global hotkey `Ctrl+Shift+V` → command-palette overlay (WPF, dark, HiDPI-clean)
- [x] ✅ Instant search as you type — Ditto-style substring filter, ranked by match quality (position, word-boundary, exact case)
- [x] ✅ `Enter` pastes the selected clip, `Esc`/click-away dismisses
- [x] ✅ Pin clips (`Ctrl+P` or 📌) — DB flag, pinned section on top, exempt from retention pruning
- [x] RTF capture — stored as RTF + plain-text companion — implemented, not yet seen running
- [x] Paste as plain text (`Ctrl+Enter`) — implemented, not yet seen running
- [x] Image thumbnails in the overlay (async, cached) — implemented, not yet seen running
- [x] Minimal settings (retention, max clips, excluded apps, rebindable hotkey) + autostart on login — implemented, not yet seen running
- [x] Tray + window + exe icon — implemented, not yet seen running
- [x] Inno Setup installer (`ClipForge-Setup-0.2.1.exe`) — built, not yet installed end-to-end

**Next (iteration order):** search highlighting → multi-paste → case transforms → snippets with variables → semantic search → OCR on images → cloud sync (opt-in). See [docs/BUILD_IN_PUBLIC.md](docs/BUILD_IN_PUBLIC.md).

## Known limitations

Stated plainly, so nobody has to discover them the hard way:

- **Search scope.** The overlay searches the 500 most recent clips, matching against each clip's preview (the first 200 characters) and source app — not the full stored text. Older clips are in the database but not reachable from search yet.
- **Unencrypted database.** History is a plain SQLite file in your user profile (`%LOCALAPPDATA%/ClipForge/clips.db`). Anyone who can read files as your Windows user can read it. Encryption is not implemented yet.
- **Secret filter window.** The secret scan examines the first 8 KB of a text capture, and images are never scanned. It is a best-effort filter, not a guarantee.
- **HTML pastes as plain text, for now.** HTML clips are captured with their markup, but pasting one currently inserts the markup as plain text — a rich, formatted HTML paste is not implemented yet. (`Ctrl+Enter` strips the tags on purpose.)

## Improving a piece (plugin seams)

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

An automated test suite (xUnit) covers the platform-independent projects (Core, Secrets, Search, Storage.Sqlite) and runs anywhere: `dotnet test ClipForge.sln -c Release`. See [TESTING.md](TESTING.md) for what's covered and what isn't.

## Project layout

```
src/
  ClipForge.Core/          # contracts + model: IClipStore, ISecretDetector,
                           #   ISearchRanker, IClipFormatHandler, PluginLoader,
                           #   ClipboardListener (handler-driven), Win32 P/Invoke
  ClipForge.Secrets/       # built-in ISecretDetector
  ClipForge.Search/        # built-in ISearchRanker (substring ranker)
  ClipForge.Storage.Sqlite/# built-in IClipStore (SQLite)
  ClipForge.Formats/       # built-in IClipFormatHandlers (text/html/image/files)
  ClipForge.App/           # WPF: overlay, global hotkey, paste, tray icon
docs/
  BUILD_IN_PUBLIC.md       # the build log — every iteration is a public beat
```

The `ClipForge.Core` ↔ UI seam is deliberately clean — search, pinning UI, snippets,
semantic search and OCR all build on top of it.

## Improving a piece

Every major piece lives behind an interface in `ClipForge.Core` and ships in its
own assembly — improve one piece without touching the rest:

| Piece | Interface | Built-in assembly |
|---|---|---|
| Secret detection | `ISecretDetector` | `ClipForge.Secrets` |
| Search ranking | `ISearchRanker` | `ClipForge.Search` |
| Clip storage | `IClipStore` | `ClipForge.Storage.Sqlite` |
| Clipboard formats | `IClipFormatHandler` (one per format) | `ClipForge.Formats` |

To swap a piece: implement the interface with a parameterless constructor, build
the DLL, drop it in the `plugins/` folder next to `ClipForge.exe`, restart.
The first plugin implementation found wins for single-implementation seams
(secrets, search, store); format handlers are additive, tried in `Priority`
order. A DLL that fails to *load* is skipped with a Debug note. Beyond that
there is no isolation: plugin code runs in-process, so a plugin that hangs or
faults can hang or crash the app — load only plugins you trust.
No versioning, no sandboxing, no signing: this is a contributor seam, not a marketplace.

## Privacy

Local-first, always: one SQLite file you own, no network calls, no telemetry, no account. Common secret patterns are refused at the capture boundary (best-effort filter, not a guarantee — see `SecretDetector.cs`).

## License

MIT — see [LICENSE](LICENSE). Built in public; PRs welcome.
