using Microsoft.Extensions.AI;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Search;

/// <summary>
/// Builds the index: embeds every chunk and pairs it with its vector.
/// </summary>
public class ChunkIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;

    public ChunkIndexer(IEmbeddingGenerator<string, Embedding<float>> embedder)
    {
        _embedder = embedder;
    }

    /// <summary>
    /// Embeds all chunks in one batch and returns them together with their vectors.
    /// </summary>
    public async Task<List<IndexedChunk>> BuildIndexAsync(DocumentChunk[] chunks)
    {
        var embeddings = await _embedder.GenerateAsync(chunks.Select(c => c.Text));

        return [.. chunks.Zip(embeddings, (chunk, embedding) => new IndexedChunk(chunk, embedding.Vector))];
    }
}