using Microsoft.Extensions.AI;
using Npgsql;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Models;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Tests.Storage;

/// <summary>
/// Tests for PostgresChunkStore against the real database in Docker, with the fake embedder.
/// Every test instance uses its own made-up model names, so real rows are never touched.
/// If the database is not reachable, the tests are skipped.
/// </summary>
public class PostgresChunkStoreTests : IAsyncLifetime
{
    private readonly string _model = $"test-{Guid.NewGuid():N}";
    private readonly string _otherModel = $"test-{Guid.NewGuid():N}";
    private readonly NpgsqlDataSource _dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
    private readonly HashingEmbeddingGenerator _embedder = new();
    private readonly PostgresChunkStore _store;
    private readonly ChunkIndexer _indexer;

    public PostgresChunkStoreTests()
    {
        _store = new PostgresChunkStore(_dataSource);
        _indexer = new ChunkIndexer(_embedder);
    }

    /// <summary>
    /// Creates the table if needed. Skips the test when the database is not reachable.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            await _store.EnsureSchemaAsync();
        }
        catch (Exception exception) when (DatabaseErrors.IsDatabaseError(exception))
        {
            Assert.Skip($"Database not reachable, test skipped. {DatabaseErrors.Describe(exception)}");
        }
    }

    /// <summary>
    /// Removes the rows of both test models and closes the data source.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _store.DeleteSourcesExceptAsync(_model, []);
            await _store.DeleteSourcesExceptAsync(_otherModel, []);
        }
        catch (Exception exception) when (DatabaseErrors.IsDatabaseError(exception))
        {
            // Database not reachable: nothing was written, so there is nothing to clean up.
        }

        await _dataSource.DisposeAsync();
    }

    /// <summary>
    /// Embeds the texts as the chunks of one source (ids like "a.md-1") and stores them. Returns the fingerprint.
    /// </summary>
    private async Task<string> StoreAsync(string model, string source, params string[] texts)
    {
        var chunks = texts.Select((text, i) => new DocumentChunk($"{source}-{i + 1}", source, text)).ToArray();
        var hash = ChunkHasher.Compute(chunks);

        await _store.ReplaceChunksAsync(model, hash, await _indexer.BuildIndexAsync(chunks));

        return hash;
    }
    
    /// <summary>
    /// Embeds one text with the fake embedder, using the cancellation token of the running test.
    /// </summary>
    private async Task<ReadOnlyMemory<float>> EmbedAsync(string text)
    {
        return await _embedder.GenerateVectorAsync(text, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Counts the stored rows of one model that have the size of the fake embedder (4096 numbers).
    /// </summary>
    private async Task<int> CountRowsAsync(string model)
    {
        var hits = await _store.SearchAsync(model, await EmbedAsync("query"), 100);

        return hits.Count;
    }


    /// <summary>
    /// Nothing is stored yet, so the source is not up to date.
    /// </summary>
    [Fact]
    public async Task IsUpToDateAsync_ReturnsFalseWhenNothingStored()
    {
        Assert.False(await _store.IsUpToDateAsync(_model, "a.md", "hash"));
    }

    /// <summary>
    /// After storing a source, the same fingerprint means up to date.
    /// </summary>
    [Fact]
    public async Task IsUpToDateAsync_ReturnsTrueAfterReplace()
    {
        var hash = await StoreAsync(_model, "a.md", "alpha", "beta");

        Assert.True(await _store.IsUpToDateAsync(_model, "a.md", hash));
    }

    /// <summary>
    /// Another fingerprint (the file changed) means not up to date.
    /// </summary>
    [Fact]
    public async Task IsUpToDateAsync_ReturnsFalseForOtherHash()
    {
        await StoreAsync(_model, "a.md", "alpha");

        Assert.False(await _store.IsUpToDateAsync(_model, "a.md", "other-hash"));
    }

    /// <summary>
    /// Another embedding model means not up to date, because every model needs its own vectors.
    /// </summary>
    [Fact]
    public async Task IsUpToDateAsync_ReturnsFalseForOtherModel()
    {
        var hash = await StoreAsync(_model, "a.md", "alpha");

        Assert.False(await _store.IsUpToDateAsync(_otherModel, "a.md", hash));
    }

    /// <summary>
    /// Storing a source again replaces its old rows: 3 old rows and 2 new rows leave 2 rows, not 5.
    /// </summary>
    [Fact]
    public async Task ReplaceChunksAsync_ReplacesRowsOfSameSourceWithoutDuplicates()
    {
        await StoreAsync(_model, "a.md", "alpha", "beta", "gamma");
        Assert.Equal(3, await CountRowsAsync(_model));

        await StoreAsync(_model, "a.md", "delta", "epsilon");

        Assert.Equal(2, await CountRowsAsync(_model));
    }

    /// <summary>
    /// Replacing one source leaves the rows of other sources untouched.
    /// </summary>
    [Fact]
    public async Task ReplaceChunksAsync_KeepsRowsOfOtherSources()
    {
        await StoreAsync(_model, "a.md", "alpha");
        var hashOfB = await StoreAsync(_model, "b.md", "beta");

        await StoreAsync(_model, "a.md", "gamma");

        Assert.Equal(2, await CountRowsAsync(_model));
        Assert.True(await _store.IsUpToDateAsync(_model, "b.md", hashOfB));
    }

    /// <summary>
    /// Chunks of two sources are rejected, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ReplaceChunksAsync_ThrowsForChunksOfSeveralSources()
    {
        DocumentChunk[] chunks = [new("a.md-1", "a.md", "alpha"), new("b.md-1", "b.md", "beta")];
        var index = await _indexer.BuildIndexAsync(chunks);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.ReplaceChunksAsync(_model, "hash", index));

        Assert.Equal(0, await CountRowsAsync(_model));
    }

    /// <summary>
    /// The chunk that shares words with the question comes first and has the higher score.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ReturnsBestMatchFirst()
    {
        await StoreAsync(_model, "support.md", "Customer support hours are Monday through Friday, 9 AM to 5 PM CET.");
        await StoreAsync(_model, "shipping.md", "Standard shipping takes 3 to 5 business days within the EU.");

        var hits = await _store.SearchAsync(_model, await EmbedAsync("What are your support hours?"), 10);

        Assert.Equal(["support.md-1", "shipping.md-1"], hits.Select(h => h.Chunk.Id).ToArray());
        Assert.True(hits[0].Score > hits[1].Score);
    }

    /// <summary>
    /// Searching with another model name finds nothing.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ReturnsOnlyRowsOfRequestedModel()
    {
        await StoreAsync(_model, "a.md", "alpha");

        Assert.Equal(0, await CountRowsAsync(_otherModel));
    }

    /// <summary>
    /// The limit caps the number of hits: 3 rows stored, limit 2, 2 hits.
    /// </summary>
    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        await StoreAsync(_model, "a.md", "alpha", "beta", "gamma");

        var hits = await _store.SearchAsync(_model, await EmbedAsync("alpha"), 2);

        Assert.Equal(2, hits.Count);
    }

    /// <summary>
    /// A row with a vector of another size in the same model does not break the search
    /// (no "different vector dimensions" error) and is not returned.
    /// </summary>
    [Fact]
    public async Task SearchAsync_IgnoresVectorsOfOtherSizes()
    {
        await StoreAsync(_model, "big.md", "alpha");
        var tiny = new IndexedChunk(new DocumentChunk("small.md-1", "small.md", "tiny"), new float[] { 1, 2, 3 });
        await _store.ReplaceChunksAsync(_model, "hash", [tiny]);

        var hits = await _store.SearchAsync(_model, await EmbedAsync("alpha"), 10);

        Assert.Equal("big.md-1", Assert.Single(hits).Chunk.Id);
    }

    /// <summary>
    /// Only rows of sources that are not in the keep list are removed. The result is the number of removed rows.
    /// </summary>
    [Fact]
    public async Task DeleteSourcesExceptAsync_RemovesOnlyMissingSources()
    {
        await StoreAsync(_model, "a.md", "alpha");
        await StoreAsync(_model, "b.md", "beta");
        var hashOfC = await StoreAsync(_model, "c.md", "gamma");

        Assert.Equal(1, await _store.DeleteSourcesExceptAsync(_model, ["a.md", "b.md"]));
        Assert.Equal(0, await _store.DeleteSourcesExceptAsync(_model, ["a.md", "b.md"]));
        Assert.Equal(2, await CountRowsAsync(_model));
        Assert.False(await _store.IsUpToDateAsync(_model, "c.md", hashOfC));
    }

    /// <summary>
    /// An empty keep list removes every row of the model.
    /// </summary>
    [Fact]
    public async Task DeleteSourcesExceptAsync_WithEmptyListRemovesAllRowsOfModel()
    {
        await StoreAsync(_model, "a.md", "alpha", "beta");
        await StoreAsync(_model, "b.md", "gamma");

        Assert.Equal(3, await _store.DeleteSourcesExceptAsync(_model, []));
        Assert.Equal(0, await CountRowsAsync(_model));
    }

    /// <summary>
    /// The rows of another embedding model are never removed.
    /// </summary>
    [Fact]
    public async Task DeleteSourcesExceptAsync_DoesNotTouchOtherModels()
    {
        await StoreAsync(_model, "a.md", "alpha");
        var hashOfOther = await StoreAsync(_otherModel, "a.md", "alpha");

        Assert.Equal(1, await _store.DeleteSourcesExceptAsync(_model, []));
        Assert.True(await _store.IsUpToDateAsync(_otherModel, "a.md", hashOfOther));
    }
}