using Microsoft.Extensions.AI;
using Npgsql;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Models;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;
using VadesDev.Rag.Tests.Fakes;

namespace VadesDev.Rag.Tests.Cli;

/// <summary>
/// Tests for IngestionPipeline against the real database in Docker. A spy around the fake embedder counts
/// the embedding calls. Every test instance uses its own made-up model names and its own folder.
/// If the database is not reachable, the tests are skipped.
/// </summary>
public class IngestionPipelineTests : IAsyncLifetime
{
    private readonly string _model = $"test-{Guid.NewGuid():N}";
    private readonly string _otherModel = $"test-{Guid.NewGuid():N}";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ingestion-pipeline-{Guid.NewGuid():N}");
    private readonly NpgsqlDataSource _dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
    private readonly SpyEmbeddingGenerator _spy = new();
    private readonly HashingEmbeddingGenerator _queryEmbedder = new();
    private readonly PostgresChunkStore _store;
    private readonly IngestionPipeline _pipeline;

    public IngestionPipelineTests()
    {
        _store = new PostgresChunkStore(_dataSource);
        _pipeline = CreatePipeline(_model);
        Directory.CreateDirectory(_folder);
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
    /// Removes the rows of both test models, closes the data source and deletes the temporary folder.
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
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    /// Builds a pipeline for one embedding model. All pipelines share the spy, so its counters add up.
    /// </summary>
    private IngestionPipeline CreatePipeline(string model)
    {
        return new IngestionPipeline(new MarkdownFileReader(), new ChunkIndexer(_spy), _store, model);
    }

    /// <summary>
    /// Writes a file into the temporary folder and returns its path.
    /// </summary>
    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);

        File.WriteAllText(path, text);

        return path;
    }

    /// <summary>
    /// Returns all stored chunks of one model (4096 numbers each), sorted by id. The question is embedded with
    /// a separate embedder, so the spy only counts the embeddings of the ingestion.
    /// </summary>
    private async Task<SearchHit[]> StoredAsync(string model)
    {
        var vector = await _queryEmbedder.GenerateVectorAsync("query", cancellationToken: TestContext.Current.CancellationToken);
        var hits = await _store.SearchAsync(model, vector, 100);

        return [.. hits.OrderBy(h => h.Chunk.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A new file is stored under its file name, and the result is true.
    /// </summary>
    [Fact]
    public async Task IngestAsync_StoresChunksAndReturnsTrueForNewFile()
    {
        var path = Write("a.md", "alpha text.");

        Assert.True(await _pipeline.IngestAsync(path));

        var stored = Assert.Single(await StoredAsync(_model));
        Assert.Equal("a.md-1", stored.Chunk.Id);
        Assert.Equal("alpha text.", stored.Chunk.Text);
        Assert.Equal(1, _spy.CallCount);
    }

    /// <summary>
    /// An empty file and a file with only whitespace have no chunks: the result is false,
    /// nothing is embedded and nothing is stored.
    /// </summary>
    [Fact]
    public async Task IngestAsync_ReturnsFalseAndStoresNothingForFileWithoutContent()
    {
        Assert.False(await _pipeline.IngestAsync(Write("empty.md", "")));
        Assert.False(await _pipeline.IngestAsync(Write("blank.md", "  \r\n\r\n \t ")));

        Assert.Equal(0, _spy.CallCount);
        Assert.Empty(await StoredAsync(_model));
    }

    /// <summary>
    /// A second run with the same file returns true but makes no embedding call.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SkipsEmbeddingForUnchangedFile()
    {
        var path = Write("a.md", "alpha text.");

        Assert.True(await _pipeline.IngestAsync(path));
        Assert.Equal(1, _spy.CallCount);

        Assert.True(await _pipeline.IngestAsync(path));
        Assert.Equal(1, _spy.CallCount);
    }

    /// <summary>
    /// A changed file is embedded again and its stored text is replaced.
    /// </summary>
    [Fact]
    public async Task IngestAsync_EmbedsAgainWhenFileChanges()
    {
        var path = Write("a.md", "alpha text.");
        await _pipeline.IngestAsync(path);

        Write("a.md", "beta text.");
        await _pipeline.IngestAsync(path);

        Assert.Equal(2, _spy.CallCount);
        Assert.Equal("beta text.", Assert.Single(await StoredAsync(_model)).Chunk.Text);
    }

    /// <summary>
    /// All chunks of a file are embedded in one call: 3 paragraphs, 1 call, 3 texts.
    /// </summary>
    [Fact]
    public async Task IngestAsync_EmbedsAllChunksOfFileInOneCall()
    {
        var path = Write("a.md", "alpha one.\n\nbeta two.\n\ngamma three.");

        await _pipeline.IngestAsync(path);

        Assert.Equal(1, _spy.CallCount);
        Assert.Equal(3, _spy.TextCount);
        Assert.Equal(3, (await StoredAsync(_model)).Length);
    }

    /// <summary>
    /// The text is chunked first (the heading is joined with its paragraph) and sanitized after:
    /// the stored text has no # marker, no link and no ** markers.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SanitizesTextBeforeStoring()
    {
        var path = Write("a.md", "# Support\n\nSee [our help page](https://example.com/help) for **more**.");

        await _pipeline.IngestAsync(path);

        var stored = Assert.Single(await StoredAsync(_model));
        Assert.Equal("Support\nSee our help page for more.", stored.Chunk.Text);
    }

    /// <summary>
    /// If the embedding fails (AI server not running), the error is thrown and the old rows stay as they were.
    /// </summary>
    [Fact]
    public async Task IngestAsync_KeepsOldRowsWhenEmbeddingFails()
    {
        var path = Write("a.md", "alpha text.");
        await _pipeline.IngestAsync(path);

        Write("a.md", "beta text.");
        _spy.Failure = new HttpRequestException("no connection");

        await Assert.ThrowsAsync<HttpRequestException>(() => _pipeline.IngestAsync(path));

        Assert.Equal("alpha text.", Assert.Single(await StoredAsync(_model)).Chunk.Text);
    }

    /// <summary>
    /// The same unchanged file is embedded again for another embedding model, and both models have their rows.
    /// </summary>
    [Fact]
    public async Task IngestAsync_EmbedsAgainForOtherModel()
    {
        var path = Write("a.md", "alpha text.");

        await _pipeline.IngestAsync(path);
        await CreatePipeline(_otherModel).IngestAsync(path);

        Assert.Equal(2, _spy.CallCount);
        Assert.Single(await StoredAsync(_model));
        Assert.Single(await StoredAsync(_otherModel));
    }
}