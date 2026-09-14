using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBA.Vision;

/// <summary>
/// Loads precomputed CLIP/SigLIP text-prompt embeddings for <see cref="ClipZeroShotSportClassifier"/> from a
/// JSON file produced by the offline text-encoder step described in design.md - the text encoder and tokenizer
/// never ship or run in this app; this only reads their precomputed output. Expected shape:
/// <code>
/// [
///   { "sport": "basketball", "prompt": "a basketball game", "embedding": [0.01, -0.02, ...] },
///   { "sport": "soccer", "prompt": "a soccer match", "embedding": [...] }
/// ]
/// </code>
/// "prompt" is kept for humans/debugging only and is not read back by this loader. Embeddings are re-normalized
/// on load regardless of whether the source file already normalized them, so this is the one place
/// <see cref="ClipZeroShotSportClassifier"/>'s "prompts are already L2-normalized" contract is actually enforced
/// for embeddings that came from a file rather than being constructed directly (e.g. in tests).
/// </summary>
public static class ClipPromptEmbeddings
{
    private sealed record Entry(
        [property: JsonPropertyName("sport")] string Sport,
        [property: JsonPropertyName("prompt")] string? Prompt,
        [property: JsonPropertyName("embedding")] float[] Embedding);

    public static IReadOnlyList<ClipSportPrompt> LoadFromJson(string path)
    {
        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<Entry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a prompt-embedding list.");

        if (entries.Count == 0)
        {
            throw new InvalidDataException($"'{path}' contains no prompt embeddings.");
        }

        return entries.Select(e => new ClipSportPrompt(new SportType(e.Sport), Normalize(e.Embedding))).ToList();
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm < 1e-12f)
        {
            throw new InvalidDataException("A prompt embedding is all-zero and cannot be normalized.");
        }

        return vector.Select(v => v / norm).ToArray();
    }
}
