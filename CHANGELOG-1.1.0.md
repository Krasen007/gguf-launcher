# GGUF Launcher — Changelog

All changes since tag `v1.0.1` (10 commits, 2026-11-04 → 2026-11-08).
31 files changed · +3,839 / −217 lines · `dotnet build` clean (0 warnings, 0 errors) · `dotnet test` passing.

This file is suitable to copy into a GitHub release (e.g. the next release after `v1.0.1`).

---

## 🖥️ Native Windows UI (WinForms)

**Bare launch now opens a native quick-pick window** (`GgufLauncher.exe` with no arguments), while launching a `.gguf` file stays headless.
- Window lists the last 10 opened models (most recent first), with an editable `.args` panel.
- Actions: Save, Launch, Reload, Save & launch, `Ctrl+S`.
- Missing-model launch disabling and unsaved-change confirmation.
- New **Load model…** button to open a `.gguf` that isn't in the recent list yet; it joins the recent list and launches with generated default arguments.
- `LauncherPalette` static class holds colors/fonts; `LauncherForm` uses it and disposes its fonts/timer.
- New `LauncherUiModel` and `LaunchPlan` files; the `.args` preview is generated from the same `LaunchPlanner` used by real launches.
- Windows Forms does not support trimming, so `PublishTrimmed` is now `false`; the published self-contained `GgufLauncher.exe` is 66,288,392 bytes.

## 📄 Per-model `.args` file handling

- **First launch (no `.args`):** the launcher writes `<model-name>.args` next to the model with the full effective llama-server argument list (everything except `-m`), including built-in defaults and companion-file flags. The file is rewritten each launch and normalized so re-reading it can't duplicate or grow the argument set (repeated flags collapse to their last value, matching llama-server's "later wins"; genuinely repeatable flags like `--lora`, `--control-vector`, `--override-kv` are preserved).
- **Existing `.args` is now authoritative:** it is the complete argument set — no baked-in defaults and no mmproj / speculative-draft companion detection on top of it. The launcher adds only `-m`, plus `--host`/`--port`/`--alias` when the file doesn't set them (needed for the browser URL).
- Logs `[launcher] args file: <path>` so you can see the file is in charge.
- Edits and deletions to the file take effect on the next launch; delete the file to regenerate it with fresh defaults and detection.
- `gguf-launcher.args` next to the exe remains user-owned (never written by the launcher) and still applies before the per-model file.

## 🔗 Network binding scope

- First launch (no per-model `.args`): still binds `0.0.0.0:8080`, so the model is reachable from other machines on the network.
- Existing `.args`: followed exactly. When `--host`/`--port` are absent, llama-server's own defaults apply (`127.0.0.1:8080`).
- The browser URL and health check follow the effective host/port, mapping wildcard binds to loopback.

## 📝 Per-argument info notes

- New `ArgumentNotes.cs` maps the flags the launcher writes plus common user flags to one-line explanations; unknown flags are reported as passed through to `llama-server` unchanged.
- The args editor has an **INFO row** that follows the caret or the mouse and explains the argument on that line; comments and blank lines show a hint.

## 🛡️ Audit suggestions / robustness

- `ServerReadiness.OpenWhenReadyAsync`: health polling is now server-process-aware — it stops when the server process exits, on Ctrl+C, after the browser opens, or at the 15-minute safety-net deadline, and prints one `still waiting` line after 60s. The health URL is built with `new Uri(baseUri, "health")`. Exception handling narrowed to `HttpRequestException`/`OperationCanceledException`/`TaskCanceledException`.
- New `Log.Warn` helper centralizes launcher diagnostics (console now; file sink deferred).
- `SplitLauncherFlags` replaces `Merge` + `HasNoBrowser` + `FilterBrowserFlag` with a single pass returning `(bool NoBrowser, List<string> ServerArgs)`; the exact match is preserved so variants such as `--no-browser=true` stay in the server args.
- Args-file read now catches `UnauthorizedAccessException` alongside `IOException` and logs via `Log.Warn`; kill-on-exited narrowed to `InvalidOperationException`; console-encoding catch narrowed to `IOException`.
- 14 new tests in `tests/AuditSuggestionsTests.cs`.

## 🌐 Browser URL handling

- Browser URLs are now parsed **before** `process.Start()`. An invalid URL skips opening the browser without preventing the server from starting. Readiness-task faults are logged; cancellation is ignored.

## 🧪 Tests

New test files:
- `tests/NativeUiTests.cs` (272), `tests/AuditSuggestionsTests.cs` (314), `tests/LoadModelButtonTests.cs` (133), `tests/ArgsFileEditableTests.cs` (118), `tests/ArgsFileAuthoritativeTests.cs` (117)
- `tests/ArgumentInfoNoteTests.cs` (92), `tests/ReviewFixesOrderingTests.cs` (65), `tests/ReviewFixesTests.cs` (57), `tests/ServerDefaultsTests.cs` (52), `tests/ProgramStartupReviewTests.cs` (47), `tests/LauncherArgsTests.cs` (24), `tests/ContinueWorkTests.cs` (15)

## 📚 Documentation

- `README.md` rewritten/expanded: native quick-pick window, per-model `.args` generation and precedence, binding behavior, file association (`--register`/`--unregister`), `--no-browser`, configuration with `.args` files.
- New `History.md` (request → behavior change → test entries per `AGENTS.md`).
- `AGENTS.md`, `.gitignore`, `guf-launcher.csproj`, `publish.bat`, and `gguf-launcher.args.sample` tweaks.

---

### Full commit list (newest first)

| Commit | Date | Summary |
|--------|------|---------|
| `70c8cb0` | 2026-11-08 | Startup flow: parse browser URLs before `process.Start()`; invalid URLs skip browser opening without preventing server start. |
| `115670d` | 2026-11-08 | Audit suggestions: server-aware health poll, `Log.Warn`, `SplitLauncherFlags`, `LauncherPalette`, 14 new tests. |
| `639d4bd` | 2026-11-08 | Per-argument info notes (`ArgumentNotes.cs`); INFO row in the args editor. |
| `e792a1d` | 2026-11-08 | Add **Load model** button to the native UI. |
| `3c77642` | 2026-11-08 | Fixes in `LauncherArgs.cs`, `LauncherForm.cs`, `RecentModels.cs` (whitespace round-trip, tensor overrides, grouped layer ranges, reload reporting, null-entry filtering). |
| `9995079` | 2026-11-08 | Simple native Windows interface (recent-model window, editable `.args` panel, Save/Launch/Reload). |
| `f44d0d5` | 2026-11-08 | First launch binds `0.0.0.0:8080`; existing `.args` followed exactly. |
| `cb559cd` | 2026-11-08 | Existing `<model>.args` is the complete argument set (no injected defaults/companions). |
| `fc8f799` | 2026-11-08 | Write `<model>.args` next to the model on every launch with the arguments used. |
| `1db0907` | 2026-11-04 | Update README. |

---

*Generated from `git log v1.0.1..HEAD` and `git diff --stat v1.0.1..HEAD` on branch `master`.*

