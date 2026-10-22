namespace GgufLauncher.Cli;

/// <summary>The usage text shown for <c>--help</c> and for invalid command lines.</summary>
public static class HelpText
{
    /// <summary>Prints the usage text to standard output.</summary>
    public static void Print()
    {
        Console.WriteLine("""
            GGUF Launcher - start a llama.cpp server by double-clicking a .gguf file.

            Usage:
              GgufLauncher <model.gguf> [options]   start the server for a model
              GgufLauncher --register               make .gguf open with this launcher
              GgufLauncher --unregister             remove the .gguf association
              GgufLauncher --status                 show the current configuration and association
              GgufLauncher --help                   show this text

            Options:
              -m <path>                model file to serve, the same as naming it directly
              --config <path>            config.json to use instead of the default search
              --llama-server <path>      llama-server.exe to use (a folder is also accepted)
              --port <n>                 TCP port to listen on (default 8080)
              --host <addr>              interface to bind (default 0.0.0.0)
              --ctx <n>                  context size; omit to use the model's context, capped at maxContextSize
              --threads <n>              CPU threads; omit to let llama.cpp decide
              --ngl <n>                  layers to offload to the GPU; omit to let the tuner decide
              --auto-tune <on|off>       fit the model into the free VRAM (default on)
              --vram-margin <MiB>        VRAM kept free for everything else (default 1024)
              --cache-type <type>        KV cache type for K and V: f16, q8_0 or q4_0
              --batch-size <n>           logical batch size
              --ubatch-size <n>          physical batch size
              --mmproj <path>            multimodal projector to use
              --spec-draft-model <path>  speculative-decoding draft model to use
              --spec-type <type>         draft strategy, e.g. draft-dspark or draft-mtp
              --cpu-moe                  keep MoE weights in system RAM
              --no-cpu-moe               force MoE weights onto the GPU
              --jinja                    use the model's Jinja chat template
              --no-jinja                 do not use the Jinja chat template (default)
              --no-browser               do not open a browser when the server is ready
              --no-webui                 serve the API only, without the built-in web UI
              --dry-run                  print the command line instead of running it
              --quiet                    print less

            Configuration file (config.json), searched next to the exe and then in
            %APPDATA%\GgufLauncher:
              { "port": 8080, "host": "0.0.0.0", "maxContextSize": 65536, "models": { } }

            The autoTune section of config.json holds the settings of the VRAM tuner:
              { "autoTune": { "enabled": true, "marginMiB": 1024, "maxContextSize": 65536 } }
            """);
    }
}
