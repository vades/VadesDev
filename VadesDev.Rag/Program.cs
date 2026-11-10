using Microsoft.Extensions.Configuration;
using Npgsql;
using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

var connectionString = AppConfiguration.Build().GetConnectionString("Rag")!;

await TryAsync("working connection", connectionString);
await TryAsync("wrong password", new NpgsqlConnectionStringBuilder(connectionString) { Password = "wrong" }.ConnectionString);
await TryAsync("wrong database", new NpgsqlConnectionStringBuilder(connectionString) { Database = "nope" }.ConnectionString);
await TryAsync("wrong port", new NpgsqlConnectionStringBuilder(connectionString) { Port = 5999 }.ConnectionString);
await TryAsync("SQL mistake (must NOT be recognized)", connectionString, "SELEC 1");

static async Task TryAsync(string label, string connectionString, string sql = "SELECT 1")
{
    Console.WriteLine($"--- {label}");

    try
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteScalarAsync();
        Console.WriteLine("OK, no error");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"{exception.GetType().Name} (inner: {exception.InnerException?.GetType().Name ?? "none"})");
        var recognized = DatabaseErrors.IsDatabaseError(exception);
        Console.WriteLine($"recognized: {recognized}");

        if (recognized)
        {
            Console.WriteLine(DatabaseErrors.Describe(exception));
        }
    }
}