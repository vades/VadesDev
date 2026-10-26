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

// Phase 3: read the Markdown file, chunk it, then sanitize each chunk.
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);
var chunks = DocumentChunker.Chunk(document)
    .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
    .ToArray();

Console.WriteLine("Indexing...");
var index = await new ChunkIndexer(provider.EmbeddingGenerator).BuildIndexAsync(chunks);
await store.ReplaceChunksAsync(provider.EmbeddingModel, index);
Console.WriteLine($"Stored {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

var retriever = new ChunkRetriever(store, provider.EmbeddingModel, new RagSettings(provider.MinScore));
var agent = new RagAgentFactory().Create(provider.ChatClient);
var pipeline = new RagPipeline(provider.EmbeddingGenerator, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();