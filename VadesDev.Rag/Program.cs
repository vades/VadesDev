using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using VadesDev.Rag.Configuration;

var configuration = AppConfiguration.Build();
var connectionString = configuration.GetConnectionString("Rag")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Rag in appsettings.Local.json.");

var builder = new NpgsqlDataSourceBuilder(connectionString);
builder.UseVector();
await using var dataSource = builder.Build();

// A column without a fixed size: can it hold vectors of different dimensions?
await RunAsync("DROP TABLE IF EXISTS rag.smoke_test");
await RunAsync("CREATE TABLE rag.smoke_test (id int PRIMARY KEY, embedding vector)");
await RunAsync("INSERT INTO rag.smoke_test VALUES (1, $1)", new Vector(new float[] { 1, 2, 3 }));
await RunAsync("INSERT INTO rag.smoke_test VALUES (2, $1)", new Vector(new float[] { 3, 2, 1 }));
await RunAsync("INSERT INTO rag.smoke_test VALUES (3, $1)", new Vector(new float[] { 1, 2, 3, 4, 5 }));

// Read a vector back
await using (var read = dataSource.CreateCommand("SELECT embedding FROM rag.smoke_test WHERE id = 1"))
{
    var stored = (Vector)(await read.ExecuteScalarAsync())!;
    Console.WriteLine(string.Join(", ", stored.ToArray()));
}

// Search with a filter on the dimension: similarity = 1 - cosine distance
await using (var search = dataSource.CreateCommand(
    "SELECT id, 1 - (embedding <=> $1) AS similarity FROM rag.smoke_test " +
    "WHERE vector_dims(embedding) = 3 ORDER BY embedding <=> $1"))
{
    search.Parameters.AddWithValue(new Vector(new float[] { 1, 2, 3 }));
    await using var reader = await search.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        Console.WriteLine($"id={reader.GetInt32(0)} similarity={reader.GetDouble(1):F4}");
    }
}

// The same search without the filter: rows with another dimension must cause an error
try
{
    await using var unfiltered = dataSource.CreateCommand("SELECT id, embedding <=> $1 FROM rag.smoke_test");
    unfiltered.Parameters.AddWithValue(new Vector(new float[] { 1, 2, 3 }));
    await using var reader = await unfiltered.ExecuteReaderAsync();

    while (await reader.ReadAsync()) { }
}
catch (PostgresException exception)
{
    Console.WriteLine($"Without the filter: {exception.MessageText}");
}

await RunAsync("DROP TABLE rag.smoke_test");

async Task RunAsync(string sql, params object[] values)
{
    await using var command = dataSource.CreateCommand(sql);

    foreach (var value in values)
    {
        command.Parameters.AddWithValue(value);
    }

    await command.ExecuteNonQueryAsync();
}