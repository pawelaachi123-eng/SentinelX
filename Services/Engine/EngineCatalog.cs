namespace SentinelX.Services.Engine;

/// <summary>A local chat model the built-in engine can run. <see cref="Name"/> is the name the rest of the app already uses
/// (settings, model routing, „modele AI”), so the light/strong model switching works exactly as before — only the runtime changed.</summary>
public sealed record EngineModelSpec(string Name, string Title, string FileName, string Url, long SizeBytes, string Sha256,
    int MinRamGb, string Family, string ParameterSize, string Quantization);

/// <summary>Everything the engine downloads is pinned here: exact llama.cpp build, exact model revision and SHA-256 of each file.
/// A file whose hash does not match is deleted and never executed or loaded.</summary>
public static class EngineCatalog
{
    /// <summary>llama.cpp (MIT) — the same inference engine that Ollama is built on, run directly. Build b11146 is the one the
    /// upstream project marks as its current release (v0.5.0, 2026-09-23). CPU build for Windows x64: works on every PC, no GPU drivers.</summary>
    public const string LlamaBuild = "b11146";
    public const string LlamaZipFile = "llama-b11146-bin-win-cpu-x64.zip";
    public const string LlamaZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11146/llama-b11146-bin-win-cpu-x64.zip";
    public const long LlamaZipSize = 18560055;
    public const string LlamaZipSha256 = "14cf1303ca9ac3abd94816850532f9f9a69ac66fbaca3776fc6f9061c2fac1d1";

    /// <summary>Light model: fast answers on any PC and the one used while a game runs. Qwen3 1.7B, Apache-2.0.</summary>
    public static readonly EngineModelSpec Lite = new(
        "qwen3:1.7b", "Qwen3 1.7B (lekki)", "Qwen3-1.7B-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/d7f544eead698dbd1f15126ef60b45a1e1933222/Qwen3-1.7B-Q4_K_M.gguf",
        1107409472, "b139949c5bd74937ad8ed8c8cf3d9ffb1e99c866c823204dc42c0d91fa181897", 4, "qwen3", "1.7B", "Q4_K_M");

    /// <summary>Stronger model for idle PCs with enough memory. Qwen3 4B Instruct 2507, Apache-2.0.</summary>
    public static readonly EngineModelSpec Standard = new(
        "qwen3:4b-instruct", "Qwen3 4B Instruct (mocny)", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
        "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
        2497281120, "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597", 12, "qwen3", "4B", "Q4_K_M");

    public static readonly IReadOnlyList<EngineModelSpec> Models = [Lite, Standard];

    public static EngineModelSpec? Find(string name) =>
        Models.FirstOrDefault(x => x.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
