// Path: VadesDev.Rag/Program.cs
using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Providers;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

var configuration = AppConfiguration.Build();
var provider = new AiProviderFactory(configuration).Create();
Console.WriteLine($"Provider: {provider.Name} (model {provider.EmbeddingModel}, MinScore {provider.MinScore})");

await using var dataSource = PostgresDataSourceFactory.Create(configuration);
var store = new PostgresChunkStore(dataSource);

var indexer = new ChunkIndexer(provider.EmbeddingGenerator);
var ingestion = new IngestionPipeline(new MarkdownFileReader(), indexer, store, provider.EmbeddingModel);
var folderIngestion = new FolderIngestion(ingestion, store, provider.EmbeddingModel);
var folder = Path.Combine(AppContext.BaseDirectory, "Data");

var started = await StartupGuard.RunAsync(async () =>
{
    await store.EnsureSchemaAsync();
    await folderIngestion.IngestFolderAsync(folder);
});

if (!started)
{
    return 1;
}

Console.WriteLine();

var retriever = new ChunkRetriever(store, provider.EmbeddingModel, new RagSettings(provider.MinScore));
var agent = new RagAgentFactory().Create(provider.ChatClient);
var pipeline = new RagPipeline(provider.EmbeddingGenerator, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();

return 0;