using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Providers;
using VadesDev.Rag.Search;

// Phase 4: the provider (Gemini or Ollama) is chosen in appsettings.json (Rag:Provider).
var configuration = AppConfiguration.Build();
var provider = new AiProviderFactory(configuration).Create();
Console.WriteLine($"Provider: {provider.Name} (MinScore {provider.MinScore})");

// Phase 3: read the Markdown file, chunk it, then sanitize each chunk.
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);
var chunks = DocumentChunker.Chunk(document)
    .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
    .ToArray();

Console.WriteLine("Indexing...");
var index = await new ChunkIndexer(provider.EmbeddingGenerator).BuildIndexAsync(chunks);
Console.WriteLine($"Indexed {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

var retriever = new ChunkRetriever(index, new RagSettings(provider.MinScore));
var agent = new RagAgentFactory().Create(provider.ChatClient);
var pipeline = new RagPipeline(provider.EmbeddingGenerator, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();