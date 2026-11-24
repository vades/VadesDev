using Microsoft.Extensions.AI;
using VadesDev.Rag.Embeddings;

namespace VadesDev.Rag.Tests.Fakes;

/// <summary>
/// Test double for the embedding model: wraps the fake embedder, counts the calls and the embedded texts,
/// and can be told to fail. Lets a test prove that an unchanged file makes no embedding call.
/// </summary>
public sealed class SpyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HashingEmbeddingGenerator _inner = new();

    /// <summary>
    /// How many times GenerateAsync was called.
    /// </summary>
    public int CallCount { get; private set; }

    /// <summary>
    /// How many texts were embedded in all calls together.
    /// </summary>
    public int TextCount { get; private set; }

    /// <summary>
    /// If set, every call throws this exception (after it was counted), like an AI server that is not running.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Counts the call, throws the configured failure if there is one, otherwise embeds with the fake embedder.
    /// </summary>
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();

        CallCount++;
        TextCount += texts.Count;

        if (Failure is not null)
        {
            throw Failure;
        }

        return await _inner.GenerateAsync(texts, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}