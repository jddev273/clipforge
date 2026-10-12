# Testing

ClipForge has an automated test suite (xUnit) in `tests/ClipForge.Tests`.
It covers the platform-independent projects — Core, Secrets, Search, and
Storage.Sqlite — which is where the capture-path decisions live: what gets
refused as a secret, what counts as an empty capture, which apps are
excluded, how clips are stored, pruned, and ranked.

## Run it

```powershell
dotnet test ClipForge.sln -c Release
```

Needs the .NET 8 SDK. The suite runs on Windows, macOS, and Linux — the
projects under test have no Windows dependencies. CI runs it on every push
(see `.github/workflows/build.yml`). The test code is held to the same
analyzer rules as the product code; the build must stay at 0 warnings.

## What's covered

- **Secret detection** — every pattern class in the detector (key prefixes,
  Luhn-checked card numbers, one-time codes in context, private-key blocks,
  `password:`-style assignments), plus the documented limits: a bare
  high-entropy string with no label is *not* flagged, and only the first
  8 KB of a capture is scanned.
- **The RTF path** — RTF captures are scanned before storage. These are
  regression tests for a real bug: RTF escape sequences (`\'3a`, `\u58?`)
  once hid a `password:` assignment from the scanner, so the secret was
  stored verbatim. Escapes are now decoded before scanning, and the tests
  pin that behavior.
- **Empty captures** — whitespace-only text, empty-paragraph RTF, and
  markup-only RTF are refused; the smallest real content is accepted.
- **Sensitive-app exclusion** — the listed password managers are matched
  (case-insensitive substring). Browser processes are deliberately *not*
  on the list; a password copied from a browser extension or web vault is
  attributed to the browser and can only be caught by the pattern scan.
  The tests encode that limitation as expected behavior.
- **SQLite store** — save/load round-trips, recent ordering (pinned
  first), search (including literal `%`/`_` handling), delete, pin,
  prune-by-count and prune-by-age (pinned clips are exempt), and
  persistence across reopening the same database file. Each test uses its
  own temp-file database and cleans up after itself.
- **Search ranking** — the substring ranker's scoring rules (match
  position, word boundaries, exact case) and the ordering of
  `FilterAndRank`.
- **Hotkey chord parsing** — the text parsing behind the settings chord
  (modifier synonyms, case and spacing drift: `CONTROL+SHIFT+V` and
  `Ctrl+Shift+V` are the same chord) lives in Core and is tested there.
- **Capture handler chain** — handlers are tried in priority order, and a
  handler that claims a format but fails to capture falls through to
  lower-priority handlers instead of dropping the copy (regression tests
  for a real lost-copy bug).
- **Paste restore decision** — when staging a paste fails, the pre-paste
  clipboard snapshot is restored only if the clipboard sequence number
  moved during staging (i.e. staging actually emptied the clipboard); an
  untouched clipboard is never overwritten with the older snapshot. The
  decision lives in Core and is tested there; the staging itself is
  Windows-only and manually verified.

## What isn't covered, and why

The WPF app itself — overlay window, clipboard listener, global hotkey
registration, paste, tray icon, settings window — only runs on Windows
with a real desktop and clipboard, so it can't execute in this suite. It
is verified by the strict build (analyzers, warnings as errors) and by
manual testing on Windows. The format handlers in `ClipForge.Formats`
are thin wrappers over the live Windows clipboard for the same reason;
the text parsing and stripping they rely on lives in Core and is tested
there.

## A note on restricted sandboxes

`dotnet test` talks to its test-host process over a loopback TCP
connection. In sandboxes that intercept or rewrite loopback connections,
the test host never connects and the run aborts after a timeout — the
suite itself is fine. In such an environment, run the same suite
in-process with the xUnit console runner instead:

```bash
dotnet build ClipForge.sln -c Release
dotnet exec <xunit.runner.console>/tools/net6.0/xunit.console.dll \
  tests/ClipForge.Tests/bin/Release/net8.0/ClipForge.Tests.dll
```

(with `DOTNET_ROLL_FORWARD=LatestMajor` if only a newer runtime is
installed). Same tests, same results — just no socket between runner
and test host.
