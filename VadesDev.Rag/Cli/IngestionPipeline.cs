using VadesDev.Rag.Documents;
using VadesDev.Rag.Models;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Cli;

/// <summary>
/// One file from disk to the database: read, chunk, sanitize and fingerprint it,
/// then skip it if it is unchanged, or embed it and store it.
/// </summary>
public class IngestionPipeline
{
    private readonly MarkdownFileReader _reader;
    private readonly ChunkIndexer _indexer;
    private readonly PostgresChunkStore _store;
    private readonly string _embeddingModel;

    public IngestionPipeline(
        MarkdownFileReader reader, ChunkIndexer indexer, PostgresChunkStore store, string embeddingModel)
    {
        _reader = reader;
        _indexer = indexer;
        _store = store;
        _embeddingModel = embeddingModel;
    }

    /// <summary>
    /// Makes sure the file is stored for the current embedding model. Skips the embedding if nothing changed.
    /// </summary>
    public async Task IngestAsync(string path)
    {
        var document = await _reader.ReadAsync(path);
        var chunks = PrepareChunks(document);

        if (chunks.Length == 0)
        {
            Console.WriteLine($"No chunks found in {document.Source}, skipped.");
            return;
        }

        var sourceHash = ChunkHasher.Compute(chunks);

        if (await _store.IsUpToDateAsync(_embeddingModel, document.Source, sourceHash))
        {
            Console.WriteLine($"Up to date: {document.Source} ({chunks.Length} chunks), embedding skipped.");
            return;
        }

        await EmbedAndStoreAsync(chunks, sourceHash);
    }

    /// <summary>
    /// Embeds the chunks and stores them together with their fingerprint.
    /// </summary>
    private async Task EmbedAndStoreAsync(DocumentChunk[] chunks, string sourceHash)
    {
        Console.WriteLine($"Indexing {chunks[0].Source}...");

        var index = await _indexer.BuildIndexAsync(chunks);
        await _store.ReplaceChunksAsync(_embeddingModel, sourceHash, index);

        Console.WriteLine($"Stored {index.Count} chunks ({index[0].Vector.Length} dimensions).");
    }

    /// <summary>
    /// Splits the document into chunks first (the chunker needs the # markers), then sanitizes each chunk.
    /// </summary>
    private static DocumentChunk[] PrepareChunks(SourceDocument document)
    {
        return DocumentChunker.Chunk(document)
            .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
            .ToArray();
    }
}