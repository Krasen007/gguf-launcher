![alt text](icon.ico)

# GGUF Launcher

Double-click a `.gguf` file in Windows Explorer and a llama.cpp server starts for it,
with sensible defaults already set, and the web UI opens in your browser.

## What it does

- Finds `llama-server.exe` (next to this exe, on `PATH`, via `LLAMA_SERVER`, or in
  WinGet packages).
- Detects companion files next to the model: `mmproj-*.gguf` for vision models and
  `dspark-*` / `*draft*` / `*-mtp*` files for speculative decoding, and wires them up.
- Starts `llama-server.exe` with the derived flags, streams its logs to the console,
  waits for `/health` to answer, then opens the browser.
- On the first launch the effective arguments are written to `<model-name>.args` next to
  the model. That file is never overwritten; while it exists it is the complete argument
  set, so the launcher adds no defaults and re-detects no companion files.
- Registers itself as the handler for `.gguf` files (per user, no administrator needed).

## Build

```powershell
dotnet publish -c Release -r win-x64 -o publish
```

The output is a single self-contained `GgufLauncher.exe`.

## Use

```powershell
GgufLauncher.exe model.gguf
# or, llama-server style:
GgufLauncher.exe -m model.gguf
```

Register the file association (per user):

```powershell
GgufLauncher.exe --register
```

Remove it again:

```powershell
GgufLauncher.exe --unregister
```

Skip opening the browser:

```powershell
GgufLauncher.exe model.gguf --no-browser
```

Any extra arguments are passed straight through to `llama-server`.

## Configuration

There is no JSON configuration. Defaults are baked into the launcher (context 98192,
batch 512, 6 threads, 99 GPU layers, flash attention, jinja, q4_0 KV cache, ...).

Override them with plain `.args` files (one or more flags per line, `#` starts a
comment):

1. Global: `gguf-launcher.args` next to `GgufLauncher.exe`
2. Per model: `<model-name>.args` next to the `.gguf`

Command-line arguments win over both. Later flags win within a file.

On the first launch the launcher creates `<model-name>.args` next to the model, with the
full effective argument list passed to `llama-server` (everything except the model
path), including the built-in defaults and the flags derived from companion files.

That file is never overwritten. While it exists it is the complete argument set for the
model: the launcher adds neither the built-in defaults nor companion-file flags, so
deleting a line actually removes that flag from the launch. It only adds `-m` and, when
the file does not set them, `--host`, `--port` and `--alias`. Delete the file to have it
regenerated with fresh defaults and detection. The global `gguf-launcher.args` next to
the exe is never written by the launcher and still applies before the per-model file.

Example `gguf-launcher.args`:

```
# keep it private
--host 127.0.0.1
# smaller context for a small GPU
-c 32768
```

## Notes

- The server binds to `0.0.0.0` by default, so the model is reachable from other
  machines on the network. Pass `--host 127.0.0.1` (or put it in an `.args` file) to
  keep it private to this machine.
- Windows 11 does not let applications set the "default app" hash, so the launcher
  registers the association but you may still need to pick it once in *Open with* /
  *Default apps*.
- Press `Ctrl+C` in the launcher console to stop the server.