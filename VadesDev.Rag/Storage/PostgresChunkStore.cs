using Npgsql;
using Pgvector;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Storage;

/// <summary>
/// Stores chunks and their vectors in PostgreSQL (schema rag) and finds the nearest chunks for a question vector.
/// Vectors from different embedding models share one table and are kept apart by the model name.
/// </summary>
public class PostgresChunkStore
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS rag.chunks (
            embedding_model text   NOT NULL,
            id              text   NOT NULL,
            source          text   NOT NULL,
            content         text   NOT NULL,
            embedding       vector NOT NULL,
            PRIMARY KEY (embedding_model, id)
        )
        """;

    private const string DeleteSql =
        "DELETE FROM rag.chunks WHERE embedding_model = $1 AND source = ANY($2)";

    private const string InsertSql =
        "INSERT INTO rag.chunks (embedding_model, id, source, content, embedding) VALUES ($1, $2, $3, $4, $5)";

    private const string SearchSql = """
        SELECT id, source, content, 1 - (embedding <=> $1) AS similarity
        FROM rag.chunks
        WHERE embedding_model = $2 AND vector_dims(embedding) = $3
        ORDER BY embedding <=> $1
        LIMIT $4
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresChunkStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Creates the table rag.chunks if it does not exist yet. Call once at startup.
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        await using var command = _dataSource.CreateCommand(CreateTableSql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Replaces the stored chunks of the given sources for one embedding model, in a single transaction.
    /// </summary>
    public async Task ReplaceChunksAsync(string embeddingModel, List<IndexedChunk> chunks)
    {
        var sources = chunks.Select(c => c.Chunk.Source).Distinct().ToArray();

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await DeleteSourcesAsync(connection, transaction, embeddingModel, sources);

        foreach (var chunk in chunks)
        {
            await InsertChunkAsync(connection, transaction, embeddingModel, chunk);
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// Returns the nearest chunks of one embedding model, best first, with similarity = 1 - cosine distance.
    /// Applies no MinScore: that decision belongs to the retriever.
    /// </summary>
    public async Task<List<SearchHit>> SearchAsync(string embeddingModel, ReadOnlyMemory<float> queryVector, int limit)
    {
        await using var command = _dataSource.CreateCommand(SearchSql);
        command.Parameters.AddWithValue(new Vector(queryVector));
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(queryVector.Length);
        command.Parameters.AddWithValue(limit);

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var chunk = new DocumentChunk(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            hits.Add(new SearchHit(chunk, (float)reader.GetDouble(3)));
        }

        return hits;
    }

    /// <summary>
    /// Deletes the existing rows of the given sources for one embedding model.
    /// </summary>
    private static async Task DeleteSourcesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string embeddingModel, string[] sources)
    {
        await using var command = new NpgsqlCommand(DeleteSql, connection, transaction);
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(sources);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts one chunk with its vector.
    /// </summary>
    private static async Task InsertChunkAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string embeddingModel, IndexedChunk chunk)
    {
        await using var command = new NpgsqlCommand(InsertSql, connection, transaction);
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(chunk.Chunk.Id);
        command.Parameters.AddWithValue(chunk.Chunk.Source);
        command.Parameters.AddWithValue(chunk.Chunk.Text);
        command.Parameters.AddWithValue(new Vector(chunk.Vector));
        await command.ExecuteNonQueryAsync();
    }
}