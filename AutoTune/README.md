# AutoTune

The VRAM budget behind the launch parameters the launcher derives for a model. A 9B model on
an 8 GB card should start with a context that fits instead of dying with an out of memory error.

## The budget

```
budget = free VRAM - margin        # margin is 1024 MiB by default
usable = budget * 0.92             # 8 percent headroom for the driver and Vulkan
weights = tensor data - vocabulary * embedding * bytes per weight   # separate output only
compute = vocabulary * micro batch * 4 bytes + 64 MiB
```

On the reference machine (RTX 3070, 8192 MiB total, 7342 MiB free) this gives 6318 MiB of
budget and 5812 MiB to plan with. The weight formula subtracts the embedding matrix that
llama.cpp shares between the token embedding and the output projection, 0.5625 bytes per
weight for Q4_K_M, 1.0625 for Q8_0 and 0.289 for IQ2_XS. The logits tensor of a 248320
token vocabulary at a batch of 512 is 485 MiB of the compute buffer.

## The KV cache

```
layers      = block count / full attention interval   # hybrid models
            = global attention layers                 # sliding window models
            = block count                             # everything else
bytes/token = layers * key heads * head dim * bytes per element * 2
window      = min(context, window) tokens per layer   # fixed term, does not grow
```

## The size classes

| class | parameters | context | cache | ubatch | batch |
|---|---|---|---|---|---|
| Tiny | <= 2000 M | 32768 | q8_0 | 512 | 2048 |
| Small | <= 5000 M | 32768 | q8_0 | 512 | 2048 |
| Medium | <= 14000 M | 16384 | q8_0 | 512 | 2048 |
| Large | <= 32000 M | 8192 | q8_0 | 256 | 2048 |
| XLarge | <= 70000 M | 4096 | q4_0 | 256 | 1024 |
| Huge | > 70000 M | 2048 | q4_0 | 256 | 1024 |

## What the constants are calibrated against

Measured with llama.cpp 0.7.0-dev on the reference machine:

| model | cache | bytes per token | measured |
|---|---|---|---|
| 9B hybrid | 8 full attention layers, 4 key heads, head dim 256 | 32768 | 32 KiB |
| 27B hybrid | 16 full attention layers | 65536 | 64 KiB |
| Gemma3 4B | 5 global layers of 34 blocks, window 1024 | 20480 | 20 KiB |

- The 9B at 262144 tokens took 4812 MiB for the model, 8242 MiB for the context and 773 MiB for the compute buffer.

## Known approximations

- The embedding discount assumes llama.cpp shares one embedding matrix, so the estimate is never smaller than the measured model memory.
- The 0.92 factor and the 1024 MiB margin overlap, because the free VRAM that nvidia-smi reports changes while the server runs.
- The global layer table is hard coded for gemma2 and gemma3 (every six blocks) and gpt-oss (every four blocks); every other architecture keeps the full context in every layer, the safe direction.
- A hybrid model that does not announce its sliding window has an unknown cache size, so the sliding layers are only charged when the model is split over CPU and GPU.
- The weights are spread evenly over the blocks when the model is split, while llama.cpp splits them by tensor.
- A model without a layer count, a vocabulary or an attention shape is estimated with 32 layers, 262144 vocabulary entries and the full embedding width.
