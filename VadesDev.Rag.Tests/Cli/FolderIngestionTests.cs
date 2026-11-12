using Microsoft.Extensions.AI;
using Npgsql;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Tests.Cli;

/// <summary>
/// Tests for FolderIngestion against the real database in Docker, with the fake embedder and a temporary folder.
/// Every test instance uses its own made-up model names and its own folder, so real rows and files are never touched.
/// If the database is not reachable, the tests are skipped.
/// </summary>
public class FolderIngestionTests : IAsyncLifetime
{
    private readonly string _model = $"test-{Guid.NewGuid():N}";
    private readonly string _otherModel = $"test-{Guid.NewGuid():N}";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"folder-ingestion-{Guid.NewGuid():N}");
    private readonly NpgsqlDataSource _dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
    private readonly HashingEmbeddingGenerator _embedder = new();
    private readonly PostgresChunkStore _store;
    private readonly ChunkIndexer _indexer;

    public FolderIngestionTests()
    {
        _store = new PostgresChunkStore(_dataSource);
        _indexer = new ChunkIndexer(_embedder);
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
    /// Builds the folder ingestion for one embedding model, with the real pipeline and the fake embedder.
    /// </summary>
    private FolderIngestion CreateFolderIngestion(string model)
    {
        var pipeline = new IngestionPipeline(new MarkdownFileReader(), _indexer, _store, model);

        return new FolderIngestion(pipeline, _store, model);
    }

    /// <summary>
    /// Runs the folder ingestion for the main test model on the temporary folder.
    /// </summary>
    private Task IngestAsync()
    {
        return CreateFolderIngestion(_model).IngestFolderAsync(_folder);
    }

    /// <summary>
    /// Writes a file into the temporary folder.
    /// </summary>
    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_folder, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Embeds one text with the fake embedder, using the cancellation token of the running test.
    /// </summary>
    private async Task<ReadOnlyMemory<float>> EmbedAsync(string text)
    {
        return await _embedder.GenerateVectorAsync(text, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns the ids of all stored chunks of one model (4096 numbers each), sorted, for example "a.md-1".
    /// </summary>
    private async Task<string[]> StoredIdsAsync(string model)
    {
        var hits = await _store.SearchAsync(model, await EmbedAsync("query"), 100);

        return [.. hits.Select(h => h.Chunk.Id).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every Markdown file in the folder is stored.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_StoresEveryMarkdownFile()
    {
        Write("a.md", "# A\n\nalpha text.");
        Write("b.md", "# B\n\nbeta text.");

        await IngestAsync();

        Assert.Equal(["a.md-1", "b.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// Files with other extensions are ignored.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_IgnoresOtherFileTypes()
    {
        Write("a.md", "# A\n\nalpha text.");
        Write("notes.txt", "gamma text.");

        await IngestAsync();

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// Only the top level of the folder is read. A Markdown file in a subfolder is ignored.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_IgnoresSubfolders()
    {
        Write("a.md", "# A\n\nalpha text.");
        Write(Path.Combine("sub", "b.md"), "# B\n\nbeta text.");

        await IngestAsync();

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// A file that gets shorter replaces its old rows: 2 chunks become 1 chunk, not 3 rows.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_ReplacesRowsOfChangedFile()
    {
        Write("a.md", "# A\n\nalpha text.\n\nbeta more.");
        await IngestAsync();
        Assert.Equal(["a.md-1", "a.md-2"], await StoredIdsAsync(_model));

        Write("a.md", "# A\n\nalpha text.");
        await IngestAsync();

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// The rows of a file that was deleted from the folder are removed.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_RemovesRowsOfDeletedFile()
    {
        Write("a.md", "# A\n\nalpha text.");
        Write("b.md", "# B\n\nbeta text.");
        await IngestAsync();

        File.Delete(Path.Combine(_folder, "b.md"));
        await IngestAsync();

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// The rows of a file that became empty are removed, because an empty file has no chunks.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_RemovesRowsOfEmptiedFile()
    {
        Write("a.md", "# A\n\nalpha text.");
        Write("b.md", "# B\n\nbeta text.");
        await IngestAsync();

        Write("b.md", "");
        await IngestAsync();

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// A folder that exists but has no files removes every row of the model.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_RemovesAllRowsWhenFolderIsEmpty()
    {
        Write("a.md", "# A\n\nalpha text.");
        await IngestAsync();

        File.Delete(Path.Combine(_folder, "a.md"));
        await IngestAsync();

        Assert.Empty(await StoredIdsAsync(_model));
    }

    /// <summary>
    /// A folder that does not exist stops with an error before anything is deleted.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_ThrowsForMissingFolderAndKeepsRows()
    {
        Write("a.md", "# A\n\nalpha text.");
        await IngestAsync();

        var missing = Path.Combine(_folder, "missing");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => CreateFolderIngestion(_model).IngestFolderAsync(missing));

        Assert.Equal(["a.md-1"], await StoredIdsAsync(_model));
    }

    /// <summary>
    /// Removing rows for one embedding model never removes the rows of another model.
    /// </summary>
    [Fact]
    public async Task IngestFolderAsync_DoesNotTouchOtherModels()
    {
        Write("a.md", "# A\n\nalpha text.");
        await IngestAsync();
        await CreateFolderIngestion(_otherModel).IngestFolderAsync(_folder);

        File.Delete(Path.Combine(_folder, "a.md"));
        await IngestAsync();

        Assert.Empty(await StoredIdsAsync(_model));
        Assert.Equal(["a.md-1"], await StoredIdsAsync(_otherModel));
    }
}