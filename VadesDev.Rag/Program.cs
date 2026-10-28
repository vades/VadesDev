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

// Phase 5: the vector database (PostgreSQL + pgvector).
await using var dataSource = PostgresDataSourceFactory.Create(configuration);
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

// Read, chunk, sanitize, embed and store the file, unless it is unchanged.
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var indexer = new ChunkIndexer(provider.EmbeddingGenerator);
await new IngestionPipeline(new MarkdownFileReader(), indexer, store, provider.EmbeddingModel).IngestAsync(path);
Console.WriteLine();

var retriever = new ChunkRetriever(store, provider.EmbeddingModel, new RagSettings(provider.MinScore));
var agent = new RagAgentFactory().Create(provider.ChatClient);
var pipeline = new RagPipeline(provider.EmbeddingGenerator, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();