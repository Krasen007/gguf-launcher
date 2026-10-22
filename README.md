# GGUF Launcher

Double-click a `.gguf` file in Windows Explorer and a llama.cpp server starts for it,
with the right flags already set, and the web UI opens in your browser.

## What it does

- Reads the GGUF metadata to learn the architecture, the context length and whether the
  model is multimodal.
- Fits the model into the free video memory: it measures the model, its projector and its
  draft model, asks the VRAM estimator for a plan, and takes the context size, the number
  of offloaded layers, the KV cache quantisation, the batch sizes and the thread count
  from it. A projector or a draft model that does not fit is left out instead of killing
  the server with an out of memory error.
- Looks for companion files next to the model: `mmproj-*.gguf` for vision models and
  `dspark-*.gguf` / `*draft*.gguf` / `*-mtp*.gguf` for speculative decoding. When more
  than one projector sits in the folder the `-4x` one wins; a draft model is only wired
  up when its strategy can be inferred from its name (`draft-dspark`, `draft-mtp`).
- Starts `llama-server.exe` with the derived flags, streams its logs to the console,
  waits for `/health` to answer, then opens the browser.
- Registers itself as the handler for `.gguf` files (per user, no administrator needed).

## Build

```powershell
dotnet publish -c Release -r win-x64 -o publish
```

The output is a single self-contained `GgufLauncher.exe`.

## Use

```powershell
GgufLauncher.exe model.gguf
```

Register the file association (per user):

```powershell
GgufLauncher.exe --register
```

Remove it again:

```powershell
GgufLauncher.exe --unregister
```

See what is registered right now:

```powershell
GgufLauncher.exe --status
```

Show what the launcher would do, without starting anything:

```powershell
GgufLauncher.exe model.gguf --dry-run
```

The model can also be named with an option, the way `llama-server` takes it:

```powershell
GgufLauncher.exe -m model.gguf --dry-run
```

Other useful flags: `--port`, `--host`, `--ctx`, `--threads`, `--ngl`, `--auto-tune`,
`--vram-margin`, `--cache-type`, `--batch-size`, `--ubatch-size`, `--mmproj`,
`--spec-draft-model`, `--spec-type`, `--cpu-moe`, `--no-browser`, `--no-webui`.
Run `GgufLauncher.exe --help` for the full list.

## Configuration

`config.json` is searched in this order:

1. the path given with `--config`
2. next to `GgufLauncher.exe`
3. `%APPDATA%\GgufLauncher\config.json`

See `config.sample.json` for the shape of the file. Global keys apply to every model;
the `models` map holds per-model overrides keyed by full path, file name, or file name
without the extension. Command-line flags win over both.

```json
{
  "port": 8080,
  "host": "0.0.0.0",
  "gpuLayers": null,
  "maxContextSize": 65536,
  "autoTune": {
    "enabled": true,
    "marginMiB": 1024,
    "maxContextSize": 65536
  },
  "models": {
    "MiniCPM-V-4_7-35B-A3B-Q4_K_M": {
      "contextSize": 98192,
      "cpuMoe": true
    }
  }
}
```

The key is matched exactly (ignoring case) against the model's full path, its file name
or its file name without the extension, so `MiniCPM-V-4_7-35B-A3B-Q4_K_M` above is the
stem of `MiniCPM-V-4_7-35B-A3B-Q4_K_M.gguf`. A key that matches none of the three is
ignored.

### Automatic VRAM tuning

A 9B model in `Q4_K_M` needs about 5.4 GiB of video memory, and the `mmproj-*.gguf` that
sits next to it needs another 1.7 GiB. Together that is more than an 8 GiB card can
hold, and a launcher that always writes `-ngl 99` and the full context of the model runs
out of memory on the first request.

The launcher therefore fits the model into the free video memory before it starts the
server. It measures the model file, the projector and the draft model, asks the
estimator for a plan, and turns that plan into the flags:

- the context size (`-c`), never larger than `maxContextSize`,
- the number of layers to offload (`-ngl`); the rest of the model stays in system RAM,
- the quantisation of the KV cache (`-ctk`, `-ctv`), `q8_0` when the memory is tight,
- the logical and the physical batch size (`-b`, `-ub`),
- the number of CPU threads (`-t`).

The projector and the draft model are part of the plan as well. When one of them does not
fit, the launcher leaves it out and says so, so the model still starts instead of dying
with an out of memory error. Every remark of the tuner starts with `auto-tune: ` and is
shown in the console and in `--dry-run`.

