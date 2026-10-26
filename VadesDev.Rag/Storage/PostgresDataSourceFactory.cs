using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.Npgsql;

namespace VadesDev.Rag.Storage;

/// <summary>
/// Creates the PostgreSQL data source for the vector database, with pgvector support enabled.
/// </summary>
public static class PostgresDataSourceFactory
{
    /// <summary>
    /// Reads ConnectionStrings:Rag and builds the data source. Throws a clear error if the connection string is missing.
    /// </summary>
    public static NpgsqlDataSource Create(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rag");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Rag is missing. Set it in appsettings.Local.json or the ConnectionStrings__Rag environment variable.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();

        return builder.Build();
    }
}