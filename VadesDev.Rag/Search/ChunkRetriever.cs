using VadesDev.Rag.Configuration;
using VadesDev.Rag.Models;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Search;

/// <summary>
/// Searches the stored chunks: asks the database for the nearest chunks of one embedding model
/// and keeps the hits that reach MinScore (best first, at most TopK).
/// </summary>
public class ChunkRetriever
{
    private readonly PostgresChunkStore _store;
    private readonly string _embeddingModel;
    private readonly RagSettings _settings;

    public ChunkRetriever(PostgresChunkStore store, string embeddingModel, RagSettings settings)
    {
        _store = store;
        _embeddingModel = embeddingModel;
        _settings = settings;
    }

    /// <summary>
    /// Returns the best chunks for a question vector: above MinScore, highest score first, at most TopK.
    /// </summary>
    public async Task<List<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryVector)
    {
        var nearest = await _store.SearchAsync(_embeddingModel, queryVector, _settings.TopK);

        return nearest.Where(h => h.Score >= _settings.MinScore).ToList();
    }
}