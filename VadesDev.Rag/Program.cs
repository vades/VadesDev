using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

Console.WriteLine(await StartupGuard.RunAsync(() => Task.CompletedTask));

Console.WriteLine(await StartupGuard.RunAsync(
    () => Task.FromException(new HttpRequestException("no connection"))));

Console.WriteLine(await StartupGuard.RunAsync(
    () => Task.FromException(new NpgsqlException("no connection", new SocketException()))));

Console.WriteLine(await StartupGuard.RunAsync(
    () => Task.FromException(new DirectoryNotFoundException("Knowledge folder not found: C:\\nope"))));

try
{
    await StartupGuard.RunAsync(() => Task.FromException(new InvalidOperationException("bug")));
}
catch (InvalidOperationException exception)
{
    Console.WriteLine($"not caught, as intended: {exception.Message}");
}