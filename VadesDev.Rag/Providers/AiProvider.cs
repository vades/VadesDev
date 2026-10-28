using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Everything the pipeline needs from one AI provider: name, chat client, embedding generator,
/// the name of the embedding model and the MinScore calibrated for that model.
/// </summary>
public record AiProvider(
    string Name,
    IChatClient ChatClient,
    IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator,
    string EmbeddingModel,
    float MinScore);