A 9B model with a 1.7 GiB projector next to it on an RTX 3070 with 8 GiB:

    [launcher] auto-tune: NVIDIA GeForce RTX 3070 with 7313 MiB free of 8192 MiB, 1024 MiB kept in reserve
    [launcher] auto-tune: 9B model (Medium), 5501.86 MiB of weights
    [launcher] auto-tune: the multimodal projector needs 1740 MiB, so fewer layers are offloaded to the GPU
    [launcher] auto-tune: offloading 21 of 33 layers to the GPU; the rest runs on the CPU
    [launcher] auto-tune: context 4096 tokens, KV cache q8_0/q8_0

The `autoTune` section of `config.json` holds the settings of the tuner:

| key | default | meaning |
| --- | --- | --- |
| `enabled` | `true` | turn the tuner off |
| `marginMiB` | `1024` | video memory kept free for everything else |
| `maxContextSize` | `65536` | largest context the tuner may pick |
| `contextSize` | `null` | context to use, `null` lets the tuner pick one |
| `gpuLayers` | `null` | layers to offload, `null` lets the tuner pick a number |
| `cacheTypeK` | `null` | quantisation of the key cache, `null` lets the tuner pick one |
| `cacheTypeV` | `null` | quantisation of the value cache, `null` lets the tuner pick one |
| `batchSize` | `null` | logical batch size, `null` lets the tuner pick one |
| `microBatchSize` | `null` | physical batch size, `null` lets the tuner pick one |
| `threads` | `null` | CPU threads, `null` lets the tuner pick a number |
| `allowProjectorSkip` | `true` | may the projector be left out when it does not fit |
| `allowDraftModelSkip` | `true` | may the draft model be left out when it does not fit |

The same keys work inside a `models` entry, where they apply to that model only. A key
that is left out is inherited from the global section.

The command line wins over both: `--auto-tune off` turns the tuner off, `--vram-margin
<MiB>` changes the reserve, `--cache-type f16|q8_0|q4_0` sets the quantisation of both
halves of the cache, and `--batch-size` / `--ubatch-size` set the batch sizes. A number
that you set by hand - `contextSize`, `gpuLayers`, `cacheTypeK`, `cacheTypeV` or
`threads` in the configuration, `--ctx`, `--ngl` or `--threads` on the command line - is
never overwritten by the tuner; it only gets a remark when it is larger than what fits.

When the tuner is off, the launcher behaves as it always did: the context is the smaller
of the model's own context and `maxContextSize`, and the number of layers is left to
llama.cpp. When no graphics card is found, the launcher says so in the console and
starts with those same defaults instead of guessing.

### Context size

A model advertises the context it was *trained* with, which is usually much larger than a
consumer GPU can hold once a projector and a draft model share the memory. The launcher
therefore starts from the smaller of the model's context and `maxContextSize` (65536 by
default) and tells you in the console what it picked.

Set `contextSize` for a model (or pass `--ctx`) to use a specific value; that value is used
as it is, with no cap.

With the [automatic tuner](#automatic-vram-tuning) switched on, the context is not taken
from the model at all: the tuner picks the largest one that fits into the free video
memory, and the projector and the draft model are part of that calculation.

### Draft models

When a `dspark-*` or `*-mtp*` file sits next to the model, the launcher wires it up as a
speculative-decoding draft model. On the Vulkan backend - the only GPU backend in the
current llama.cpp Windows build - letting llama.cpp sample that draft on the GPU aborts the
whole server on the first request:

    ggml-vulkan.cpp:5750: GGML_ASSERT(allow_misalign || misalign_bytes == 0) failed

The launcher therefore samples the draft on the CPU
(`--no-spec-draft-backend-sampling`). That step only touches a handful of logits, so it
costs nothing measurable and keeps the server alive. Set `"draftBackendSampling": true` in
`config.json`, globally or for a single model, to put the sampling back on the GPU.

## Notes

- The server binds to `0.0.0.0` by default, so the model is reachable from other
  machines on the network. Pass `--host 127.0.0.1` to keep it private to this machine.
- Windows 11 does not let applications set the "default app" hash any more, so the
  launcher registers the association but you may still need to pick it once in
  *Open with* / *Default apps*.
- Press `Ctrl+C` in the launcher console to stop the server.
