using System.Numerics.Tensors;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Search;

/// <summary>
/// Searches the index: scores every chunk against a question vector and returns the best hits.
/// </summary>
public class ChunkRetriever
{
    private readonly List<IndexedChunk> _index;
    private readonly RagSettings _settings;

    public ChunkRetriever(List<IndexedChunk> index, RagSettings settings)
    {
        _index = index;
        _settings = settings;
    }

    /// <summary>
    /// Returns the best chunks for a question vector: above MinScore, highest score first, at most TopK.
    /// </summary>
    public List<SearchHit> Search(ReadOnlyMemory<float> queryVector)
    {
        return _index
            .Select(i => new SearchHit(i.Chunk, TensorPrimitives.CosineSimilarity(queryVector.Span, i.Vector.Span)))
            .Where(h => h.Score >= _settings.MinScore)
            .OrderByDescending(h => h.Score)
            .Take(_settings.TopK)
            .ToList();
    }
}