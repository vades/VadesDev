using Microsoft.Extensions.Configuration;
using Pgvector;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Storage;

await using var dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());

await using var command = dataSource.CreateCommand("SELECT '[1,2,3]'::vector <=> $1");
command.Parameters.AddWithValue(new Vector(new float[] { 3, 2, 1 }));
Console.WriteLine(await command.ExecuteScalarAsync());

try
{
    PostgresDataSourceFactory.Create(new ConfigurationBuilder().Build());
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message);
}