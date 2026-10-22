using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Everything the pipeline needs from one AI provider: name, chat client, embedding generator
/// and the MinScore calibrated for that embedding model.
/// </summary>
public record AiProvider(
    string Name,
    IChatClient ChatClient,
    IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator,
    float MinScore);